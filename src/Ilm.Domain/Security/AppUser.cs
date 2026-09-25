namespace Ilm.Domain.Security;

/// <summary>
/// An operator of the portal, keyed only by (Issuer, Subject). Email, UPN, username, display name and
/// group names are informational and never used to find or merge users.
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Issuer { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? LastSeenEmail { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime LastSignInUtc { get; set; }

    public AppUserStatus Status { get; set; } = AppUserStatus.Active;

    public Guid? MigratedToUserId { get; set; }

    public string Label => $"{DisplayName} ({Subject})";
}

public enum AppUserStatus
{
    Active = 0,
    Migrated,
    Disabled,

    /// <summary>
    /// Signed in under a new issuer while the same subject is active under another issuer. Holds no roles until a
    /// Security Approver approves the issuer migration.
    /// </summary>
    PendingIssuerMigration,
}

/// <summary>A dual-controlled mapping from an old (issuer, subject) to a new one after an authorization-server change.</summary>
public sealed class IssuerMigration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromUserId { get; set; }

    public Guid ToUserId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid ProposedByUserId { get; set; }

    public Guid? ApprovalId { get; set; }

    public bool Applied { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? AppliedUtc { get; set; }
}
