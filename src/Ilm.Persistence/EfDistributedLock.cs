using Ilm.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Persistence;

/// <summary>
/// Lease-based distributed lock stored in the database. Uses parameterised raw statements so it never
/// flushes unrelated pending changes in the caller's unit of work.
/// </summary>
public sealed class EfDistributedLock(IlmDbContext db, TimeProvider time) : IDistributedLock
{
    private const string SqliteDeleteExpired = "DELETE FROM \"LockLeases\" WHERE \"LockKey\" = {0} AND \"ExpiresUtc\" < {1}";
    private const string SqliteInsert = "INSERT INTO \"LockLeases\" (\"LockKey\", \"Owner\", \"AcquiredUtc\", \"ExpiresUtc\") VALUES ({0}, {1}, {2}, {3}) ON CONFLICT (\"LockKey\") DO NOTHING";
    private const string SqliteRelease = "DELETE FROM \"LockLeases\" WHERE \"LockKey\" = {0} AND \"Owner\" = {1}";
    private const string PgDeleteExpired = "DELETE FROM \"ilm\".\"LockLeases\" WHERE \"LockKey\" = {0} AND \"ExpiresUtc\" < {1}";
    private const string PgInsert = "INSERT INTO \"ilm\".\"LockLeases\" (\"LockKey\", \"Owner\", \"AcquiredUtc\", \"ExpiresUtc\") VALUES ({0}, {1}, {2}, {3}) ON CONFLICT (\"LockKey\") DO NOTHING";
    private const string PgRelease = "DELETE FROM \"ilm\".\"LockLeases\" WHERE \"LockKey\" = {0} AND \"Owner\" = {1}";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var postgres = db is PostgresIlmDbContext;
        var owner = Guid.NewGuid().ToString("N");
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.Add(leaseDuration);

        // Take over an expired lease, if one exists.
        await db.Database.ExecuteSqlRawAsync(postgres ? PgDeleteExpired : SqliteDeleteExpired, [key, now], cancellationToken);
        var inserted = await db.Database.ExecuteSqlRawAsync(postgres ? PgInsert : SqliteInsert, [key, owner, now, expires], cancellationToken);
        return inserted == 1 ? new Handle(db, postgres ? PgRelease : SqliteRelease, key, owner) : null;
    }

    private sealed class Handle(IlmDbContext db, string releaseSql, string key, string owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await db.Database.ExecuteSqlRawAsync(releaseSql, [key, owner]);
    }
}
