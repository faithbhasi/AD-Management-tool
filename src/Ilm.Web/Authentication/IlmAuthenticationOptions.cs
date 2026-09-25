namespace Ilm.Web.Authentication;

public enum AuthenticationMode
{
    Okta = 0,
    MockOidc,
}

/// <summary>
/// Operator authentication settings. There is no password field for operators and no AD bind option.
/// The OIDC client secret is read from an environment variable (vault-injected), never from files.
/// </summary>
public sealed class IlmAuthenticationOptions
{
    public const string SectionName = "Ilm:Authentication";

    public AuthenticationMode Mode { get; set; } = AuthenticationMode.Okta;

    /// <summary>Exact issuer, for example https://your-org.okta.com/oauth2/default. Changing it is an identity migration.</summary>
    public string Issuer { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecretEnvironmentVariable { get; set; } = "ILM_OIDC_CLIENT_SECRET";

    /// <summary>The exact public base URL (https). The redirect URI is derived from it, never from the request host.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>Exact redirect URIs registered with the IdP. The computed redirect URI must be one of them.</summary>
    public List<string> AllowedRedirectUris { get; set; } = [];

    public int SessionMinutes { get; set; } = 30;

    public string RedirectUri => PublicBaseUrl.TrimEnd('/') + CallbackPath;

    public string PostLogoutRedirectUri => PublicBaseUrl.TrimEnd('/') + SignedOutCallbackPath;
}

public static class IlmClaimTypes
{
    public const string Subject = "sub";
    public const string Issuer = "ilm:iss";
    public const string Name = "name";
    public const string Email = "email";
    public const string AppUserId = "ilm:appuser_id";
    public const string Role = "ilm:role";
    public const string ScopePrefix = "ilm:scope:";
    public const string RoleSource = "ilm:role_source";
    public const string RoleResolutionFailed = "ilm:role_failure";
}
