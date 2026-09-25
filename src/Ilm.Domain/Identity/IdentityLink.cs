namespace Ilm.Domain.Identity;

/// <summary>
/// Evidence that an identity belongs to a person, optionally as the migration successor of a source identity.
/// </summary>
public sealed class IdentityLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PersonId { get; set; }

    /// <summary>For migration links: the legacy identity. Null for plain person-to-account links.</summary>
    public Guid? SourceIdentityId { get; set; }

    public Guid TargetIdentityId { get; set; }

    public LinkMethod LinkMethod { get; set; }

    public EvidenceType EvidenceType { get; set; }

    public string? EvidenceReference { get; set; }

    public LinkConfidence Confidence { get; set; } = LinkConfidence.Provisional;

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string? ApprovedBy { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedUtc { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveUntil { get; set; }

    public bool IsEffective(DateTime atUtc) =>
        Confidence != LinkConfidence.Rejected
        && EffectiveFrom <= atUtc
        && (EffectiveUntil is null || EffectiveUntil > atUtc);
}

/// <summary>The person-link states from the identity-linking specification.</summary>
public enum LinkConfidence
{
    Provisional = 0,
    Ambiguous,
    HumanApproved,
    Authoritative,
    Rejected,
}

public enum LinkMethod
{
    ManualAssertion = 0,
    SourceAnchorMatch,
    MigrationToolMapping,
    EmployeeIdentifierMatch,
    EmailMatch,
    NameMatch,
    OktaImportCorrelation,
}

/// <summary>Kinds of evidence. A link can carry several.</summary>
[Flags]
public enum EvidenceType
{
    None = 0,
    SourceAnchor = 1,
    MigrationMapping = 2,
    EmployeeRecord = 4,
    EmailAttribute = 8,
    NameSimilarity = 16,
    HumanAttestation = 32,
    TicketReference = 64,
    OktaImportMatch = 128,
}
