namespace Ilm.Domain.Configuration;

public enum ConfigurationVersionStatus
{
    Proposed = 0,
    ValidationFailed,
    AwaitingApproval,
    Approved,
    Active,
    Superseded,
    Rejected,
}

/// <summary>An immutable, versioned configuration document moving through propose → validate → approve → activate.</summary>
public sealed class ConfigurationVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public long Version { get; set; }

    public ConfigurationVersionStatus Status { get; set; }

    public string DocumentJson { get; set; } = "{}";

    public string ContentHash { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? ValidationIssuesJson { get; set; }

    public Guid ProposedByUserId { get; set; }

    public string ProposedByLabel { get; set; } = string.Empty;

    public DateTime ProposedUtc { get; set; }

    public Guid? ApprovalId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedUtc { get; set; }

    public DateTime? ActivatedUtc { get; set; }

    /// <summary>The version that was active when this one was activated; the rollback target.</summary>
    public long? SupersedesVersion { get; set; }

    public long? RollbackOfVersion { get; set; }

    public bool IsBootstrap { get; set; }
}
