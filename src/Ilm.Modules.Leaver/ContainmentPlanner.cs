using Ilm.Application.Abstractions;
using Ilm.Application.Authority;
using Ilm.Application.Configuration;
using Ilm.Application.Protection;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Protection;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Modules.Leaver;

/// <summary>
/// Builds the containment plan for a person. Every linked account gets an explicit method; anything that cannot be
/// automated safely (unresolved authority, protection not Clear, disabled strategy, unsupported owner) becomes a
/// manual controlled action rather than being dropped.
/// </summary>
public sealed class ContainmentPlanner(
    IIlmDbContext db,
    IActiveConfigurationProvider configuration,
    AuthorityService authority,
    ProtectionService protection,
    TimeProvider time)
{
    public async Task<LeaverPlan> BuildAsync(Guid personId, CancellationToken cancellationToken)
    {
        var config = await configuration.GetAsync(cancellationToken);
        var policy = config.Document.LifecyclePolicies.SafelyContained;
        var now = time.GetUtcNow().UtcDateTime;
        var person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Person not found.");
        var identities = await db.ExternalIdentities.AsNoTracking().Where(i => i.PersonId == personId).ToListAsync(cancellationToken);
        var links = (await db.IdentityLinks.AsNoTracking().Where(l => l.PersonId == personId).ToListAsync(cancellationToken))
            .Where(l => l.IsEffective(now)).ToList();

        var actions = new List<PlannedContainmentAction>();
        var unresolved = new List<UnresolvedLink>();
        var notes = new List<string>();
        var confirmed = new List<ExternalIdentity>();

        foreach (var identity in identities.OrderBy(i => i.System).ThenBy(i => i.StableObjectId, StringComparer.Ordinal))
        {
            var link = links.Where(l => l.TargetIdentityId == identity.Id).OrderByDescending(l => Rank(l.Confidence)).FirstOrDefault();
            if (link is not null && IdentityLinkPolicy.IsContainmentEligible(link.Confidence))
            {
                confirmed.Add(identity);
            }
            else if (link is not null)
            {
                unresolved.Add(new UnresolvedLink(link.Id, identity.Id, $"{identity.System} {identity.DisplayLabel}", link.Confidence, link.EvidenceType.ToString()));
            }
            else
            {
                notes.Add($"{identity.System} {identity.DisplayLabel} is associated with the person but has no link record; treated as unresolved.");
            }
        }

        var okta = confirmed.Where(i => i.System == SystemKind.Okta).ToList();
        var directory = confirmed.Where(i => i.System == SystemKind.ActiveDirectory).ToList();

        // Step 1: the authoritative authentication system.
        foreach (var o in okta)
        {
            var decision = await authority.ResolveAsync(o, LifecycleAction.Leaver, AttributeSet.AccountEnabledState, cancellationToken);
            if (!decision.IsResolved)
            {
                actions.Add(Manual(ContainmentStep.AuthenticationContainment, SystemKind.Okta, o, decision, "n/a", $"Authority unresolved ({decision.Outcome}): {decision.Explanation}", false));
            }
            else if (decision.ContainmentOwner != SystemKind.Okta)
            {
                actions.Add(Manual(ContainmentStep.AuthenticationContainment, SystemKind.Okta, o, decision, "n/a", $"Containment owner {decision.ContainmentOwner} is not supported for Okta identities.", false));
            }
            else if (!config.IsEnabled(Feature.OktaContainment))
            {
                actions.Add(Manual(ContainmentStep.AuthenticationContainment, SystemKind.Okta, o, decision, "n/a", "Okta containment is disabled by feature flag.", false));
            }
            else
            {
                actions.Add(Action(ContainmentStep.AuthenticationContainment, ContainmentMethod.OktaApi, SystemKind.Okta, null, o, decision.ToAuditString(), "n/a", true));
            }
        }

        var directoryIsAuthenticationAuthority = okta.Count == 0;
        if (directoryIsAuthenticationAuthority && directory.Count > 0)
        {
            notes.Add("No Okta identity is linked, so directory accounts are the authoritative authentication system.");
        }

        foreach (var d in directory)
        {
            actions.Add(await PlanDirectoryAsync(d, directoryIsAuthenticationAuthority, okta.Count > 0, config, cancellationToken));
        }

        if (okta.Count == 0 && directory.Count == 0)
        {
            actions.Add(new PlannedContainmentAction("auth:none", ContainmentStep.AuthenticationContainment, ContainmentMethod.ManualControlled, SystemKind.Manual, null, null,
                person.PersonIdentifier, person.DisplayName, null, null, true, "Missing", "n/a",
                "No confirmed account is linked to this person. Identify and contain every account manually.", false, false));
        }

        if (directory.Count == 0)
        {
            actions.Add(new PlannedContainmentAction("directory:none", ContainmentStep.DirectoryDisable, ContainmentMethod.VerifyOnly, SystemKind.ActiveDirectory, null, null,
                person.PersonIdentifier, "No directory account", null, null, true, "n/a", "n/a", null, Skipped: true, Tier0: false));
        }

        // Step 2: live sessions, per the SafelyContained policy.
        var anyAccount = okta.Count > 0 || directory.Count > 0;
        foreach (var (system, requirement) in policy.SessionRequirements.OrderBy(kv => kv.Key))
        {
            if (requirement == SessionRequirement.NotApplicable)
            {
                continue;
            }

            var applicable = system == SessionSystem.Okta ? okta.Count > 0 : anyAccount;
            actions.Add(new PlannedContainmentAction(
                $"session:{system}",
                ContainmentStep.SessionRevocation,
                ContainmentMethod.SessionConnector,
                SystemFor(system),
                system,
                null,
                person.PersonIdentifier,
                $"{system} sessions for {person.DisplayName}",
                null,
                null,
                requirement == SessionRequirement.Mandatory,
                "policy:SafelyContained",
                "n/a",
                null,
                Skipped: !applicable,
                Tier0: false));
        }

        var requiresSecurity = actions.Any(a => a.Method == ContainmentMethod.ContainmentOnlyLegacy || a.Tier0);
        return new LeaverPlan(
            person.Id,
            person.DisplayName,
            config.Version,
            actions,
            unresolved,
            requiresSecurity ? AppRole.SecurityApprover : AppRole.LifecycleApprover,
            notes);
    }

    private async Task<PlannedContainmentAction> PlanDirectoryAsync(ExternalIdentity d, bool isAuthenticationAuthority, bool hasOkta, ActiveConfiguration config, CancellationToken cancellationToken)
    {
        var step = isAuthenticationAuthority ? ContainmentStep.AuthenticationContainment : ContainmentStep.DirectoryDisable;
        var decision = await authority.ResolveAsync(d, LifecycleAction.Leaver, AttributeSet.AccountEnabledState, cancellationToken);
        var protectionDecision = d.ConnectorId is not null && d.ObjectGuid is not null
            ? await protection.EvaluateAsync(d.ConnectorId, d.ObjectGuid.Value, cancellationToken)
            : ProtectionDecision.UnknownBecause("No connector or objectGUID.");
        var protectionText = protectionDecision.ToAuditString();

        if (protectionDecision.Status == ProtectionStatus.Protected)
        {
            return Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, "Protected (Tier 0 or Tier 0-adjacent) object: ILM will not act; perform from a Tier 0 PAW.", true);
        }

        if (protectionDecision.Status == ProtectionStatus.Unknown)
        {
            return Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, "Protection could not be conclusively evaluated; automated containment denied.", true);
        }

        if (!decision.IsResolved)
        {
            return Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, $"Authority unresolved ({decision.Outcome}): {decision.Explanation}", false);
        }

        var connector = config.FindConnector(d.ConnectorId!);
        switch (decision.ContainmentOwner)
        {
            case SystemKind.Okta:
                return hasOkta
                    ? Action(step, ContainmentMethod.VerifyOnly, SystemKind.ActiveDirectory, null, d, decision.ToAuditString(), protectionText, true)
                    : Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, "Containment owner is Okta but no Okta identity is linked.", false);

            case SystemKind.ActiveDirectory or SystemKind.Ilm:
                if (decision.Strategy == StrategyKind.ContainmentOnlyLegacy)
                {
                    var reason = connector is not { Mode: ConnectorMode.ContainmentOnlyLegacy, Role: ForestRole.Legacy } ? "The connector is not in ContainmentOnlyLegacy mode."
                        : d.AccountType != AccountType.Standard ? "ContainmentOnlyLegacy disables standard accounts only."
                        : !config.IsEnabled(Feature.LegacyContainment) ? "Legacy containment is disabled by feature flag."
                        : null;
                    return reason is null
                        ? Action(step, ContainmentMethod.ContainmentOnlyLegacy, SystemKind.ActiveDirectory, null, d, decision.ToAuditString(), protectionText, true)
                        : Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, reason, false);
                }

                if (decision.Strategy == StrategyKind.DirectActiveDirectory)
                {
                    var reason = !config.IsEnabled(Feature.DirectActiveDirectoryContainment) ? "Direct AD containment is disabled by feature flag."
                        : connector is not { Mode: ConnectorMode.WriteTarget, Role: ForestRole.Target } ? "The target connector is not in WriteTarget mode."
                        : null;
                    return reason is null
                        ? Action(step, ContainmentMethod.DirectActiveDirectory, SystemKind.ActiveDirectory, null, d, decision.ToAuditString(), protectionText, true)
                        : Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, reason, false);
                }

                return Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, $"Strategy {decision.Strategy} cannot perform containment for this population.", false);

            default:
                return Manual(step, SystemKind.ActiveDirectory, d, decision, protectionText, $"Containment owner {decision.ContainmentOwner?.ToString() ?? "(none)"} is not supported.", false);
        }
    }

    private static PlannedContainmentAction Action(ContainmentStep step, ContainmentMethod method, SystemKind system, SessionSystem? session, ExternalIdentity identity, string authorityText, string protectionText, bool mandatory) =>
        new($"{step}:{identity.System}:{identity.StableObjectId}", step, method, system, session, identity.Id, identity.StableObjectId,
            $"{identity.System} {identity.DisplayLabel} ({identity.ConnectorId ?? identity.ForestOrTenantId})", identity.ConnectorId, identity.ForestOrTenantId,
            mandatory, authorityText, protectionText, null, false, false);

    private static PlannedContainmentAction Manual(ContainmentStep step, SystemKind system, ExternalIdentity identity, AuthorityDecision decision, string protectionText, string reason, bool tier0) =>
        new($"{step}:{identity.System}:{identity.StableObjectId}", step, ContainmentMethod.ManualControlled, system, null, identity.Id, identity.StableObjectId,
            $"{identity.System} {identity.DisplayLabel} ({identity.ConnectorId ?? identity.ForestOrTenantId})", identity.ConnectorId, identity.ForestOrTenantId,
            true, decision.ToAuditString(), protectionText, reason, false, tier0);

    private static SystemKind SystemFor(SessionSystem system) => system switch
    {
        SessionSystem.Okta => SystemKind.Okta,
        SessionSystem.Entra => SystemKind.Entra,
        SessionSystem.Citrix => SystemKind.Citrix,
        SessionSystem.Vpn => SystemKind.Vpn,
        _ => SystemKind.Application,
    };

    private static int Rank(LinkConfidence c) => c switch
    {
        LinkConfidence.Authoritative => 4,
        LinkConfidence.HumanApproved => 3,
        LinkConfidence.Provisional => 2,
        LinkConfidence.Ambiguous => 1,
        _ => 0,
    };
}
