namespace Ilm.Domain.Security;

/// <summary>A database-backed lease used as an application distributed lock.</summary>
public sealed class DistributedLockLease
{
    public string LockKey { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public DateTime AcquiredUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }
}
