using System.Net;
using Ilm.Application.Abstractions;
using Ilm.Application.Approvals;
using Ilm.Application.Security;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Infrastructure.Development;
using Ilm.Infrastructure.Okta.Mock;
using Ilm.TestSupport;
using Ilm.Web.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ilm.SecurityTests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly IlmWebFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Operator_is_keyed_by_exact_issuer_and_subject()
    {
        await factory.SignInAsync(FictionalIds.OperatorOlivia);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().AppUsers.SingleAsync(u => u.Subject == FictionalIds.OperatorOlivia);
        Assert.Equal(IlmWebFactory.Issuer, user.Issuer);
    }

    [Fact]
    public async Task Changed_email_does_not_create_a_new_identity()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<AppUserService>();
        var first = await users.SignInAsync(IlmWebFactory.Issuer, "00uEmailChange000001", "Pat", "pat.old@example.test", CancellationToken.None);
        var second = await users.SignInAsync(IlmWebFactory.Issuer, "00uEmailChange000001", "Pat", "pat.new@example.test", CancellationToken.None);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("pat.new@example.test", second.LastSeenEmail);

        // Same email, different subject: a different operator. Email is never an identity key.
        var other = await users.SignInAsync(IlmWebFactory.Issuer, "00uEmailChange000002", "Pat", "pat.new@example.test", CancellationToken.None);
        Assert.NotEqual(first.Id, other.Id);
    }

    [Fact]
    public async Task Issuer_change_is_an_identity_migration_requiring_security_approval()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<AppUserService>();
        var old = await users.SignInAsync(IlmWebFactory.Issuer, "00uIssuerMove0000001", "Robin", "robin@example.test", CancellationToken.None);
        var moved = await users.SignInAsync("https://okta.example.test/oauth2/aus000000000000000001", "00uIssuerMove0000001", "Robin", "robin@example.test", CancellationToken.None);
        Assert.NotEqual(old.Id, moved.Id);

        var casey = await users.SignInAsync(IlmWebFactory.Issuer, FictionalIds.ConfigCasey, "Casey Config", null, CancellationToken.None);
        var requester = new ActorContext { AppUserId = casey.Id, Issuer = IlmWebFactory.Issuer, Subject = FictionalIds.ConfigCasey, DisplayName = "Casey" };
        var migrations = sp.GetRequiredService<IssuerMigrationService>();
        var migration = await migrations.ProposeAsync(old.Id, moved.Id, "Authorization server change", requester, 1, CancellationToken.None);
        await Assert.ThrowsAsync<DomainException>(() => migrations.ApplyAsync(migration.Id, requester, CancellationToken.None));

        var sasha = await users.SignInAsync(IlmWebFactory.Issuer, FictionalIds.SecuritySasha, "Sasha", null, CancellationToken.None);
        var approver = new ActorContext
        {
            AppUserId = sasha.Id, Issuer = IlmWebFactory.Issuer, Subject = FictionalIds.SecuritySasha, DisplayName = "Sasha", RolesFreshlyResolved = true,
            Roles = new Dictionary<Domain.Security.AppRole, IReadOnlyCollection<string>> { [Domain.Security.AppRole.SecurityApprover] = [] },
        };
        var db = sp.GetRequiredService<IIlmDbContext>();
        var approval = await db.Approvals.SingleAsync(a => a.Id == migration.ApprovalId);
        await sp.GetRequiredService<ApprovalService>().DecideAsync(approval.Id, true, null, approver, approval.ContentHash, CancellationToken.None);
        await db.SaveChangesAsync();
        await migrations.ApplyAsync(migration.Id, approver, CancellationToken.None);
        Assert.Equal(Domain.Security.AppUserStatus.Migrated, (await db.AppUsers.SingleAsync(u => u.Id == old.Id)).Status);
    }

    [Fact]
    public async Task Tokens_with_wrong_issuer_audience_or_signature_are_rejected()
    {
        await factory.SignInAsync(FictionalIds.OperatorOlivia);
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        var mock = factory.Services.GetRequiredService<MockOidcProvider>();
        var parameters = oidc.TokenValidationParameters.Clone();
        parameters.IssuerSigningKey = mock.SigningKey;
        var handler = new JsonWebTokenHandler();
        string Token(string issuer, string audience, SecurityKey key) => handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = FictionalIds.OperatorOlivia },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });

        Assert.True((await handler.ValidateTokenAsync(Token(IlmWebFactory.Issuer, "ilm-portal-test", mock.SigningKey), parameters)).IsValid);
        Assert.False((await handler.ValidateTokenAsync(Token("https://localhost/mock-oidc/", "ilm-portal-test", mock.SigningKey), parameters)).IsValid);
        Assert.False((await handler.ValidateTokenAsync(Token("https://attacker.example.test", "ilm-portal-test", mock.SigningKey), parameters)).IsValid);
        Assert.False((await handler.ValidateTokenAsync(Token(IlmWebFactory.Issuer, "another-client", mock.SigningKey), parameters)).IsValid);
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        Assert.False((await handler.ValidateTokenAsync(Token(IlmWebFactory.Issuer, "ilm-portal-test", new RsaSecurityKey(rsa)), parameters)).IsValid);
        Assert.True(oidc.UsePkce);
        Assert.Equal("code", oidc.ResponseType);
        Assert.False(oidc.SaveTokens);
    }

    [Fact]
    public async Task Mutable_group_name_in_token_does_not_grant_a_privileged_role()
    {
        var mallory = await factory.SignInAsync(FictionalIds.DecoyMallory);
        var me = await (await mallory.GetAsync("/Account/Me")).Content.ReadAsStringAsync();
        Assert.Contains("No roles.", me, StringComparison.Ordinal);
        var admin = await mallory.GetAsync("/Admin");
        Assert.Contains("/Account/AccessDenied", admin.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_side_membership_failure_denies_privileged_roles()
    {
        factory.Services.GetRequiredService<MockOktaOrg>().ApiAvailable = false;
        var sasha = await factory.SignInAsync(FictionalIds.SecuritySasha);
        var me = await (await sasha.GetAsync("/Account/Me")).Content.ReadAsStringAsync();
        Assert.Contains("Failed:", me, StringComparison.Ordinal);
        Assert.Contains("/Account/AccessDenied", (await sasha.GetAsync("/Admin")).Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_requests_are_challenged_and_cookies_are_hardened()
    {
        var anonymous = factory.Browser();
        var response = await anonymous.GetAsync("/People");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(IlmWebFactory.Issuer + "/authorize", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        var query = System.Web.HttpUtility.ParseQueryString(response.Headers.Location.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.Equal("https://localhost/signin-oidc", query["redirect_uri"]);

        var client = factory.Browser();
        var challenge = await client.GetAsync("/Account/SignIn");
        var authorize = challenge.Headers.Location!;
        var fields = System.Web.HttpUtility.ParseQueryString(authorize.Query);
        var dict = fields.AllKeys.ToDictionary(k => k!, k => fields[k]!);
        dict["subject"] = FictionalIds.OperatorOlivia;
        var code = await client.PostAsync("/mock-oidc/authorize", new FormUrlEncodedContent(dict));
        var callback = await client.GetAsync(code.Headers.Location);
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-ilm=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mock_provider_enforces_redirect_allowlist_and_pkce()
    {
        var client = factory.Browser();
        var evil = await client.GetAsync("/mock-oidc/authorize?client_id=ilm-portal-test&response_type=code&redirect_uri=https%3A%2F%2Fevil.example.test%2Fcb&code_challenge=x&code_challenge_method=S256&state=s");
        Assert.Equal(HttpStatusCode.BadRequest, evil.StatusCode);
        var noPkce = await client.GetAsync("/mock-oidc/authorize?client_id=ilm-portal-test&response_type=code&redirect_uri=https%3A%2F%2Flocalhost%2Fsignin-oidc&state=s");
        Assert.Equal(HttpStatusCode.BadRequest, noPkce.StatusCode);
        Assert.Throws<InvalidOperationException>(() => OidcSetup.EnsureAllowedRedirect(factory.Services.GetRequiredService<IlmAuthenticationOptions>(), "https://localhost:8443/signin-oidc"));
    }

    [Fact]
    public async Task Mock_token_endpoint_rejects_wrong_pkce_verifier_and_client_secret()
    {
        var provider = factory.Services.GetRequiredService<MockOidcProvider>();
        var (redirect, error) = provider.Authorize(new Dictionary<string, string>
        {
            ["client_id"] = "ilm-portal-test", ["redirect_uri"] = "https://localhost/signin-oidc", ["response_type"] = "code",
            ["code_challenge"] = Base64UrlEncoder.Encode(System.Security.Cryptography.SHA256.HashData("right-verifier"u8.ToArray())), ["code_challenge_method"] = "S256", ["state"] = "s",
        }, FictionalIds.OperatorOlivia);
        Assert.Null(error);
        var code = System.Web.HttpUtility.ParseQueryString(new Uri(redirect!).Query)["code"]!;
        var (json, tokenError) = provider.Token(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = "https://localhost/signin-oidc",
            ["client_id"] = "ilm-portal-test", ["client_secret"] = provider.ClientSecret, ["code_verifier"] = "wrong-verifier",
        }, null);
        Assert.Null(json);
        Assert.Equal("invalid_grant", tokenError);
        Assert.Equal("invalid_client", provider.Token(new Dictionary<string, string> { ["client_id"] = "ilm-portal-test", ["client_secret"] = "guess" }, null).Error);
    }

    [Fact]
    public async Task Approval_requests_cannot_be_decided_by_requester_even_through_http()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
            Assert.Equal(0, await db.Approvals.CountAsync(a => a.Status == ApprovalStatus.Approved));
        }

        var person = (await (await olivia.GetAsync("/People?q=Alex")).Content.ReadAsStringAsync());
        var id = System.Text.RegularExpressions.Regex.Match(person, "/People/Details/([0-9a-f-]{36})").Groups[1].Value;
        var created = await IlmWebFactory.PostFormAsync(olivia, $"/Leavers/New?personId={id}", $"/Leavers/New?personId={id}", new()
        {
            ["Reason"] = "Security test", ["TicketReference"] = "CHG-30001", ["Urgency"] = "Urgent", ["IdempotencyKey"] = Guid.NewGuid().ToString("N"),
        });
        var leaverUrl = created.Headers.Location!.ToString();
        var attempt = await IlmWebFactory.PostFormAsync(olivia, leaverUrl, leaverUrl + "?handler=Approve", new() { ["comment"] = "self" });
        Assert.Equal(HttpStatusCode.Redirect, attempt.StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<IIlmDbContext>().Approvals.CountAsync(a => a.Status == ApprovalStatus.Approved));
    }
}
