using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Leaver;
using Ilm.Domain.Protection;
using Ilm.Domain.Security;

namespace Ilm.Domain.Configuration;

public sealed record ValidationIssue(string Code, string Message);

/// <summary>
/// Rejects configuration that would weaken the security boundary. Runs on every proposal and again at activation.
/// </summary>
public static class ConfigurationValidator
{
    public const int MaxRoleCacheSeconds = 300;
    public const int MaxMigrationExceptionDays = 90;

    private static readonly System.Text.RegularExpressions.Regex OktaGroupId =
        new("^00g[A-Za-z0-9]{17}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<ValidationIssue> Validate(IlmConfigurationDocument doc, PlatformIdentities platform, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(platform);
        var issues = new List<ValidationIssue>();

        ValidateFeatureFlags(doc, issues);
        ValidateConnectors(doc, nowUtc, issues);
        ValidateScopes(doc, issues);
        ValidateAuthority(doc, issues);
        ValidateProtection(doc, platform, issues);
        ValidateRoles(doc, issues);
        ValidatePolicies(doc, issues);
        ValidateTemplates(doc, platform, issues);

        return issues;
    }

    private static void ValidateFeatureFlags(IlmConfigurationDocument doc, List<ValidationIssue> issues)
    {
        foreach (var (name, enabled) in doc.FeatureFlags)
        {
            if (!Enum.TryParse<Feature>(name, ignoreCase: false, out var feature) || !Enum.IsDefined(feature) || int.TryParse(name, out _))
            {
                issues.Add(new("FEATURE_UNKNOWN", $"Unknown feature flag '{name}'. Tier 0 management and other undefined capabilities cannot be enabled."));
                continue;
            }

            if (enabled && FeatureDefaults.RequireApprovedFeasibility.Contains(feature))
            {
                var approved = doc.FeasibilityApprovals.Any(a =>
                    a.Recommendation is FeasibilityRecommendation.Go or FeasibilityRecommendation.ConditionalGo
                    && !string.IsNullOrWhiteSpace(a.ReportSha256)
                    && !string.IsNullOrWhiteSpace(a.ApprovedBy));
                if (!approved)
                {
                    issues.Add(new("OKTA_PROVISIONING_WITHOUT_FEASIBILITY", $"{feature} cannot be enabled without an approved Okta-to-AD feasibility report (Go or Conditional Go)."));
                }
            }
        }

        var containment = doc.LifecyclePolicies.SafelyContained;
        if (containment.AllowEmergencyDirectoryOverride && !FeatureDefaults.IsEnabled(doc.FeatureFlags, Feature.EmergencyDirectoryOverride))
        {
            issues.Add(new("OVERRIDE_POLICY_WITHOUT_FLAG", "The emergency directory override policy requires the EmergencyDirectoryOverride feature flag."));
        }
    }

