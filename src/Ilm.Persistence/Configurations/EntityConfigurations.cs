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
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ilm.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.HasKey(p => p.Id);
        b.HasIndex(p => p.PersonIdentifier).IsUnique();
        b.Property(p => p.PersonIdentifier).HasMaxLength(64);
        b.Property(p => p.DisplayName).HasMaxLength(256);
        b.Property(p => p.EmployeeIdentifier).HasMaxLength(64);
        b.Property(p => p.BusinessEntity).HasMaxLength(128);
        b.Property(p => p.Department).HasMaxLength(128);
    }
}

internal sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> b)
    {
        b.HasKey(i => i.Id);
        b.HasIndex(i => new { i.System, i.ForestOrTenantId, i.StableObjectId }).IsUnique();
        b.HasIndex(i => i.PersonId);
        b.HasIndex(i => i.ObjectGuid);
        b.Property(i => i.ForestOrTenantId).HasMaxLength(256);
        b.Property(i => i.StableObjectId).HasMaxLength(256);
        b.Property(i => i.ConnectorId).HasMaxLength(64);
        b.Property(i => i.ObjectSid).HasMaxLength(184);
        b.Property(i => i.DistinguishedName).HasMaxLength(1024);
        b.Property(i => i.SamAccountName).HasMaxLength(256);
        b.Property(i => i.UserPrincipalName).HasMaxLength(1024);
        b.Property(i => i.Mail).HasMaxLength(256);
        b.Property(i => i.OktaIssuer).HasMaxLength(512);
        b.Property(i => i.OktaSubject).HasMaxLength(256);
        b.Property(i => i.Population).HasMaxLength(128);
        b.Ignore(i => i.DisplayLabel);
    }
}

internal sealed class IdentityLinkConfiguration : IEntityTypeConfiguration<IdentityLink>
{
    public void Configure(EntityTypeBuilder<IdentityLink> b)
    {
        b.HasKey(l => l.Id);
        b.HasIndex(l => l.PersonId);
        b.HasIndex(l => l.TargetIdentityId);
        b.Property(l => l.EvidenceReference).HasMaxLength(512);
        b.Property(l => l.ApprovedBy).HasMaxLength(512);
    }
}

internal sealed class MigrationStateConfiguration : IEntityTypeConfiguration<MigrationState>
{
    public void Configure(EntityTypeBuilder<MigrationState> b)
    {
        b.HasKey(m => m.PersonId);
        b.Ignore(m => m.IsApprovedTransition);
    }
}

internal sealed class AuthorityRuleConfiguration : IEntityTypeConfiguration<AuthorityRule>
{
    public void Configure(EntityTypeBuilder<AuthorityRule> b)
    {
        b.HasKey(r => r.Id);
        b.HasIndex(r => r.ApprovedConfigurationVersion);
        b.Property(r => r.Population).HasMaxLength(128);
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.HasKey(u => u.Id);
        b.HasIndex(u => new { u.Issuer, u.Subject }).IsUnique();
        b.Property(u => u.Issuer).HasMaxLength(512);
        b.Property(u => u.Subject).HasMaxLength(256);
        b.Property(u => u.DisplayName).HasMaxLength(256);
        b.Property(u => u.LastSeenEmail).HasMaxLength(256);
        b.Ignore(u => u.Label);
    }
}

internal sealed class IssuerMigrationConfiguration : IEntityTypeConfiguration<IssuerMigration>
{
    public void Configure(EntityTypeBuilder<IssuerMigration> b) => b.HasKey(m => m.Id);
}

internal sealed class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> b)
    {
        b.HasKey(a => a.Id);
        b.HasIndex(a => new { a.SubjectType, a.SubjectId });
        b.HasIndex(a => a.Status);
        b.Property(a => a.ConcurrencyStamp).IsConcurrencyToken();
        b.Property(a => a.ContentHash).HasMaxLength(64);
    }
}

