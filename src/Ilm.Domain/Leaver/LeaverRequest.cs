using Ilm.Domain.Common;

namespace Ilm.Domain.Leaver;

/// <summary>A leaver case. Its Id is the operation ID for every related audit record.</summary>
public sealed class LeaverRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PersonId { get; set; }

    public LeaverState State { get; set; } = LeaverState.Requested;

    public LeaverUrgency Urgency { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string TicketReference { get; set; } = string.Empty;

    public DateTime EffectiveUtc { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>SHA-256 of the canonical request payload, used to detect idempotency-key reuse with different content.</summary>
    public string RequestFingerprint { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public Guid RequestedByUserId { get; set; }

    public string RequestedByLabel { get; set; } = string.Empty;

    public Guid? ApprovalId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public string? PlanJson { get; set; }

    public string? PlanHash { get; set; }

    public long ConfigurationVersion { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public DateTime? ContainmentStartedUtc { get; set; }

    public DateTime? SafelyContainedUtc { get; set; }

    public DateTime? SlaDueUtc { get; set; }

    /// <summary>Only one non-terminal leaver per person; enforced by a filtered unique index on this flag.</summary>
    public bool IsActive { get; set; } = true;

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public SafeErrorCategory LastErrorCategory { get; set; }

    public List<ContainmentAction> Actions { get; set; } = [];

    public List<LeaverTransition> Transitions { get; set; } = [];
}

/// <summary>One containment control for one target in one system.</summary>
public sealed class ContainmentAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LeaverRequestId { get; set; }

    public int Sequence { get; set; }

    public ContainmentStep Step { get; set; }

    public ContainmentMethod Method { get; set; }

    public SystemKind System { get; set; }

    public SessionSystem? SessionSystem { get; set; }

    public Guid? TargetIdentityId { get; set; }

    public string TargetStableId { get; set; } = string.Empty;

    public string TargetLabel { get; set; } = string.Empty;

    public string? ConnectorId { get; set; }

    public string? ForestId { get; set; }

    public bool Mandatory { get; set; }

    public ContainmentActionStatus Status { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? SelectedDomainController { get; set; }

    public int Attempts { get; set; }

    public SafeErrorCategory ErrorCategory { get; set; }

    public string? Evidence { get; set; }

    public string? AuthorityDecision { get; set; }

    public string? ProtectionDecision { get; set; }

    public DateTime? AttemptedUtc { get; set; }

    public DateTime? VerifiedUtc { get; set; }

    public Guid? ManualTaskId { get; set; }

    public bool ChangedExternalState { get; set; }

    public bool IsResolved => Status is ContainmentActionStatus.Verified or ContainmentActionStatus.Skipped
        || (Step == ContainmentStep.SessionRevocation && Status == ContainmentActionStatus.Succeeded);
}

/// <summary>An immutable record of one durable state transition.</summary>
public sealed class LeaverTransition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LeaverRequestId { get; set; }

    public Guid OperationId { get; set; }

    public Guid CorrelationId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string? Approver { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string TicketReference { get; set; } = string.Empty;

    public LeaverState PreviousState { get; set; }

    public LeaverState NextState { get; set; }

    public string AuthorityDecision { get; set; } = string.Empty;

    public string ProtectionDecision { get; set; } = string.Empty;

    public long ConfigurationVersion { get; set; }

    public string ActionsAttempted { get; set; } = string.Empty;

    public string ActionsVerified { get; set; } = string.Empty;

    public string UnresolvedActions { get; set; } = string.Empty;

    public SafeErrorCategory SafeErrorCategory { get; set; }
}
