namespace Ilm.Infrastructure.Directory.Ldap;

/// <summary>
/// The only attributes ILM ever requests. Password, LAPS, gMSA password and key-credential attributes are absent,
/// so they can never be returned, displayed, logged or audited.
/// </summary>
public static class SafeAttributeAllowlist
{
    public static readonly string[] Object =
    [
        "objectGUID", "objectSid", "distinguishedName", "objectClass", "cn", "ou", "name", "sAMAccountName",
        "userPrincipalName", "mail", "proxyAddresses", "displayName", "givenName", "sn", "department", "title",
        "employeeID", "manager", "description", "userAccountControl", "adminCount", "primaryGroupID", "dNSHostName",
        "operatingSystem", "operatingSystemVersion", "managedBy", "lastLogonTimestamp", "whenCreated", "whenChanged",
        "mS-DS-ConsistencyGuid",
    ];

    public static readonly string[] Forbidden =
    [
        "unicodePwd", "userPassword", "ms-Mcs-AdmPwd", "msLAPS-Password", "msLAPS-EncryptedPassword",
        "msDS-ManagedPassword", "msDS-KeyCredentialLink", "supplementalCredentials", "dBCSPwd", "ntPwdHistory", "lmPwdHistory",
    ];
}
