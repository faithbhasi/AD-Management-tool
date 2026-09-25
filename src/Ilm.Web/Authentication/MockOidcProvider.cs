using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ilm.Infrastructure.Development;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ilm.Web.Authentication;

/// <summary>
/// DEVELOPMENT ONLY. A minimal in-process OpenID Connect provider (authorization code + PKCE S256) that signs
/// ID tokens for fictional operators. Its signing key and client secret are generated at startup and held in memory.
/// Startup refuses to enable it outside Development or Testing.
/// </summary>
public sealed class MockOidcProvider : IDisposable
{
    public const string PathBase = "/mock-oidc";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, PendingCode> codes = new(StringComparer.Ordinal);

    public MockOidcProvider(IlmAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        ClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        SigningKey = new RsaSecurityKey(rsa) { KeyId = "mock-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)) };
    }

    public IlmAuthenticationOptions Options { get; }

    public string ClientSecret { get; }

    public RsaSecurityKey SigningKey { get; }

    public string Issuer => Options.Issuer;

    public string AuthorizationEndpoint => Issuer + "/authorize";

    public string TokenEndpoint => Issuer + "/token";

    public string EndSessionEndpoint => Issuer + "/logout";

    public OpenIdConnectConfiguration CreateConfiguration()
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
            EndSessionEndpoint = EndSessionEndpoint,
            JwksUri = Issuer + "/jwks",
        };
        configuration.SigningKeys.Add(SigningKey);
        configuration.ResponseTypesSupported.Add("code");
        configuration.CodeChallengeMethodsSupported.Add("S256");
        return configuration;
    }

    /// <summary>Validates an authorization request and, when the operator chooses a fictional user, issues a code.</summary>
    public (string? RedirectUrl, string? Error) Authorize(IReadOnlyDictionary<string, string> query, string subject)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clientId = query.GetValueOrDefault("client_id");
        var redirectUri = query.GetValueOrDefault("redirect_uri");
        var responseType = query.GetValueOrDefault("response_type");
        var challenge = query.GetValueOrDefault("code_challenge");
        var method = query.GetValueOrDefault("code_challenge_method");
        if (clientId != Options.ClientId)
        {
            return (null, "unknown client_id");
        }

        if (redirectUri is null || !Options.AllowedRedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            return (null, "redirect_uri is not registered");
        }

        if (responseType != "code" || string.IsNullOrEmpty(challenge) || method != "S256")
        {
            return (null, "authorization code flow with PKCE S256 is required");
        }

        if (FictionalOkta.Operators.All(o => o.Subject != subject))
        {
            return (null, "unknown subject");
        }

        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        codes[code] = new PendingCode(subject, query.GetValueOrDefault("nonce"), challenge, redirectUri, DateTime.UtcNow.AddMinutes(2));
        var state = query.GetValueOrDefault("state");
        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return ($"{redirectUri}{separator}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state ?? string.Empty)}", null);
    }

    /// <summary>Token endpoint: confidential client authentication, redirect URI match, PKCE verification.</summary>
    public (string? Json, string? Error) Token(IReadOnlyDictionary<string, string> form, string? basicAuthorization)
    {
        ArgumentNullException.ThrowIfNull(form);
        var (clientId, clientSecret) = ReadClientCredentials(form, basicAuthorization);
        if (clientId != Options.ClientId || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(clientSecret ?? string.Empty), Encoding.UTF8.GetBytes(ClientSecret)))
        {
            return (null, "invalid_client");
        }

        if (form.GetValueOrDefault("grant_type") != "authorization_code")
        {
            return (null, "unsupported_grant_type");
        }

        if (!codes.TryRemove(form.GetValueOrDefault("code") ?? string.Empty, out var pending) || pending.ExpiresUtc < DateTime.UtcNow)
        {
            return (null, "invalid_grant");
        }

        if (pending.RedirectUri != form.GetValueOrDefault("redirect_uri"))
        {
            return (null, "invalid_grant");
        }

        var verifier = form.GetValueOrDefault("code_verifier") ?? string.Empty;
        var computed = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        if (!string.Equals(computed, pending.CodeChallenge, StringComparison.Ordinal))
        {
            return (null, "invalid_grant");
        }

        var op = FictionalOkta.Operators.First(o => o.Subject == pending.Subject);
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = op.Subject,
            ["name"] = op.DisplayName,
            ["email"] = op.Email,
            ["preferred_username"] = op.Email,
            // Group NAMES are included deliberately: ILM must ignore them for authorisation.
            ["groups"] = op.TokenGroupNames.ToArray(),
        };
        if (pending.Nonce is not null)
        {
            claims["nonce"] = pending.Nonce;
        }

        var idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Options.ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["token_type"] = "Bearer",
            ["id_token"] = idToken,
            ["access_token"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            ["expires_in"] = 300,
        });
        return (json, null);
    }

    public void Dispose() => rsa.Dispose();

    private static (string? ClientId, string? Secret) ReadClientCredentials(IReadOnlyDictionary<string, string> form, string? basic)
    {
        if (basic is not null && basic.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(basic[6..].Trim()));
            var i = decoded.IndexOf(':', StringComparison.Ordinal);
            if (i > 0)
            {
                return (Uri.UnescapeDataString(decoded[..i]), Uri.UnescapeDataString(decoded[(i + 1)..]));
            }
        }

        return (form.GetValueOrDefault("client_id"), form.GetValueOrDefault("client_secret"));
    }

    private sealed record PendingCode(string Subject, string? Nonce, string CodeChallenge, string RedirectUri, DateTime ExpiresUtc);
}

/// <summary>Routes the OIDC handler's back-channel token request to the in-process mock provider (no network, no TLS trust changes).</summary>
public sealed class MockOidcBackchannelHandler(MockOidcProvider provider) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Method != HttpMethod.Post || request.RequestUri?.ToString() != provider.TokenEndpoint || request.Content is null)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty, StringComparer.Ordinal);
        var (json, error) = provider.Token(form, request.Headers.Authorization?.ToString());
        return error is null
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json!, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"error\":\"{error}\"}}", Encoding.UTF8, "application/json") };
    }
}
