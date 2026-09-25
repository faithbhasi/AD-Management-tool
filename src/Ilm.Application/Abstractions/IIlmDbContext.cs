using Ilm.Domain.Approvals;
using Ilm.Domain.Audit;
using Ilm.Domain.Authority;
using Ilm.Domain.Configuration;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Reconciliation;
using Ilm.Domain.Security;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Abstractions;

/// <summary>The persistence port. Implemented by Ilm.Persistence for SQLite (development) and PostgreSQL (production).</summary>
public interface IIlmDbContext
{
    DbSet<Person> Persons { get; }

    DbSet<ExternalIdentity> ExternalIdentities { get; }

    DbSet<IdentityLink> IdentityLinks { get; }

    DbSet<MigrationState> MigrationStates { get; }

    DbSet<AuthorityRule> AuthorityRules { get; }

    DbSet<AppUser> AppUsers { get; }

    DbSet<IssuerMigration> IssuerMigrations { get; }

    DbSet<Approval> Approvals { get; }

    DbSet<ManualTask> ManualTasks { get; }

    DbSet<Alert> Alerts { get; }

    DbSet<AuditRecord> AuditRecords { get; }

    DbSet<AuditForwardingCheckpoint> AuditForwardingCheckpoints { get; }

    DbSet<ConfigurationVersion> ConfigurationVersions { get; }

    DbSet<LeaverRequest> LeaverRequests { get; }

    DbSet<ContainmentAction> ContainmentActions { get; }

    DbSet<LeaverTransition> LeaverTransitions { get; }

    DbSet<FeasibilityRun> FeasibilityRuns { get; }

    DbSet<ReconciliationRun> ReconciliationRuns { get; }

    DbSet<ReconciliationFinding> ReconciliationFindings { get; }

    DbSet<DistributedLockLease> LockLeases { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
