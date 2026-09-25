namespace Ilm.Application.Audit;

/// <summary>
/// Input for one audit record. Values are sanitised before they are stored; secrets and passwords never reach the record.
/// </summary>
public sealed record AuditEvent
{
    public required string Action { get; init; }

    public required string Result { get; init; }

    public Guid? OperationId { get; init; }

    public Guid? CorrelationId { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? TargetStableId { get; init; }

    public string? Domain { get; init; }

    public string? Forest { get; init; }

    public string? OuGuid { get; init; }

    public string? AuthorityDecision { get; init; }

    public string? ProtectionDecision { get; init; }

    public string? ScopeDecision { get; init; }

    public string? Approval { get; init; }

    public long? ConfigurationVersion { get; init; }

    public object? BeforeValues { get; init; }

    public object? RequestedValues { get; init; }

    public object? AppliedValues { get; init; }

    public string? SelectedConnector { get; init; }

    public string? SelectedDomainController { get; init; }

    public object? AttemptedActions { get; init; }

    public object? VerifiedActions { get; init; }

    public string? WorkflowState { get; init; }

    public long? DurationMs { get; init; }

    public string? ExceptionCategory { get; init; }

    public object? ReconciliationResults { get; init; }
}
