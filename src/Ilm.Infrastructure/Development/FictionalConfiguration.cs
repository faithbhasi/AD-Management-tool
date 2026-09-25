using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;
using Ilm.Domain.Security;
using static Ilm.Infrastructure.Development.FictionalIds;

namespace Ilm.Infrastructure.Development;

/// <summary>The development bootstrap configuration (version 1). Fictional values only.</summary>
public static class FictionalConfiguration
{
    public const string PopulationTargetOkta = "target-okta";
    public const string PopulationOktaWorkforce = "okta-workforce";
    public const string PopulationTargetDirect = "target-direct";
    public const string PopulationTargetAdmin = "target-admin";
    public const string PopulationLegacyA = "legacy-a";
    public const string PopulationLegacyB = "legacy-b";
    public const string PopulationUnmapped = "unmapped";

    private static readonly DateTime Effective = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IlmConfigurationDocument Bootstrap()
    {
        var doc = new IlmConfigurationDocument
        {
            Connectors =
            [
                new() { Id = TargetConnector, DisplayName = "Target forest (corp.example.test)", ForestDnsName = "corp.example.test", DomainDnsName = "corp.example.test", DomainSid = TargetDomainSid, Role = ForestRole.Target, Mode = ConnectorMode.ReadOnlyTarget, Implementation = "Mock", DomainControllers = ["dc01.corp.example.test", "dc02.corp.example.test"], DefaultPopulation = PopulationTargetDirect },
                new() { Id = LegacyAConnector, DisplayName = "Legacy forest A (legacy-a.example.test)", ForestDnsName = "legacy-a.example.test", DomainDnsName = "legacy-a.example.test", DomainSid = LegacyADomainSid, Role = ForestRole.Legacy, Mode = ConnectorMode.ContainmentOnlyLegacy, Implementation = "Mock", DomainControllers = ["lgadc01.legacy-a.example.test"], DefaultPopulation = PopulationLegacyA },
                new() { Id = LegacyBConnector, DisplayName = "Legacy forest B (legacy-b.example.test)", ForestDnsName = "legacy-b.example.test", DomainDnsName = "legacy-b.example.test", DomainSid = LegacyBDomainSid, Role = ForestRole.Legacy, Mode = ConnectorMode.ReadOnlyLegacy, Implementation = "Mock", DomainControllers = ["lgbdc01.legacy-b.example.test"], DefaultPopulation = PopulationLegacyB },
            ],
            Scopes =
            [
                new() { Id = "corp-managed", ConnectorId = TargetConnector, OuObjectGuid = CorpManagedOu.ToString("D"), DisplayName = "corp: ILM Managed" },
                new() { Id = "corp-staff", ConnectorId = TargetConnector, OuObjectGuid = CorpStaffOu.ToString("D"), DisplayName = "corp: Staff" },
                new() { Id = "corp-workstations", ConnectorId = TargetConnector, OuObjectGuid = CorpWorkstationsOu.ToString("D"), DisplayName = "corp: Workstations" },
                new() { Id = "lega-staff", ConnectorId = LegacyAConnector, OuObjectGuid = LegacyAStaffOu.ToString("D"), DisplayName = "legacy-a: Legacy Staff" },
                new() { Id = "lega-admins", ConnectorId = LegacyAConnector, OuObjectGuid = LegacyAAdminOu.ToString("D"), DisplayName = "legacy-a: Legacy Admins" },
                new() { Id = "legb-users", ConnectorId = LegacyBConnector, OuObjectGuid = LegacyBUsersOu.ToString("D"), DisplayName = "legacy-b: Users" },
            ],
            AuthorityRules = [.. LeaverRules(), .. JoinerRules()],
            ProtectionAdditions =
            [
                new() { MatchOn = ProtectedObjectMatch.Sid, Value = CertificateAuthoritySid, Category = ProtectionCategory.CertificateAuthorityServer, Source = ProtectionSource.ImportedAttackPath, SourceReference = "attack-path-analysis-2026-09-01" },
                new() { MatchOn = ProtectedObjectMatch.DnsHostName, Value = OktaAgentDns, Category = ProtectionCategory.OktaAgentServer, Source = ProtectionSource.ConfiguredAddition, SourceReference = "okta-agent-inventory" },
            ],
            RoleMappings =
            [
                new() { OktaGroupId = GroupReaders, Role = AppRole.Reader, ScopeIds = ["corp-managed", "lega-staff", "lega-admins", "legb-users"] },
                new() { OktaGroupId = GroupLifecycleOperators, Role = AppRole.LifecycleOperator, ScopeIds = ["corp-managed", "lega-staff", "lega-admins", "legb-users"] },
                new() { OktaGroupId = GroupLifecycleApprovers, Role = AppRole.LifecycleApprover, ScopeIds = ["corp-managed", "lega-staff", "lega-admins", "legb-users"] },
                new() { OktaGroupId = GroupSecurityApprovers, Role = AppRole.SecurityApprover, ScopeIds = ["corp-managed", "lega-staff", "lega-admins", "legb-users"] },
                new() { OktaGroupId = GroupConfigurationAdministrators, Role = AppRole.ConfigurationAdministrator },
                new() { OktaGroupId = GroupAuditors, Role = AppRole.Auditor },
            ],
            RoleResolution = new RoleResolutionSettings { Mode = RoleResolutionMode.OktaGroupMembership, CacheSeconds = 120 },
            LifecyclePolicies = new LifecyclePolicies
            {
                GroupRemoval = new GroupRemovalPolicy { PreserveGroupGuids = [LegalHoldGroup.ToString("D"), MigrationWaveGroup.ToString("D")] },
            },
            FeatureFlags = Enum.GetValues<Feature>().ToDictionary(f => f.ToString(), f => FeatureDefaults.EnabledByDefault.Contains(f), StringComparer.Ordinal),
        };
        return doc;
    }

