using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Security;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Identity;

/// <summary>
/// Person-to-account linking. Automatic evidence can never exceed the policy ceiling (email-only is Provisional,
/// name-only is Ambiguous); approval needs a second person and non-name evidence.
/// </summary>
public sealed class IdentityLinkService(IIlmDbContext db, IAuditWriter audit, TimeProvider time)
{
    public async Task<IdentityLink> ProposeAsync(Guid personId, Guid identityId, EvidenceType evidence, LinkMethod method, string? evidenceReference, int candidateCount, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.ProposeIdentityLink))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Proposing identity links requires the Lifecycle Operator role.");
        }

        var identity = await db.ExternalIdentities.FirstOrDefaultAsync(i => i.Id == identityId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Identity not found.");
        if (!await db.Persons.AnyAsync(p => p.Id == personId, cancellationToken))
        {
            throw new DomainException(SafeErrorCategory.NotFound, "Person not found.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var link = new IdentityLink
        {
            PersonId = personId,
            TargetIdentityId = identityId,
            EvidenceType = evidence,
            LinkMethod = method,
            EvidenceReference = AuditSanitizer.SanitizeText(evidenceReference),
            Confidence = IdentityLinkPolicy.AutomaticConfidence(evidence, candidateCount),
            CreatedByUserId = actor.AppUserId,
            CreatedUtc = now,
            EffectiveFrom = now,
        };
        db.IdentityLinks.Add(link);
        identity.PersonId ??= personId;
        audit.Append(new AuditEvent
        {
            Action = "IdentityLinkProposed",
            Result = link.Confidence.ToString(),
            TargetStableId = identity.StableObjectId,
            RequestedValues = new { personId, identityId, evidence = evidence.ToString(), method = method.ToString(), candidateCount },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task<IdentityLink> ApproveAsync(Guid linkId, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.ApproveIdentityLink) || !actor.RolesFreshlyResolved || actor.AppUserId is null)
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Approving identity links requires a freshly verified approver role.");
        }

        var link = await db.IdentityLinks.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Link not found.");
        var before = link.Confidence;
        if (link.CreatedByUserId is { } proposer && proposer != actor.AppUserId.Value
            && await Security.AppUserService.IsSamePersonAsync(db, proposer, actor.AppUserId.Value, cancellationToken))
        {
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "The person who proposed a link cannot approve it, including under a migrated identity.");
        }

        IdentityLinkPolicy.Approve(link, actor.AppUserId.Value, actor.Label, time.GetUtcNow().UtcDateTime);
        audit.Append(new AuditEvent
        {
            Action = "IdentityLinkApproved",
            Result = link.Confidence.ToString(),
            TargetStableId = link.TargetIdentityId.ToString("D"),
            BeforeValues = new { confidence = before.ToString() },
            AppliedValues = new { confidence = link.Confidence.ToString(), evidence = link.EvidenceType.ToString() },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task<IdentityLink> RejectAsync(Guid linkId, string reason, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.ApproveIdentityLink))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Rejecting identity links requires an approver role.");
        }

        var link = await db.IdentityLinks.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Link not found.");
        var before = link.Confidence;
        link.Confidence = LinkConfidence.Rejected;
        link.EffectiveUntil = time.GetUtcNow().UtcDateTime;
        var identity = await db.ExternalIdentities.FirstOrDefaultAsync(i => i.Id == link.TargetIdentityId, cancellationToken);
        if (identity?.PersonId == link.PersonId
            && !await db.IdentityLinks.AnyAsync(l => l.Id != link.Id && l.TargetIdentityId == identity.Id && l.PersonId == link.PersonId && l.Confidence != LinkConfidence.Rejected, cancellationToken))
        {
            identity.PersonId = null;
        }

        audit.Append(new AuditEvent
        {
            Action = "IdentityLinkRejected",
            Result = "Rejected",
            TargetStableId = link.TargetIdentityId.ToString("D"),
            BeforeValues = new { confidence = before.ToString() },
            RequestedValues = new { reason },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }
}
