using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
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
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ilm.Persistence;

/// <summary>
/// The ILM database. Audit records are sealed into the hash chain at save time under a process lock
/// (and a PostgreSQL advisory lock), and any attempt to modify or delete audit or transition history is refused.
/// </summary>
public abstract class IlmDbContext : DbContext, IIlmDbContext
{
    private const long AuditAdvisoryLockKey = 7_241_130_001;
    private static readonly SemaphoreSlim AuditAppendLock = new(1, 1);
    private readonly IAuditKeyProvider auditKeys;

    protected IlmDbContext(DbContextOptions options, IAuditKeyProvider auditKeys)
        : base(options)
    {
        this.auditKeys = auditKeys;
    }

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    public DbSet<IdentityLink> IdentityLinks => Set<IdentityLink>();

    public DbSet<MigrationState> MigrationStates => Set<MigrationState>();

    public DbSet<AuthorityRule> AuthorityRules => Set<AuthorityRule>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<IssuerMigration> IssuerMigrations => Set<IssuerMigration>();

    public DbSet<Approval> Approvals => Set<Approval>();

    public DbSet<ManualTask> ManualTasks => Set<ManualTask>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    public DbSet<AuditForwardingCheckpoint> AuditForwardingCheckpoints => Set<AuditForwardingCheckpoint>();

    public DbSet<ConfigurationVersion> ConfigurationVersions => Set<ConfigurationVersion>();

    public DbSet<LeaverRequest> LeaverRequests => Set<LeaverRequest>();

    public DbSet<ContainmentAction> ContainmentActions => Set<ContainmentAction>();

    public DbSet<LeaverTransition> LeaverTransitions => Set<LeaverTransition>();

    public DbSet<FeasibilityRun> FeasibilityRuns => Set<FeasibilityRun>();

    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();

    public DbSet<ReconciliationFinding> ReconciliationFindings => Set<ReconciliationFinding>();

    public DbSet<DistributedLockLease> LockLeases => Set<DistributedLockLease>();

    protected abstract bool IsPostgres { get; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync; audit sealing is asynchronous.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceImmutableHistory();
        var pending = ChangeTracker.Entries<AuditRecord>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        if (pending.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        await AuditAppendLock.WaitAsync(cancellationToken);
        try
        {
            var ownTransaction = Database.CurrentTransaction is null;
            await using var transaction = ownTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
            if (IsPostgres)
            {
                await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AuditAdvisoryLockKey})", cancellationToken);
            }

            var last = await AuditRecords.AsNoTracking()
                .OrderByDescending(r => r.Sequence)
                .Select(r => new { r.Sequence, r.Hash })
                .FirstOrDefaultAsync(cancellationToken);
            var sequence = last?.Sequence ?? 0;
            var previous = last?.Hash ?? AuditChain.GenesisHash;
            var key = auditKeys.CurrentKey;
            foreach (var record in pending)
            {
                AuditChain.Seal(record, ++sequence, previous, key);
                previous = record.Hash;
            }

            var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return written;
        }
        finally
        {
            AuditAppendLock.Release();
        }
    }

    private void EnforceImmutableHistory()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditRecord or LeaverTransition && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only; modification and deletion are not permitted.");
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(64);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IlmDbContext).Assembly);

        var activeFilter = IsPostgres ? "\"IsActive\" = TRUE" : "\"IsActive\" = 1";
        modelBuilder.Entity<LeaverRequest>()
            .HasIndex(r => r.PersonId)
            .IsUnique()
            .HasFilter(activeFilter)
            .HasDatabaseName("UX_LeaverRequests_OneActivePerPerson");
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.ToUniversalTime(), DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value.ToUniversalTime(), DateTimeKind.Utc),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}

/// <summary>SQLite context: isolated development and tests only.</summary>
public sealed class SqliteIlmDbContext(DbContextOptions<SqliteIlmDbContext> options, IAuditKeyProvider auditKeys) : IlmDbContext(options, auditKeys)
{
    protected override bool IsPostgres => false;
}

/// <summary>PostgreSQL context: production.</summary>
public sealed class PostgresIlmDbContext(DbContextOptions<PostgresIlmDbContext> options, IAuditKeyProvider auditKeys) : IlmDbContext(options, auditKeys)
{
    public const string Schema = "ilm";

    protected override bool IsPostgres => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }
}
