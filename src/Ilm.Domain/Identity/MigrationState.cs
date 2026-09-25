using Ilm.Domain.Common;

namespace Ilm.Domain.Identity;

/// <summary>Where a person is in the forest-consolidation migration.</summary>
public sealed class MigrationState
{
    public Guid PersonId { get; set; }

    public string? MigrationWave { get; set; }

    public Guid? LegacyIdentityId { get; set; }

    public Guid? TargetIdentityId { get; set; }

    public SystemKind AuthenticationAuthority { get; set; } = SystemKind.ActiveDirectory;

    public SystemKind ProvisioningAuthority { get; set; } = SystemKind.ActiveDirectory;

    public SystemKind AccountEnabledStateOwner { get; set; } = SystemKind.ActiveDirectory;

    public Guid? OktaIdentityId { get; set; }

    public Guid? CloudIdentityId { get; set; }

    public MigrationStateKind State { get; set; } = MigrationStateKind.NotStarted;

    public DateOnly? CutoverDate { get; set; }

    public DateOnly? RollbackDeadline { get; set; }

    /// <summary>A transition state that has been approved for migration exception writes.</summary>
    public bool IsApprovedTransition => State == MigrationStateKind.ApprovedTransition;
}

public enum MigrationStateKind
{
    NotStarted = 0,
    Scheduled,
    ApprovedTransition,
    CutOver,
    RolledBack,
    Completed,
}