    private static void ValidateConnectors(IlmConfigurationDocument doc, DateTime nowUtc, List<ValidationIssue> issues)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.Connectors)
        {
            if (string.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id))
            {
                issues.Add(new("CONNECTOR_ID", $"Connector IDs must be present and unique ('{c.Id}')."));
            }

            if (!ConnectorModePolicy.IsModeValidForRole(c.Mode, c.Role))
            {
                issues.Add(new("LEGACY_GENERALLY_WRITABLE", $"Connector '{c.Id}': mode {c.Mode} is not permitted for a {c.Role} forest. Legacy forests can never be generally writable."));
            }

            if (c.Mode == ConnectorMode.MigrationException)
            {
                if (c.MigrationExceptionExpiresUtc is null || c.MigrationExceptionExpiresUtc <= nowUtc)
                {
                    issues.Add(new("MIGRATION_EXCEPTION_EXPIRY", $"Connector '{c.Id}': a migration exception needs a future expiry date."));
                }
                else if (c.MigrationExceptionExpiresUtc > nowUtc.AddDays(MaxMigrationExceptionDays))
                {
                    issues.Add(new("MIGRATION_EXCEPTION_TOO_LONG", $"Connector '{c.Id}': a migration exception may last at most {MaxMigrationExceptionDays} days."));
                }

                if (c.MigrationExceptionOperations.Contains(DirectoryOperation.DeleteObject))
                {
                    issues.Add(new("MIGRATION_EXCEPTION_DELETE", $"Connector '{c.Id}': deletion can never be part of a migration exception."));
                }
            }

            if (c.ContainmentMarkerApproved && string.IsNullOrWhiteSpace(c.ContainmentMarkerAttribute))
            {
                issues.Add(new("CONTAINMENT_MARKER_ATTRIBUTE", $"Connector '{c.Id}': an approved containment marker needs an explicit attribute."));
            }

            if (c.ContainmentMarkerAttribute is { } attr && IsForbiddenMarkerAttribute(attr))
            {
                issues.Add(new("CONTAINMENT_MARKER_FORBIDDEN", $"Connector '{c.Id}': '{attr}' cannot be used as a containment marker."));
            }

            if (c.Implementation is not ("Mock" or "Ldap"))
            {
                issues.Add(new("CONNECTOR_IMPLEMENTATION", $"Connector '{c.Id}': implementation must be Mock or Ldap."));
            }

            if (c.Implementation == "Ldap" && c.DomainControllers.Count == 0)
            {
                issues.Add(new("CONNECTOR_DCS", $"Connector '{c.Id}': an LDAP connector needs at least one domain controller."));
            }
        }
    }

    private static bool IsForbiddenMarkerAttribute(string attribute)
    {
        string[] forbidden =
        [
            "userAccountControl", "unicodePwd", "userPassword", "memberOf", "member", "mail", "proxyAddresses",
            "userPrincipalName", "sAMAccountName", "objectSid", "sIDHistory", "mS-DS-ConsistencyGuid", "adminCount",
            "servicePrincipalName", "msDS-AllowedToDelegateTo", "msDS-AllowedToActOnBehalfOfOtherIdentity",
        ];
        return forbidden.Contains(attribute, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateScopes(IlmConfigurationDocument doc, List<ValidationIssue> issues)
    {
        var connectorIds = doc.Connectors.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.Scopes)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || !scopeIds.Add(s.Id))
            {
                issues.Add(new("SCOPE_ID", $"Scope IDs must be present and unique ('{s.Id}')."));
            }

            if (!connectorIds.Contains(s.ConnectorId))
            {
                issues.Add(new("SCOPE_CONNECTOR", $"Scope '{s.Id}' references unknown connector '{s.ConnectorId}'."));
            }

            if (!Guid.TryParseExact(s.OuObjectGuid, "D", out var guid) || guid == Guid.Empty)
            {
                issues.Add(new("SCOPE_NOT_GUID", $"Scope '{s.Id}' must identify an OU by objectGUID; DNs and wildcards are not accepted."));
            }
        }
    }

    private static void ValidateAuthority(IlmConfigurationDocument doc, List<ValidationIssue> issues)
    {
        var rules = doc.AuthorityRules.Select(r => r.ToRule(0)).ToList();
        foreach (var conflict in WriterConflictDetector.FindConflicts(rules))
        {
            issues.Add(new("OVERLAPPING_WRITERS", conflict));
        }

        foreach (var r in doc.AuthorityRules)
        {
            if (string.IsNullOrWhiteSpace(r.Population))
            {
                issues.Add(new("AUTHORITY_POPULATION", "Every authority rule needs a population."));
            }

            if (r.AttributeSet == AttributeSet.AccountEnabledState && r.ContainmentOwner is null)
            {
                issues.Add(new("CONTAINMENT_OWNER_MISSING", $"AccountEnabledState rule for '{r.Population}' ({r.LifecycleAction}) must name an explicit ContainmentOwner."));
            }

            if (r.EffectiveUntil is not null && r.EffectiveUntil <= r.EffectiveFrom)
            {
                issues.Add(new("AUTHORITY_WINDOW", $"Authority rule for '{r.Population}' has an empty effective window."));
            }

            if (r.AuthoritativeSystem is SystemKind.None)
            {
                issues.Add(new("AUTHORITY_SYSTEM", $"Authority rule for '{r.Population}' needs an authoritative system."));
            }
        }
    }

    private static void ValidateProtection(IlmConfigurationDocument doc, PlatformIdentities platform, List<ValidationIssue> issues)
    {
        foreach (var p in doc.ProtectionAdditions)
        {
            if (p.Source is not (ProtectionSource.ImportedAttackPath or ProtectionSource.ConfiguredAddition))
            {
                issues.Add(new("PROTECTION_SOURCE", $"Protection entry '{p.Value}' has source {p.Source}; only imported or configured additions are allowed. The floor is not configuration."));
            }

            if (string.IsNullOrWhiteSpace(p.Value))
            {
                issues.Add(new("PROTECTION_VALUE", "Protection entries need a value."));
            }
        }

        foreach (var guid in doc.LifecyclePolicies.GroupRemoval.PreserveGroupGuids)
        {
            if (!Guid.TryParse(guid, out _))
            {
                issues.Add(new("GROUP_POLICY_GUID", $"Preserved group '{guid}' must be an objectGUID."));
            }
        }

        // Protection additions can only add. Listing a platform or floor identity is harmless but redundant;
        // anything that looks like an attempt to un-protect is rejected by the schema having no such field.
        foreach (var p in doc.ProtectionAdditions.Where(p => p.MatchOn == ProtectedObjectMatch.Sid && !p.Value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new("PROTECTION_SID_FORMAT", $"Protection entry '{p.Value}' is not a SID."));
        }

        _ = platform;
    }

    private static void ValidateTemplates(IlmConfigurationDocument doc, PlatformIdentities platform, List<ValidationIssue> issues)
    {
        var protectedAdditionSids = doc.ProtectionAdditions
            .Where(p => p.MatchOn == ProtectedObjectMatch.Sid)
            .Select(p => p.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var platformSids = platform.RuntimeIdentitySids.Concat(platform.ManagedPasswordRetrieverSids).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopeIds = doc.Scopes.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var t in doc.AccountTemplates)
        {
            if (!scopeIds.Contains(t.TargetScopeId))
            {
                issues.Add(new("TEMPLATE_SCOPE", $"Template '{t.Id}' must target a configured OU scope."));
            }

            foreach (var sid in t.MandatoryGroupSids)
            {
                if (ProtectionFloor.IsFloorSid(sid) || protectedAdditionSids.Contains(sid) || platformSids.Contains(sid))
                {
                    issues.Add(new("PROTECTED_GROUP_ASSIGNMENT", $"Template '{t.Id}' assigns protected group {sid}. Protected group assignment is never allowed."));
                }
            }
        }
    }

    private static void ValidateRoles(IlmConfigurationDocument doc, List<ValidationIssue> issues)
    {
        if (doc.RoleResolution.CacheSeconds is < 0 or > MaxRoleCacheSeconds)
        {
            issues.Add(new("ROLE_CACHE", $"Privileged role cache must be between 0 and {MaxRoleCacheSeconds} seconds."));
        }

        var scopeIds = doc.Scopes.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in doc.RoleMappings)
        {
            switch (doc.RoleResolution.Mode)
            {
                case RoleResolutionMode.OktaGroupMembership when m.OktaGroupId is null || !OktaGroupId.IsMatch(m.OktaGroupId):
                    issues.Add(new("ROLE_MAPPING_NOT_IMMUTABLE", $"Role mapping for {m.Role} must use an immutable Okta group ID (00g…), not a group name ('{m.OktaGroupId}')."));
                    break;
                case RoleResolutionMode.OktaAppAssignment when string.IsNullOrWhiteSpace(m.AppAssignmentRole):
                    issues.Add(new("ROLE_MAPPING_APP_VALUE", $"Role mapping for {m.Role} needs an application-assignment role value."));
                    break;
            }

            if (m.Role is AppRole.Reader or AppRole.LifecycleOperator or AppRole.LifecycleApprover)
            {
                if (m.ScopeIds.Count == 0)
                {
                    issues.Add(new("UNRESTRICTED_SCOPE", $"Role mapping for {m.Role} must be limited to explicit OU scopes; unrestricted domain scope is not allowed."));
                }
            }

            foreach (var scope in m.ScopeIds.Where(s => !scopeIds.Contains(s)))
            {
                issues.Add(new("ROLE_SCOPE_UNKNOWN", $"Role mapping for {m.Role} references unknown scope '{scope}'."));
            }
        }

        if (doc.RoleResolution.Mode == RoleResolutionMode.OktaAppAssignment && string.IsNullOrWhiteSpace(doc.RoleResolution.OktaAppId))
        {
            issues.Add(new("ROLE_APP_ID", "App-assignment role resolution needs the ILM Okta application ID."));
        }
    }

    private static void ValidatePolicies(IlmConfigurationDocument doc, List<ValidationIssue> issues)
    {
        var sc = doc.LifecyclePolicies.SafelyContained;
        if (!sc.RequireAuthenticationContainmentVerified || !sc.RequireDirectoryDisableVerified)
        {
            issues.Add(new("SAFELY_CONTAINED_WEAKENED", "SafelyContained must always require verified authentication containment and verified directory disable."));
        }

        if (!sc.RequireAllIdentityLinksResolved)
        {
            issues.Add(new("SAFELY_CONTAINED_LINKS", "SafelyContained must require all provisional and ambiguous links to be resolved."));
        }

        if (sc.SessionRequirements.TryGetValue(SessionSystem.Okta, out var okta) && okta != SessionRequirement.Mandatory)
        {
            issues.Add(new("OKTA_SESSIONS_OPTIONAL", "Okta session revocation must remain mandatory."));
        }

        if (sc.DirectoryVerificationTimeoutSeconds is < 10 or > 86_400)
        {
            issues.Add(new("VERIFICATION_TIMEOUT", "Directory verification timeout must be between 10 seconds and 24 hours."));
        }

        if (sc.ManualContainmentSlaMinutes is < 1 or > 1_440 || sc.UrgentContainmentSlaMinutes is < 1 or > 1_440)
        {
            issues.Add(new("CONTAINMENT_SLA", "Containment SLAs must be between 1 minute and 24 hours."));
        }

        var retention = doc.LifecyclePolicies.Retention;
        if (retention.AutomaticDeletion)
        {
            issues.Add(new("AUTOMATIC_DELETION", "Automatic account deletion is not permitted."));
        }

        if (doc.LifecyclePolicies.Approval.LeaverApprovalValidityHours is < 1 or > 168)
        {
            issues.Add(new("APPROVAL_VALIDITY", "Leaver approval validity must be between 1 and 168 hours."));
        }
    }
}