    private static IEnumerable<AuthorityRuleDefinition> LeaverRules()
    {
        AuthorityRuleDefinition Rule(string population, AttributeSet set, SystemKind owner, SystemKind? containment, StrategyKind strategy, AccountType? accountType = null) => new()
        {
            Population = population,
            AccountType = accountType,
            LifecycleAction = LifecycleAction.Leaver,
            AttributeSet = set,
            AuthoritativeSystem = owner,
            ContainmentOwner = containment,
            ProvisioningStrategy = strategy,
            EffectiveFrom = Effective,
        };

        yield return Rule(PopulationOktaWorkforce, AttributeSet.AccountEnabledState, SystemKind.Okta, SystemKind.Okta, StrategyKind.OktaApiProvisioning);
        yield return Rule(PopulationOktaWorkforce, AttributeSet.AuthenticationSessions, SystemKind.Okta, SystemKind.Okta, StrategyKind.OktaApiProvisioning);
        yield return Rule(PopulationTargetOkta, AttributeSet.AccountEnabledState, SystemKind.Okta, SystemKind.Okta, StrategyKind.OktaApiProvisioning);
        yield return Rule(PopulationTargetOkta, AttributeSet.GroupMembership, SystemKind.Okta, null, StrategyKind.OktaApiProvisioning);
        yield return Rule(PopulationTargetDirect, AttributeSet.AccountEnabledState, SystemKind.ActiveDirectory, SystemKind.ActiveDirectory, StrategyKind.DirectActiveDirectory);
        yield return Rule(PopulationTargetDirect, AttributeSet.GroupMembership, SystemKind.ActiveDirectory, null, StrategyKind.DirectActiveDirectory);
        yield return Rule(PopulationTargetAdmin, AttributeSet.AccountEnabledState, SystemKind.ActiveDirectory, SystemKind.ActiveDirectory, StrategyKind.DirectActiveDirectory, AccountType.Administrative);
        yield return Rule(PopulationLegacyA, AttributeSet.AccountEnabledState, SystemKind.ActiveDirectory, SystemKind.ActiveDirectory, StrategyKind.ContainmentOnlyLegacy);
        yield return Rule(PopulationLegacyB, AttributeSet.AccountEnabledState, SystemKind.ActiveDirectory, SystemKind.ActiveDirectory, StrategyKind.ReadOnly);
    }

    private static IEnumerable<AuthorityRuleDefinition> JoinerRules()
    {
        (AttributeSet Set, SystemKind Owner, StrategyKind Strategy)[] okta =
        [
            (AttributeSet.CoreProfile, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.EmploymentAttributes, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.NamingAttributes, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.MailAttributes, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.GroupMembership, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.ApplicationAssignments, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.Password, SystemKind.ActiveDirectory, StrategyKind.ManualControlled),
            (AttributeSet.LicenceAssignments, SystemKind.Entra, StrategyKind.ManualControlled),
            (AttributeSet.ManagerAndOwnership, SystemKind.Okta, StrategyKind.OktaApiProvisioning),
            (AttributeSet.DeviceAccess, SystemKind.Manual, StrategyKind.ManualControlled),
        ];
        foreach (var (set, owner, strategy) in okta)
        {
            yield return new AuthorityRuleDefinition { Population = PopulationTargetOkta, LifecycleAction = LifecycleAction.Joiner, AttributeSet = set, AuthoritativeSystem = owner, ProvisioningStrategy = strategy, EffectiveFrom = Effective };
        }

        yield return new AuthorityRuleDefinition { Population = PopulationTargetOkta, LifecycleAction = LifecycleAction.Joiner, AttributeSet = AttributeSet.AccountEnabledState, AuthoritativeSystem = SystemKind.Okta, ContainmentOwner = SystemKind.Okta, ProvisioningStrategy = StrategyKind.OktaApiProvisioning, EffectiveFrom = Effective };

        foreach (var set in new[] { AttributeSet.CoreProfile, AttributeSet.NamingAttributes, AttributeSet.MailAttributes, AttributeSet.GroupMembership, AttributeSet.Password })
        {
            yield return new AuthorityRuleDefinition { Population = PopulationTargetDirect, LifecycleAction = LifecycleAction.Joiner, AttributeSet = set, AuthoritativeSystem = SystemKind.ActiveDirectory, ProvisioningStrategy = StrategyKind.DirectActiveDirectory, EffectiveFrom = Effective };
        }
    }
}
