using Ilm.Domain.Common;

namespace Ilm.Domain.Identity;

/// <summary>
/// Rules for how much trust a link may carry. Email-only matching is always Provisional;
/// name-only matching can never produce an approved link.
/// </summary>
public static class IdentityLinkPolicy
{
    private const EvidenceType StrongSystemEvidence = EvidenceType.SourceAnchor | EvidenceType.MigrationMapping;

    /// <summary>The highest confidence an automatic (non-human) match may assign.</summary>
    public static LinkConfidence AutomaticConfidence(EvidenceType evidence, int candidateCount)
    {
        if (candidateCount > 1)
        {
            return LinkConfidence.Ambiguous;
        }

        if ((evidence & StrongSystemEvidence) != 0)
        {
            return LinkConfidence.Authoritative;
        }

        if (IsNameOnly(evidence))
        {
            return LinkConfidence.Ambiguous;
        }

        // Email, employee number (no HR source) and Okta import matches are never more than provisional.
        return evidence == EvidenceType.None ? LinkConfidence.Ambiguous : LinkConfidence.Provisional;
    }

    public static bool IsNameOnly(EvidenceType evidence)
    {
        var substantive = evidence & ~(EvidenceType.TicketReference | EvidenceType.HumanAttestation);
        return substantive == EvidenceType.NameSimilarity || substantive == EvidenceType.None;
    }

    public static bool IsEmailOnly(EvidenceType evidence)
    {
        var substantive = evidence & ~(EvidenceType.TicketReference | EvidenceType.HumanAttestation);
        return substantive == EvidenceType.EmailAttribute;
    }

    /// <summary>Checks whether a human may approve the link. Returns null when approval is allowed.</summary>
    public static string? ApprovalBlocker(IdentityLink link, Guid approverUserId)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (link.Confidence == LinkConfidence.Rejected)
        {
            return "A rejected link cannot be approved; create a new link with new evidence.";
        }

        if (link.Confidence is LinkConfidence.Authoritative or LinkConfidence.HumanApproved)
        {
            return "The link is already approved.";
        }

        if (IsNameOnly(link.EvidenceType))
        {
            return "Name-only evidence can never produce an approved link. Add stronger evidence first.";
        }

        if (link.CreatedByUserId == approverUserId)
        {
            return "The person who proposed a link cannot approve it.";
        }

        return null;
    }

    public static void Approve(IdentityLink link, Guid approverUserId, string approverLabel, DateTime nowUtc)
    {
        var blocker = ApprovalBlocker(link, approverUserId);
        if (blocker is not null)
        {
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, blocker);
        }

        link.Confidence = LinkConfidence.HumanApproved;
        link.EvidenceType |= EvidenceType.HumanAttestation;
        link.ApprovedByUserId = approverUserId;
        link.ApprovedBy = approverLabel;
        link.ApprovedUtc = nowUtc;
    }

    /// <summary>Only Authoritative and HumanApproved links are contained without further confirmation.</summary>
    public static bool IsContainmentEligible(LinkConfidence confidence) =>
        confidence is LinkConfidence.Authoritative or LinkConfidence.HumanApproved;
}
