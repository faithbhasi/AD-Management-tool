using System.Text.Json;
using Ilm.Application.Abstractions;
using Ilm.Application.Approvals;
using Ilm.Application.Audit;
using Ilm.Application.Directory;
using Ilm.Application.Protection;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Configuration;

/// <summary>
/// Sensitive configuration lifecycle: propose → validate → Security Approver approval → activate, with
/// versioning, audit and a rollback target. Nothing here can remove the hardcoded protection floor.
/// </summary>
public sealed class ConfigurationService(
    IIlmDbContext db,
    ApprovalService approvals,
    IAuditWriter audit,
    IPlatformIdentityProvider platformIdentities,
    IActiveConfigurationProvider activeProvider,
    IDirectoryConnectorRegistry registry,
    TimeProvider time)
{
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(IlmConfigurationDocument document, CancellationToken cancellationToken)
    {
        var platform = await platformIdentities.GetAsync(cancellationToken);
        var issues = ConfigurationValidator.Validate(document, platform, time.GetUtcNow().UtcDateTime).ToList();
        await ValidateFeasibilityReferencesAsync(document, issues, cancellationToken);
        await ValidateScopesAgainstDirectoryAsync(document, platform, issues, cancellationToken);
        return issues;
    }

    public async Task<ConfigurationVersion> ProposeAsync(string documentJson, string summary, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRole(AppRole.ConfigurationAdministrator))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Only a Configuration Administrator can propose configuration.");
        }

        var document = ConfigurationSerializer.Deserialize(documentJson);
        var issues = await ValidateAsync(document, cancellationToken);
        var active = await activeProvider.GetAsync(cancellationToken);
        var nextVersion = (await db.ConfigurationVersions.MaxAsync(v => (long?)v.Version, cancellationToken) ?? 0) + 1;
        var hash = ConfigurationSerializer.ContentHash(document);
        var version = new ConfigurationVersion
        {
            Version = nextVersion,
            DocumentJson = ConfigurationSerializer.Serialize(document),
            ContentHash = hash,
            Summary = AuditSanitizer.SanitizeText(summary),
            ProposedByUserId = actor.AppUserId ?? Guid.Empty,
            ProposedByLabel = actor.Label,
            ProposedUtc = time.GetUtcNow().UtcDateTime,
            Status = issues.Count == 0 ? ConfigurationVersionStatus.AwaitingApproval : ConfigurationVersionStatus.ValidationFailed,
            ValidationIssuesJson = JsonSerializer.Serialize(issues, IlmJson.Compact),
        };
        db.ConfigurationVersions.Add(version);

        if (issues.Count == 0)
        {
            var approval = approvals.Create(
                ApprovalSubjectType.ConfigurationVersion,
                nextVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Configuration v{nextVersion}: {version.Summary}",
                AppRole.SecurityApprover,
                actor,
                hash,
                TimeSpan.FromHours(active.Document.LifecyclePolicies.Approval.ConfigurationApprovalValidityHours),
                active.Version);
            version.ApprovalId = approval.Id;
        }

        audit.Append(new AuditEvent
        {
            Action = "ConfigurationProposed",
            Result = version.Status.ToString(),
            TargetStableId = $"config:v{nextVersion}",
            ConfigurationVersion = active.Version,
            RequestedValues = new { version = nextVersion, hash, summary = version.Summary, issues },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<ConfigurationVersion> ApproveAsync(long versionNumber, bool approve, string? comment, ActorContext actor, CancellationToken cancellationToken)
    {
        var version = await db.ConfigurationVersions.FirstOrDefaultAsync(v => v.Version == versionNumber, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Configuration version not found.");
        if (version.Status != ConfigurationVersionStatus.AwaitingApproval || version.ApprovalId is null)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, $"Version {versionNumber} is {version.Status}.");
        }

        var approval = await approvals.DecideAsync(version.ApprovalId.Value, approve, comment, actor, version.ContentHash, cancellationToken);
        version.Status = approval.Status == ApprovalStatus.Approved ? ConfigurationVersionStatus.Approved : ConfigurationVersionStatus.Rejected;
        version.ApprovedByUserId = approval.Status == ApprovalStatus.Approved ? actor.AppUserId : null;
        version.ApprovedUtc = approval.DecidedUtc;
        await db.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<ConfigurationVersion> ActivateAsync(long versionNumber, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRole(AppRole.ConfigurationAdministrator) && !actor.HasRole(AppRole.SecurityApprover))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Activation requires Configuration Administrator or Security Approver.");
        }

        var version = await db.ConfigurationVersions.FirstOrDefaultAsync(v => v.Version == versionNumber, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Configuration version not found.");
        if (version.Status != ConfigurationVersionStatus.Approved)
        {
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "Only an approved version can be activated.");
        }

        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == version.ApprovalId, cancellationToken);
        var document = ConfigurationSerializer.Deserialize(version.DocumentJson);
        if (approval is not { Status: ApprovalStatus.Approved } || !string.Equals(approval.ContentHash, ConfigurationSerializer.ContentHash(document), StringComparison.Ordinal))
        {
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "The approval does not match this configuration content.");
        }

        var issues = await ValidateAsync(document, cancellationToken);
        if (issues.Count > 0)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "The configuration no longer validates: " + string.Join(" ", issues.Select(i => i.Message)));
        }

        var current = await db.ConfigurationVersions.Where(v => v.Status == ConfigurationVersionStatus.Active).ToListAsync(cancellationToken);
        foreach (var old in current)
        {
            old.Status = ConfigurationVersionStatus.Superseded;
        }

        version.Status = ConfigurationVersionStatus.Active;
        version.ActivatedUtc = time.GetUtcNow().UtcDateTime;
        version.SupersedesVersion = current.Select(v => (long?)v.Version).Max();
        MaterialiseAuthorityRules(document, version.Version);

        audit.Append(new AuditEvent
        {
            Action = "ConfigurationActivated",
            Result = "Active",
            TargetStableId = $"config:v{version.Version}",
            ConfigurationVersion = version.Version,
            Approval = $"{approval.Id}:{approval.Status}:{approval.DecidedByLabel}",
            BeforeValues = new { activeVersion = version.SupersedesVersion },
            AppliedValues = new { activeVersion = version.Version, version.ContentHash },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        activeProvider.Invalidate();
        return version;
    }

    /// <summary>Proposes a copy of an earlier version. It still needs Security Approver approval.</summary>
    public async Task<ConfigurationVersion> ProposeRollbackAsync(long targetVersion, ActorContext actor, CancellationToken cancellationToken)
    {
        var target = await db.ConfigurationVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Version == targetVersion, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Rollback target not found.");
        var proposed = await ProposeAsync(target.DocumentJson, $"Rollback to v{targetVersion}", actor, cancellationToken);
        proposed.RollbackOfVersion = targetVersion;
        await db.SaveChangesAsync(cancellationToken);
        return proposed;
    }

    /// <summary>Creates version 1 from a reviewed source-controlled document if no configuration exists.</summary>
    public async Task<bool> BootstrapAsync(IlmConfigurationDocument document, CancellationToken cancellationToken)
    {
        if (await db.ConfigurationVersions.AnyAsync(cancellationToken))
        {
            return false;
        }

        var platform = await platformIdentities.GetAsync(cancellationToken);
        var issues = ConfigurationValidator.Validate(document, platform, time.GetUtcNow().UtcDateTime);
        if (issues.Count > 0)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "Bootstrap configuration is invalid: " + string.Join(" ", issues.Select(i => i.Code)));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var version = new ConfigurationVersion
        {
            Version = 1,
            Status = ConfigurationVersionStatus.Active,
            DocumentJson = ConfigurationSerializer.Serialize(document),
            ContentHash = ConfigurationSerializer.ContentHash(document),
            Summary = "Bootstrap from source-controlled configuration (reviewed through change management).",
            ProposedByLabel = "system:bootstrap",
            ProposedUtc = now,
            ApprovedUtc = now,
            ActivatedUtc = now,
            IsBootstrap = true,
        };
        db.ConfigurationVersions.Add(version);
        MaterialiseAuthorityRules(document, 1);
        audit.Append(new AuditEvent
        {
            Action = "ConfigurationBootstrapped",
            Result = "Active",
            TargetStableId = "config:v1",
            ConfigurationVersion = 1,
            AppliedValues = new { version.ContentHash },
        }, ActorContext.System("bootstrap"));
        await db.SaveChangesAsync(cancellationToken);
        activeProvider.Invalidate();
        return true;
    }

    private void MaterialiseAuthorityRules(IlmConfigurationDocument document, long version)
    {
        foreach (var rule in document.AuthorityRules.Select(r => r.ToRule(version)))
        {
            db.AuthorityRules.Add(rule);
        }
    }

    private async Task ValidateFeasibilityReferencesAsync(IlmConfigurationDocument document, List<ValidationIssue> issues, CancellationToken cancellationToken)
    {
        foreach (var reference in document.FeasibilityApprovals)
        {
            var run = await db.FeasibilityRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reference.FeasibilityRunId, cancellationToken);
            if (run is null || run.Status != FeasibilityRunStatus.Approved || run.Mode != FeasibilityMode.Pcatest
                || !string.Equals(run.ReportSha256, reference.ReportSha256, StringComparison.OrdinalIgnoreCase)
                || run.Recommendation != reference.Recommendation)
            {
                issues.Add(new("FEASIBILITY_REFERENCE_INVALID", $"Feasibility reference {reference.FeasibilityRunId} is not an approved PCATEST report with a matching hash and recommendation."));
            }
        }
    }

    private async Task ValidateScopesAgainstDirectoryAsync(IlmConfigurationDocument document, Domain.Protection.PlatformIdentities platform, List<ValidationIssue> issues, CancellationToken cancellationToken)
    {
        foreach (var scope in document.Scopes)
        {
            if (!Guid.TryParse(scope.OuObjectGuid, out var guid) || !registry.ConnectorIds.Contains(scope.ConnectorId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var reader = registry.GetReader(scope.ConnectorId);
                var ou = await reader.GetByGuidAsync(guid, null, cancellationToken);
                if (ou is null)
                {
                    issues.Add(new("SCOPE_UNRESOLVED", $"Scope '{scope.Id}' objectGUID does not resolve to an object."));
                    continue;
                }

                if (ou.Kind != Domain.Directory.DirectoryObjectKind.OrganizationalUnit)
                {
                    issues.Add(new("UNRESTRICTED_SCOPE", $"Scope '{scope.Id}' is not an organizational unit (domain roots and containers are not permitted)."));
                    continue;
                }

                foreach (var sid in platform.RuntimeIdentitySids.Concat(platform.HostComputerSids).Concat(platform.DatabaseServiceIdentitySids).Concat(platform.BreakGlassSids))
                {
                    var platformObject = await reader.GetBySidAsync(sid, cancellationToken);
                    if (platformObject is not null && DistinguishedNameHelper.IsWithin(platformObject.DistinguishedName, ou.DistinguishedName))
                    {
                        issues.Add(new("RUNTIME_IDENTITY_MANAGEABLE", $"Scope '{scope.Id}' contains a platform identity ({sid}); the portal's own identities must never be manageable."));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                issues.Add(new("SCOPE_UNVERIFIABLE", $"Scope '{scope.Id}' could not be verified against the directory ({ex.GetType().Name})."));
            }
        }
    }
}
