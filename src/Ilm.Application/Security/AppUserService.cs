using Ilm.Application.Abstractions;
using Ilm.Application.Approvals;
using Ilm.Application.Audit;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Security;

/// <summary>
/// Finds or creates the application user for (issuer, subject). It never matches on email, UPN,
/// username, display name or group names; a changed email updates informational fields only.
/// </summary>
public sealed class AppUserService(IIlmDbContext db, IAuditWriter audit, TimeProvider time)
{
    public async Task<AppUser> SignInAsync(string issuer, string subject, string? displayName, string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "An operator identity needs both issuer and subject.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject, cancellationToken);
        var created = false;
        var issuerChanged = false;
        if (user is null)
        {
            // The same subject active under another issuer means the issuer changed. That is an identity migration,
            // not a new operator: the new identity holds no roles until the migration is approved and applied.
            issuerChanged = await db.AppUsers.AnyAsync(u => u.Subject == subject && u.Issuer != issuer && u.Status == AppUserStatus.Active, cancellationToken);
            user = new AppUser
            {
                Issuer = issuer,
                Subject = subject,
                CreatedUtc = now,
                Status = issuerChanged ? AppUserStatus.PendingIssuerMigration : AppUserStatus.Active,
            };
            db.AppUsers.Add(user);
            created = true;
        }

        if (user.Status == AppUserStatus.Disabled)
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "This operator account is disabled in ILM.");
        }

        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? subject : displayName.Trim();
        user.LastSeenEmail = email;
        user.LastSignInUtc = now;

        var actor = new ActorContext { AppUserId = user.Id, Issuer = issuer, Subject = subject, DisplayName = user.DisplayName };
        audit.Append(new AuditEvent
        {
            Action = issuerChanged ? "OperatorIssuerChangeDetected" : created ? "OperatorFirstSignIn" : "OperatorSignIn",
            Result = issuerChanged ? "PendingIssuerMigration" : "Succeeded",
            TargetStableId = $"{issuer}|{subject}",
        }, actor);

        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    /// <summary>
    /// Returns why an operator identity may hold no roles, or null when roles may be resolved. Unknown, migrated,
    /// disabled and pending-migration identities get no roles.
    /// </summary>
    public async Task<string?> RoleBlockerAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        var status = await db.AppUsers.AsNoTracking()
            .Where(u => u.Issuer == issuer && u.Subject == subject)
            .Select(u => (AppUserStatus?)u.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return status switch
        {
            null => "Unknown operator identity.",
            AppUserStatus.Active => null,
            AppUserStatus.PendingIssuerMigration => "This identity uses a new issuer. It holds no roles until a Security Approver approves the issuer migration.",
            AppUserStatus.Migrated => "This identity was migrated to a new issuer.",
            _ => "This operator account is disabled in ILM.",
        };
    }

    /// <summary>
    /// True when two application users are the same person: identical, or joined by an applied issuer migration
    /// in either direction. Separation-of-duties checks use this, so an issuer change cannot be used to approve one's own request.
    /// </summary>
    public static async Task<bool> IsSamePersonAsync(IIlmDbContext db, Guid first, Guid second, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (first == second)
        {
            return true;
        }

        return await Reaches(first, second) || await Reaches(second, first);

        async Task<bool> Reaches(Guid from, Guid to)
        {
            Guid? current = from;
            for (var hop = 0; hop < 16 && current is { } id; hop++)
            {
                current = await db.AppUsers.AsNoTracking().Where(u => u.Id == id).Select(u => u.MigratedToUserId).FirstOrDefaultAsync(cancellationToken);
                if (current == to)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Treats a change of issuer as an identity migration: the old and new (issuer, subject) pairs are linked
/// only through a dual-controlled, audited migration, never by configuration edit or email match.
/// </summary>
public sealed class IssuerMigrationService(IIlmDbContext db, ApprovalService approvals, IAuditWriter audit, TimeProvider time)
{
    public async Task<IssuerMigration> ProposeAsync(Guid fromUserId, Guid toUserId, string reason, ActorContext actor, long configurationVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var from = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == fromUserId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Source operator not found.");
        var to = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == toUserId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Target operator not found.");
        if (string.Equals(from.Issuer, to.Issuer, StringComparison.Ordinal))
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "An issuer migration must move between different issuers.");
        }

        var migration = new IssuerMigration
        {
            FromUserId = from.Id,
            ToUserId = to.Id,
            Reason = reason,
            ProposedByUserId = actor.AppUserId ?? Guid.Empty,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.IssuerMigrations.Add(migration);
        var approval = approvals.Create(
            ApprovalSubjectType.IssuerMigration,
            migration.Id.ToString("D"),
            $"Migrate operator {from.Issuer}|{from.Subject} to {to.Issuer}|{to.Subject}",
            AppRole.SecurityApprover,
            actor,
            ContentHash(from, to),
            TimeSpan.FromHours(72),
            configurationVersion);
        migration.ApprovalId = approval.Id;
        await db.SaveChangesAsync(cancellationToken);
        return migration;
    }

    public async Task ApplyAsync(Guid migrationId, ActorContext actor, CancellationToken cancellationToken)
    {
        var migration = await db.IssuerMigrations.FirstOrDefaultAsync(m => m.Id == migrationId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Migration not found.");
        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == migration.ApprovalId, cancellationToken);
        if (approval?.Status != ApprovalStatus.Approved)
        {
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "The issuer migration has not been approved.");
        }

        var from = await db.AppUsers.FirstAsync(u => u.Id == migration.FromUserId, cancellationToken);
        var to = await db.AppUsers.FirstAsync(u => u.Id == migration.ToUserId, cancellationToken);
        if (!string.Equals(approval.ContentHash, ContentHash(from, to), StringComparison.Ordinal))
        {
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "The identities changed after approval.");
        }

        from.Status = AppUserStatus.Migrated;
        from.MigratedToUserId = to.Id;
        if (to.Status == AppUserStatus.PendingIssuerMigration)
        {
            to.Status = AppUserStatus.Active;
        }

        migration.Applied = true;
        migration.AppliedUtc = time.GetUtcNow().UtcDateTime;
        audit.Append(new AuditEvent
        {
            Action = "OperatorIssuerMigrated",
            Result = "Succeeded",
            OperationId = migration.Id,
            BeforeValues = new { from.Issuer, from.Subject },
            AppliedValues = new { to.Issuer, to.Subject },
            Approval = $"{approval.Id}:{approval.Status}",
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ContentHash(AppUser from, AppUser to) =>
        Hashing.Sha256Hex($"{from.Id}|{from.Issuer}|{from.Subject}|{to.Id}|{to.Issuer}|{to.Subject}");
}
