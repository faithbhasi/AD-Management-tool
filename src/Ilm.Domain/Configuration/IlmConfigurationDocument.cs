using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Leaver;
using Ilm.Domain.Protection;
using Ilm.Domain.Security;

namespace Ilm.Domain.Configuration;

/// <summary>
/// The complete sensitive configuration, versioned as one document. It has no field that can remove
/// the hardcoded protection floor and no field for Tier 0 management.
/// </summary>
public sealed class IlmConfigurationDocument
{
    public List<DirectoryConnectorDefinition> Connectors { get; set; } = [];

    public List<ScopeDefinition> Scopes { get; set; } = [];

    public List<AuthorityRuleDefinition> AuthorityRules { get; set; } = [];

    public List<ProtectedObjectDefinition> ProtectionAdditions { get; set; } = [];

    public List<RoleMappingDefinition> RoleMappings { get; set; } = [];

    public RoleResolutionSettings RoleResolution { get; set; } = new();

    public LifecyclePolicies LifecyclePolicies { get; set; } = new();

    public Dictionary<string, bool> FeatureFlags { get; set; } = new(StringComparer.Ordinal);

    public List<FeasibilityApprovalReference> FeasibilityApprovals { get; set; } = [];

    public List<AccountTemplateDefinition> AccountTemplates { get; set; } = [];
}

public sealed class DirectoryConnectorDefinition
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ForestDnsName { get; set; } = string.Empty;

    public string DomainDnsName { get; set; } = string.Empty;

    public string? DomainSid { get; set; }

    public ForestRole Role { get; set; }

    public ConnectorMode Mode { get; set; }

    /// <summary>"Mock" or "Ldap".</summary>
    public string Implementation { get; set; } = "Mock";

    public List<string> DomainControllers { get; set; } = [];

    public bool UseLdaps { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public DateTime? MigrationExceptionExpiresUtc { get; set; }

    public List<DirectoryOperation> MigrationExceptionOperations { get; set; } = [];

    public bool ContainmentMarkerApproved { get; set; }

    public string? ContainmentMarkerAttribute { get; set; }

    /// <summary>Population key assigned to user accounts read through this connector.</summary>
    public string DefaultPopulation { get; set; } = string.Empty;
}

/// <summary>An OU scope, identified only by objectGUID. The DN is resolved at request time.</summary>
public sealed class ScopeDefinition
{
    public string Id { get; set; } = string.Empty;

    public string ConnectorId { get; set; } = string.Empty;

    public string OuObjectGuid { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AuthorityRuleDefinition
{
    public string Population { get; set; } = string.Empty;

    public string? BusinessEntity { get; set; }

    public string? MigrationWave { get; set; }

    public AccountType? AccountType { get; set; }

    public LifecycleAction LifecycleAction { get; set; }

    public AttributeSet AttributeSet { get; set; }

    public SystemKind AuthoritativeSystem { get; set; }

    public SystemKind? ContainmentOwner { get; set; }

    public StrategyKind ProvisioningStrategy { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveUntil { get; set; }

    public AuthorityRule ToRule(long version) => new()
    {
        Id = DeterministicId(version),
        Population = Population,
        BusinessEntity = BusinessEntity,
        MigrationWave = MigrationWave,
        AccountType = AccountType,
        LifecycleAction = LifecycleAction,
        AttributeSet = AttributeSet,
        AuthoritativeSystem = AuthoritativeSystem,
        ContainmentOwner = ContainmentOwner,
        ProvisioningStrategy = ProvisioningStrategy,
        EffectiveFrom = EffectiveFrom,
        EffectiveUntil = EffectiveUntil,
        ApprovedConfigurationVersion = version,
    };

    private Guid DeterministicId(long version)
    {
        var key = $"{version}|{Population}|{BusinessEntity}|{MigrationWave}|{AccountType}|{LifecycleAction}|{AttributeSet}|{EffectiveFrom:O}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class ProtectedObjectDefinition
{
    public ProtectedObjectMatch MatchOn { get; set; }

    public string Value { get; set; } = string.Empty;

    public ProtectionCategory Category { get; set; }

    /// <summary>ImportedAttackPath or ConfiguredAddition. Floor and platform sources are rejected here.</summary>
    public ProtectionSource Source { get; set; } = ProtectionSource.ConfiguredAddition;

    public string? SourceReference { get; set; }

    public ProtectedObjectEntry ToEntry() => new(MatchOn, Value, Category, Source, SourceReference);
}

public sealed class RoleMappingDefinition
{
    /// <summary>Immutable Okta group ID (00g...). Group names are rejected.</summary>
    public string? OktaGroupId { get; set; }

    /// <summary>Okta application-assignment role value, used when resolution mode is OktaAppAssignment.</summary>
    public string? AppAssignmentRole { get; set; }

    public AppRole Role { get; set; }

    /// <summary>Scope IDs; required for lifecycle roles.</summary>
    public List<string> ScopeIds { get; set; } = [];
}

public enum RoleResolutionMode
{
    OktaGroupMembership = 0,
    OktaAppAssignment,
}

public sealed class RoleResolutionSettings
{
    public RoleResolutionMode Mode { get; set; } = RoleResolutionMode.OktaGroupMembership;

    /// <summary>Privileged role cache lifetime. Capped at 300 seconds by validation.</summary>
    public int CacheSeconds { get; set; } = 120;

    public string? OktaAppId { get; set; }
}

public sealed class LifecyclePolicies
{
    public SafelyContainedPolicy SafelyContained { get; set; } = new();

    public GroupRemovalPolicy GroupRemoval { get; set; } = new();

    public RetentionPolicy Retention { get; set; } = new();

    public ApprovalPolicy Approval { get; set; } = new();
}

public sealed class GroupRemovalPolicy
{
    public bool RemoveNonPreservedGroups { get; set; } = true;

    /// <summary>Legal-hold, investigation and migration groups, by objectGUID.</summary>
    public List<string> PreserveGroupGuids { get; set; } = [];
}

public sealed class RetentionPolicy
{
    public int RetainDisabledAccountDays { get; set; } = 90;

    public bool AutomaticDeletion { get; set; }

    public bool RemoveLicencesAutomatically { get; set; }
}

public sealed class ApprovalPolicy
{
    public int LeaverApprovalValidityHours { get; set; } = 24;

    public int ConfigurationApprovalValidityHours { get; set; } = 72;
}

public sealed class FeasibilityApprovalReference
{
    public Guid FeasibilityRunId { get; set; }

    public string ReportSha256 { get; set; } = string.Empty;

    public FeasibilityRecommendation Recommendation { get; set; }

    public string ApprovedBy { get; set; } = string.Empty;

    public DateTime ApprovedUtc { get; set; }
}

/// <summary>Privileged and standard account templates (joiner module; disabled initially).</summary>
public sealed class AccountTemplateDefinition
{
    public string Id { get; set; } = string.Empty;

    public AccountType AccountType { get; set; }

    public string TargetScopeId { get; set; } = string.Empty;

    public List<string> MandatoryGroupSids { get; set; } = [];
}
