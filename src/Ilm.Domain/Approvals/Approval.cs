using Ilm.Domain.Security;

namespace Ilm.Domain.Approvals;

public enum ApprovalStatus
{
    Pending = 0,
    Approved,
    Rejected,
    Expired,
    Invalidated,
}

public enum ApprovalSubjectType
{
    LeaverRequest = 0,
    ConfigurationVersion,
    IdentityLink,
    IssuerMigration,
    FeasibilityReport,
    ManualTaskVerification,
}

/// <summary>
/// A request for a second person to approve a specific content hash. Changing the content
/// invalidates the approval; the requester can never approve their own request.
/// </summary>
public sealed class Approval
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ApprovalSubjectType SubjectType { get; set; }

    public string SubjectId { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public AppRole RequiredRole { get; set; }

    public Guid RequestedByUserId { get; set; }

    public string RequestedByLabel { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public Guid? DecidedByUserId { get; set; }

    public string? DecidedByLabel { get; set; }

    public DateTime? DecidedUtc { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public string? InvalidationReason { get; set; }

    public long ConfigurationVersion { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
