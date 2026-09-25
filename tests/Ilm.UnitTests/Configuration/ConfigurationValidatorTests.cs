using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Leaver;
using Ilm.Domain.Protection;
using Ilm.Domain.Security;
using Ilm.Infrastructure.Development;

namespace Ilm.UnitTests.Configuration;

public sealed class ConfigurationValidatorTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
    private static readonly PlatformIdentities Platform = new() { Complete = true, RuntimeIdentitySids = ["S-1-5-21-1-2-3-1201"] };

    private static IReadOnlyList<ValidationIssue> Validate(Action<IlmConfigurationDocument> change)
    {
        var doc = FictionalConfiguration.Bootstrap();
        change(doc);
        return ConfigurationValidator.Validate(doc, Platform, Now);
    }

    [Fact]
    public void Development_bootstrap_is_valid() => Assert.Empty(Validate(_ => { }));

    [Fact]
    public void Legacy_connector_cannot_be_made_generally_writable() =>
        Assert.Contains(Validate(d => d.Connectors.First(c => c.Role == ForestRole.Legacy).Mode = ConnectorMode.WriteTarget), i => i.Code == "LEGACY_GENERALLY_WRITABLE");

    [Fact]
    public void Unrestricted_domain_scope_is_rejected() =>
        Assert.Contains(Validate(d => d.RoleMappings.First(m => m.Role == AppRole.LifecycleOperator).ScopeIds.Clear()), i => i.Code == "UNRESTRICTED_SCOPE");

    [Fact]
    public void Arbitrary_dn_scope_is_rejected() =>
        Assert.Contains(Validate(d => d.Scopes[0].OuObjectGuid = "OU=Anything,DC=corp,DC=example,DC=test"), i => i.Code == "SCOPE_NOT_GUID");

    [Fact]
    public void Protected_group_assignment_is_rejected() =>
        Assert.Contains(Validate(d => d.AccountTemplates.Add(new AccountTemplateDefinition { Id = "admin", AccountType = AccountType.Administrative, TargetScopeId = "corp-managed", MandatoryGroupSids = [FictionalIds.TargetDomainSid + "-512"] })), i => i.Code == "PROTECTED_GROUP_ASSIGNMENT");

    [Fact]
    public void Runtime_identity_group_assignment_is_rejected() =>
        Assert.Contains(Validate(d => d.AccountTemplates.Add(new AccountTemplateDefinition { Id = "t", TargetScopeId = "corp-managed", MandatoryGroupSids = ["S-1-5-21-1-2-3-1201"] })), i => i.Code == "PROTECTED_GROUP_ASSIGNMENT");

    [Fact]
    public void Okta_provisioning_without_feasibility_is_rejected()
    {
        Assert.Contains(Validate(d => d.FeatureFlags[nameof(Feature.OktaUserProvisioning)] = true), i => i.Code == "OKTA_PROVISIONING_WITHOUT_FEASIBILITY");
        Assert.Contains(Validate(d =>
        {
            d.FeatureFlags[nameof(Feature.OktaUserProvisioning)] = true;
            d.FeasibilityApprovals.Add(new FeasibilityApprovalReference { FeasibilityRunId = Guid.NewGuid(), ReportSha256 = "abc", Recommendation = FeasibilityRecommendation.NoGo, ApprovedBy = "x" });
        }), i => i.Code == "OKTA_PROVISIONING_WITHOUT_FEASIBILITY");
    }

    [Fact]
    public void Okta_provisioning_with_approved_go_report_passes_static_validation() =>
        Assert.DoesNotContain(Validate(d =>
        {
            d.FeatureFlags[nameof(Feature.OktaUserProvisioning)] = true;
            d.FeasibilityApprovals.Add(new FeasibilityApprovalReference { FeasibilityRunId = Guid.NewGuid(), ReportSha256 = "abc", Recommendation = FeasibilityRecommendation.ConditionalGo, ApprovedBy = "sasha" });
        }), i => i.Code == "OKTA_PROVISIONING_WITHOUT_FEASIBILITY");

    [Fact]
    public void SafelyContained_cannot_be_weakened()
    {
        Assert.Contains(Validate(d => d.LifecyclePolicies.SafelyContained.RequireDirectoryDisableVerified = false), i => i.Code == "SAFELY_CONTAINED_WEAKENED");
        Assert.Contains(Validate(d => d.LifecyclePolicies.SafelyContained.SessionRequirements[SessionSystem.Okta] = SessionRequirement.BestEffort), i => i.Code == "OKTA_SESSIONS_OPTIONAL");
    }

    [Fact]
    public void Automatic_deletion_is_rejected() =>
        Assert.Contains(Validate(d => d.LifecyclePolicies.Retention.AutomaticDeletion = true), i => i.Code == "AUTOMATIC_DELETION");

    [Fact]
    public void Role_mapping_by_group_name_is_rejected() =>
        Assert.Contains(Validate(d => d.RoleMappings[0].OktaGroupId = "ILM-SecurityApprovers"), i => i.Code == "ROLE_MAPPING_NOT_IMMUTABLE");

    [Fact]
    public void Role_cache_is_capped() =>
        Assert.Contains(Validate(d => d.RoleResolution.CacheSeconds = 3600), i => i.Code == "ROLE_CACHE");

    [Fact]
    public void Migration_exception_needs_future_bounded_expiry()
    {
        Assert.Contains(Validate(d => d.Connectors[1].Mode = ConnectorMode.MigrationException), i => i.Code == "MIGRATION_EXCEPTION_EXPIRY");
        Assert.Contains(Validate(d => { d.Connectors[1].Mode = ConnectorMode.MigrationException; d.Connectors[1].MigrationExceptionExpiresUtc = Now.AddDays(400); }), i => i.Code == "MIGRATION_EXCEPTION_TOO_LONG");
    }

    [Fact]
    public void Containment_marker_cannot_target_security_attributes() =>
        Assert.Contains(Validate(d => { d.Connectors[1].ContainmentMarkerApproved = true; d.Connectors[1].ContainmentMarkerAttribute = "userAccountControl"; }), i => i.Code == "CONTAINMENT_MARKER_FORBIDDEN");

    [Fact]
    public void Unknown_feature_flags_are_rejected() =>
        Assert.Contains(Validate(d => d.FeatureFlags["ManageTier0Groups"] = true), i => i.Code == "FEATURE_UNKNOWN");

    [Fact]
    public void Initially_enabled_and_disabled_capabilities_match_the_specification()
    {
        var doc = FictionalConfiguration.Bootstrap();
        Feature[] disabled =
        [
            Feature.StandardUserCreation, Feature.AdministrativeUserCreation, Feature.PasswordReset, Feature.UserMove,
            Feature.ComputerDisableAndMove, Feature.OktaUserProvisioning, Feature.OktaToAdProvisioning, Feature.EntraLifecycle,
            Feature.ExchangeLifecycle, Feature.CitrixLifecycle, Feature.MimecastLifecycle, Feature.DirectActiveDirectoryContainment,
        ];
        foreach (var f in disabled)
        {
            Assert.False(FeatureDefaults.IsEnabled(doc.FeatureFlags, f), f.ToString());
        }

        Assert.True(FeatureDefaults.IsEnabled(doc.FeatureFlags, Feature.LegacyContainment));
        Assert.True(FeatureDefaults.IsEnabled(doc.FeatureFlags, Feature.OktaContainment));
    }
}
