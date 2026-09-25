using System.Security.Cryptography;
using System.Text;

namespace Ilm.Infrastructure.Development;

/// <summary>Deterministic fictional identifiers so that configuration can reference OUs by objectGUID.</summary>
public static class FictionalIds
{
    public const string TargetConnector = "corp";
    public const string LegacyAConnector = "legacy-a";
    public const string LegacyBConnector = "legacy-b";

    public const string TargetDomainSid = "S-1-5-21-1000000001-1000000002-1000000003";
    public const string LegacyADomainSid = "S-1-5-21-2000000001-2000000002-2000000003";
    public const string LegacyBDomainSid = "S-1-5-21-3000000001-3000000002-3000000003";

    public const string RuntimeGmsaSid = TargetDomainSid + "-1201";
    public const string PortalHostSid = TargetDomainSid + "-1202";
    public const string DatabaseServiceSid = TargetDomainSid + "-1203";
    public const string BreakGlassSid = TargetDomainSid + "-1204";
    public const string GmsaRetrieversGroupSid = TargetDomainSid + "-1102";
    public const string CertificateAuthoritySid = TargetDomainSid + "-1301";
    public const string PortalHostDns = "ilm-web01.corp.example.test";
    public const string OktaAgentDns = "okta-agt01.corp.example.test";

    // Okta group IDs (immutable) used for role mapping. Names in tokens are ignored.
    public const string GroupReaders = "00gReaders0000000001";
    public const string GroupLifecycleOperators = "00gLifecycleOps00001";
    public const string GroupLifecycleApprovers = "00gLifecycleApr00001";
    public const string GroupSecurityApprovers = "00gSecurityApr000001";
    public const string GroupConfigurationAdministrators = "00gConfigAdmins00001";
    public const string GroupAuditors = "00gAuditors000000001";
    public const string GroupDecoy = "00gDecoyGroup0000001";
    public const string GroupAdProvisioning = "00gAdPushTarget00001";

    // Operators (fictional Okta users) for the mock OIDC provider.
    public const string OperatorOlivia = "00uOperatorOlivia001";
    public const string ApproverAdrian = "00uApproverAdrian001";
    public const string SecuritySasha = "00uSecuritySasha0001";
    public const string ConfigCasey = "00uConfigCasey000001";
    public const string AuditorAudrey = "00uAuditorAudrey0001";
    public const string DbaDale = "00uDbaDale0000000001";
    public const string DecoyMallory = "00uDecoyMallory00001";

    // Persons' Okta identities.
    public const string OktaAlex = "00uPersonAlex0000001";
    public const string OktaBailey = "00uPersonBailey00001";
    public const string OktaFinley = "00uPersonFinley00001";
    public const string OktaHarper = "00uPersonHarper00001";

    public static Guid For(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("ilm-fictional:" + name)).AsSpan(0, 16));

    public static readonly Guid CorpManagedOu = For("corp:OU=ILM Managed");
    public static readonly Guid CorpStaffOu = For("corp:OU=Staff");
    public static readonly Guid CorpFinanceOu = For("corp:OU=Finance");
    public static readonly Guid CorpEngineeringOu = For("corp:OU=Engineering");
    public static readonly Guid CorpAdminOu = For("corp:OU=Admin Accounts");
    public static readonly Guid CorpWorkstationsOu = For("corp:OU=Workstations");
    public static readonly Guid CorpMemberServersOu = For("corp:OU=Member Servers");
    public static readonly Guid CorpGroupsOu = For("corp:OU=Groups");
    public static readonly Guid CorpTier0Ou = For("corp:OU=Tier0");
    public static readonly Guid CorpPlatformOu = For("corp:OU=Platform");
    public static readonly Guid CorpDomainControllersOu = For("corp:OU=Domain Controllers");
    public static readonly Guid LegacyAStaffOu = For("legacy-a:OU=Legacy Staff");
    public static readonly Guid LegacyAAdminOu = For("legacy-a:OU=Legacy Admins");
    public static readonly Guid LegacyBUsersOu = For("legacy-b:OU=Users Legacy B");

    public static readonly Guid LegalHoldGroup = For("corp:group:Legal Hold");
    public static readonly Guid MigrationWaveGroup = For("corp:group:Migration Wave 1");
    public static readonly Guid FinanceGroup = For("corp:group:Finance Staff");
}
