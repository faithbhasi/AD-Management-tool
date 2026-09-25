using Ilm.Application.Abstractions;
using Ilm.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ilm.Application.Audit;

/// <summary>Sends sealed audit records to a sink outside the database (SIEM, WEF, append-only file).</summary>
public interface IAuditForwarder
{
    string SinkName { get; }

    Task ForwardAsync(IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken);
}

/// <summary>A sink that can be read back, so the verifier can compare it with the database.</summary>
public interface IAuditSinkReader
{
    string SinkName { get; }

    Task<IReadOnlyDictionary<long, ForwardedAuditEntry>> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed partial class AuditForwardingService(
    IIlmDbContext db,
    IEnumerable<IAuditForwarder> forwarders,
    TimeProvider time,
    ILogger<AuditForwardingService> logger)
{
    public const int BatchSize = 500;

    public async Task<int> ForwardPendingAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var forwarder in forwarders)
        {
            var checkpoint = await db.AuditForwardingCheckpoints.FirstOrDefaultAsync(c => c.SinkName == forwarder.SinkName, cancellationToken);
            if (checkpoint is null)
            {
                checkpoint = new AuditForwardingCheckpoint { SinkName = forwarder.SinkName };
                db.AuditForwardingCheckpoints.Add(checkpoint);
            }

            var batch = await db.AuditRecords.AsNoTracking()
                .Where(r => r.Sequence > checkpoint.LastForwardedSequence)
                .OrderBy(r => r.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                continue;
            }

            try
            {
                await forwarder.ForwardAsync(batch, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
            {
                LogForwardFailed(logger, forwarder.SinkName, ex.GetType().Name);
                continue;
            }

            checkpoint.LastForwardedSequence = batch[^1].Sequence;
            checkpoint.LastForwardedHash = batch[^1].Hash;
            checkpoint.UpdatedUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            total += batch.Count;
        }

        return total;
    }

    /// <summary>Number of records not yet forwarded to the slowest sink.</summary>
    public async Task<long> GetLagAsync(CancellationToken cancellationToken)
    {
        var last = await db.AuditRecords.AsNoTracking().OrderByDescending(r => r.Sequence).Select(r => r.Sequence).FirstOrDefaultAsync(cancellationToken);
        long lag = 0;
        foreach (var forwarder in forwarders)
        {
            var checkpoint = await db.AuditForwardingCheckpoints.AsNoTracking().FirstOrDefaultAsync(c => c.SinkName == forwarder.SinkName, cancellationToken);
            lag = Math.Max(lag, last - (checkpoint?.LastForwardedSequence ?? 0));
        }

        return lag;
    }

    public const string LagAlertCategory = "AuditForwardingLag";

    /// <summary>
    /// Raises a High alert when more than <paramref name="maxLag"/> records have not reached the off-box sink.
    /// While an unacknowledged lag alert is open, no further lag alert is raised.
    /// </summary>
    public async Task<bool> AlertOnLagAsync(long maxLag, Tasks.AlertService alerts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        var lag = await GetLagAsync(cancellationToken);
        if (lag <= maxLag || await db.Alerts.AnyAsync(a => a.Category == LagAlertCategory && a.AcknowledgedUtc == null, cancellationToken))
        {
            return false;
        }

        alerts.Raise(Domain.Tasks.AlertSeverity.High, LagAlertCategory, $"{lag} audit record(s) have not reached the off-box sink (threshold {maxLag}).", null, ActorContext.System("audit-forwarding"));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit forwarding to {Sink} failed ({ErrorType}); will retry.")]
    private static partial void LogForwardFailed(ILogger logger, string sink, string errorType);
}
