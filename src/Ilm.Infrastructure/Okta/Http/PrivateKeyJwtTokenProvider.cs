using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ilm.Infrastructure.Okta.Http;

public interface IOktaAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// OAuth 2.0 client-credentials with private_key_jwt for the Okta service app. The signing key comes from
/// the Windows machine certificate store and is never written to configuration or source.
/// UNVERIFIED against a real Okta org in this repository.
/// </summary>
public sealed class PrivateKeyJwtTokenProvider(IHttpClientFactory httpClients, IOptions<OktaOptions> options, TimeProvider time) : IOktaAccessTokenProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTime expiresUtc;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (token is not null && now < expiresUtc)
        {
            return token;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (token is not null && now < expiresUtc)
            {
                return token;
            }

            var o = options.Value;
            var tokenEndpoint = o.OrgUrl.TrimEnd('/') + "/oauth2/v1/token";
            var assertion = CreateClientAssertion(o, tokenEndpoint, now);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = string.Join(' ', o.Scopes),
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = assertion,
            });
            using var response = await httpClients.CreateClient("okta-token").PostAsync(new Uri(tokenEndpoint), content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Okta returned an empty token response.");
            token = body.AccessToken;
            expiresUtc = now.AddSeconds(Math.Max(60, body.ExpiresIn - 60));
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string CreateClientAssertion(OktaOptions o, string audience, DateTime now)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(o.SigningCertificateThumbprint))
        {
            throw new InvalidOperationException("Live Okta mode requires a signing certificate thumbprint in the Windows machine store.");
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var cert = store.Certificates.Find(X509FindType.FindByThumbprint, o.SigningCertificateThumbprint, validOnly: false).FirstOrDefault()
            ?? throw new InvalidOperationException("The Okta signing certificate was not found in LocalMachine\\My.");
        using var rsa = cert.GetRSAPrivateKey() ?? throw new InvalidOperationException("The Okta signing certificate has no RSA private key.");
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = o.SigningKeyId }, SecurityAlgorithms.RsaSha256);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.ServiceClientId,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = o.ServiceClientId, ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = credentials,
        });
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