internal sealed class ManualTaskConfiguration : IEntityTypeConfiguration<ManualTask>
{
    public void Configure(EntityTypeBuilder<ManualTask> b)
    {
        b.HasKey(t => t.Id);
        b.HasIndex(t => t.Status);
        b.HasIndex(t => t.LeaverRequestId);
    }
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> b)
    {
        b.HasKey(a => a.Id);
        b.HasIndex(a => a.CreatedUtc);
        b.Ignore(a => a.IsOpen);
    }
}

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> b)
    {
        b.HasKey(r => r.EventId);
        b.Property(r => r.EventId).ValueGeneratedNever();
        b.HasIndex(r => r.Sequence).IsUnique();
        b.HasIndex(r => r.OperationId);
        b.HasIndex(r => r.TimestampUtc);
        b.HasIndex(r => r.Action);
        b.Property(r => r.Hash).HasMaxLength(64);
        b.Property(r => r.PreviousHash).HasMaxLength(64);
        b.Property(r => r.Mac).HasMaxLength(64);
        b.Property(r => r.Action).HasMaxLength(128);
    }
}

internal sealed class AuditCheckpointConfiguration : IEntityTypeConfiguration<AuditForwardingCheckpoint>
{
    public void Configure(EntityTypeBuilder<AuditForwardingCheckpoint> b)
    {
        b.HasKey(c => c.SinkName);
        b.Property(c => c.SinkName).HasMaxLength(128);
    }
}

internal sealed class ConfigurationVersionConfiguration : IEntityTypeConfiguration<ConfigurationVersion>
{
    public void Configure(EntityTypeBuilder<ConfigurationVersion> b)
    {
        b.HasKey(v => v.Id);
        b.HasIndex(v => v.Version).IsUnique();
        b.HasIndex(v => v.Status);
    }
}

internal sealed class LeaverRequestConfiguration : IEntityTypeConfiguration<LeaverRequest>
{
    public void Configure(EntityTypeBuilder<LeaverRequest> b)
    {
        b.HasKey(r => r.Id);
        b.HasIndex(r => r.IdempotencyKey).IsUnique();
        b.HasIndex(r => r.State);
        b.Property(r => r.IdempotencyKey).HasMaxLength(128);
        b.Property(r => r.TicketReference).HasMaxLength(128);
        b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
        b.HasMany(r => r.Actions).WithOne().HasForeignKey(a => a.LeaverRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Transitions).WithOne().HasForeignKey(t => t.LeaverRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContainmentActionConfiguration : IEntityTypeConfiguration<ContainmentAction>
{
    public void Configure(EntityTypeBuilder<ContainmentAction> b)
    {
        b.HasKey(a => a.Id);
        b.HasIndex(a => a.IdempotencyKey).IsUnique();
        b.Property(a => a.IdempotencyKey).HasMaxLength(200);
        b.Ignore(a => a.IsResolved);
    }
}

internal sealed class LeaverTransitionConfiguration : IEntityTypeConfiguration<LeaverTransition>
{
    public void Configure(EntityTypeBuilder<LeaverTransition> b)
    {
        b.HasKey(t => t.Id);
        b.HasIndex(t => new { t.LeaverRequestId, t.TimestampUtc });
    }
}

internal sealed class FeasibilityRunConfiguration : IEntityTypeConfiguration<FeasibilityRun>
{
    public void Configure(EntityTypeBuilder<FeasibilityRun> b) => b.HasKey(r => r.Id);
}

internal sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> b) => b.HasKey(r => r.Id);
}

internal sealed class ReconciliationFindingConfiguration : IEntityTypeConfiguration<ReconciliationFinding>
{
    public void Configure(EntityTypeBuilder<ReconciliationFinding> b)
    {
        b.HasKey(f => f.Id);
        b.HasIndex(f => f.RunId);
    }
}

internal sealed class LockLeaseConfiguration : IEntityTypeConfiguration<DistributedLockLease>
{
    public void Configure(EntityTypeBuilder<DistributedLockLease> b)
    {
        b.HasKey(l => l.LockKey);
        b.Property(l => l.LockKey).HasMaxLength(256);
        b.Property(l => l.Owner).HasMaxLength(128);
    }
}
