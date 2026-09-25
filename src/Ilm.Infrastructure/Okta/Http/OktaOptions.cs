namespace Ilm.Infrastructure.Okta.Http;

public enum OktaMode
{
    Mock = 0,
    Live,
}

/// <summary>
/// Okta management API settings. There is deliberately no field for an SSWS token or client secret:
/// the service app authenticates with private_key_jwt using a non-exportable key from the machine store.
/// </summary>
public sealed class OktaOptions
{
    public const string SectionName = "Ilm:Okta";

    public OktaMode Mode { get; set; } = OktaMode.Mock;

    /// <summary>For example https://your-org.okta.com (fictional in development).</summary>
    public string OrgUrl { get; set; } = "https://okta.example.test";

    public string ServiceClientId { get; set; } = string.Empty;

    /// <summary>Thumbprint of the service-app signing certificate in LocalMachine\My (Windows production).</summary>
    public string? SigningCertificateThumbprint { get; set; }

    public string SigningKeyId { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } =
    [
        "okta.users.read", "okta.users.manage", "okta.groups.read", "okta.groups.manage", "okta.apps.read", "okta.logs.read",
    ];

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Application-assignment profile attribute that holds ILM role values.</summary>
    public string AppAssignmentRoleAttribute { get; set; } = "ilm_roles";
}
