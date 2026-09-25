namespace Ilm.Domain.Tasks;

public enum ManualTaskKind
{
    ManualContainment = 0,
    Tier0Containment,
    IdentityLinkConfirmation,
    SessionRevocation,
    NonUrgentLeaverStage,
    ReconciliationDrift,
    ProtectionReview,
}

public enum ManualTaskStatus
{
    Open = 0,
    InProgress,
    AwaitingVerification,
    Completed,
    Cancelled,
}

/// <summary>
/// Work ILM cannot or must not automate. Containment tasks carry an SLA and a precise runbook,
/// and are not closed until ILM verifies the outcome where verification is possible.
/// </summary>
public sealed class ManualTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ManualTaskKind Kind { get; set; }

    public Guid? LeaverRequestId { get; set; }

    public Guid? ContainmentActionId { get; set; }

    public AlertSeverity Severity { get; set; }

    public string Title { get; set; } = string.Empty;

    public string RunbookMarkdown { get; set; } = string.Empty;

    public string TargetsJson { get; set; } = "[]";

    public DateTime CreatedUtc { get; set; }

    public DateTime? SlaDueUtc { get; set; }

    public bool SlaBreached { get; set; }

    public ManualTaskStatus Status { get; set; } = ManualTaskStatus.Open;

    public string AssignedRole { get; set; } = string.Empty;

    public bool VerificationRequired { get; set; }

    public Guid? CompletedByUserId { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public string? CompletionEvidence { get; set; }
}

public enum AlertSeverity
{
    Info = 0,
    Low,
    Medium,
    High,
    Critical,
}

public sealed class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AlertSeverity Severity { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Guid? OperationId { get; set; }

    public Guid? CorrelationId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }

    public DateTime? AcknowledgedUtc { get; set; }

    public bool IsOpen => AcknowledgedUtc is null;
}
