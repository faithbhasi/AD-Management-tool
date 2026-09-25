using Ilm.Domain.Tasks;

namespace Ilm.Domain.Reconciliation;

public enum ReconciliationFindingKind
{
    InSync = 0,
    ObjectMissing,
    DistinguishedNameChanged,
    EnabledStateChanged,
    ContainedAccountReEnabled,
    ProtectionChanged,
    ConnectorUnavailable,
    OutOfBandChange,
}

public sealed class ReconciliationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public int IdentitiesChecked { get; set; }

    public int FindingsCount { get; set; }

    public string Trigger { get; set; } = string.Empty;
}

public sealed class ReconciliationFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RunId { get; set; }

    public Guid? IdentityId { get; set; }

    public ReconciliationFindingKind Kind { get; set; }

    public AlertSeverity Severity { get; set; }

    public string Detail { get; set; } = string.Empty;

    public DateTime DetectedUtc { get; set; }
}
