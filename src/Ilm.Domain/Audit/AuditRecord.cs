namespace Ilm.Domain.Audit;

/// <summary>
/// One append-only, hash-chained audit event. Never updated or deleted. <see cref="Hash"/> covers every
/// field plus <see cref="PreviousHash"/>; <see cref="Mac"/> is an HMAC over the hash with a key held outside the database.
/// </summary>
public sealed class AuditRecord
{
    public long Sequence { get; set; }

    public Guid EventId { get; set; } = Guid.NewGuid();

    public DateTime TimestampUtc { get; set; }

    public string PreviousHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public string Mac { get; set; } = string.Empty;

    public string MacKeyId { get; set; } = string.Empty;

    public Guid? OperationId { get; set; }

    public Guid? CorrelationId { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? ActorIssuer { get; set; }

    public string? ActorSubject { get; set; }

    public Guid? AppUserId { get; set; }

    public string? EffectiveRoles { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? TargetStableId { get; set; }

    public string? Domain { get; set; }

    public string? Forest { get; set; }

    public string? OuGuid { get; set; }

    public string? AuthorityDecision { get; set; }

    public string? ProtectionDecision { get; set; }

    public string? ScopeDecision { get; set; }

    public string? Approval { get; set; }

    public long? ConfigurationVersion { get; set; }

    public string? BeforeValues { get; set; }

    public string? RequestedValues { get; set; }

    public string? AppliedValues { get; set; }

    public string? SelectedConnector { get; set; }

    public string? SelectedDomainController { get; set; }

    public string? AttemptedActions { get; set; }

    public string? VerifiedActions { get; set; }

    public string? WorkflowState { get; set; }

    public string Result { get; set; } = string.Empty;

    public long? DurationMs { get; set; }

    public string? ExceptionCategory { get; set; }

    public string? ReconciliationResults { get; set; }
}

/// <summary>Records how far the audit stream has been forwarded to each off-box sink.</summary>
public sealed class AuditForwardingCheckpoint
{
    public string SinkName { get; set; } = string.Empty;

    public long LastForwardedSequence { get; set; }

    public string? LastForwardedHash { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
