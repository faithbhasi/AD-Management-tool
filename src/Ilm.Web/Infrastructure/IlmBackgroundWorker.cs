using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Reconciliation;
using Ilm.Modules.Leaver;

namespace Ilm.Web.Infrastructure;

public sealed class WorkerOptions
{
    public const string SectionName = "Ilm:Workers";

    public bool Enabled { get; set; } = true;

    public int LeaverIntervalSeconds { get; set; } = 30;

    public int AuditForwardingIntervalSeconds { get; set; } = 15;

    public int ReconciliationIntervalMinutes { get; set; } = 60;
}

/// <summary>Durable background processing: scheduled containment, re-verification, SLA escalation, audit forwarding, reconciliation.</summary>
public sealed partial class IlmBackgroundWorker(IServiceScopeFactory scopes, WorkerOptions options, TimeProvider time, ILogger<IlmBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        var nextLeaver = DateTime.MinValue;
        var nextAudit = DateTime.MinValue;
        var nextReconcile = time.GetUtcNow().UtcDateTime.AddMinutes(1);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (now >= nextAudit)
            {
                await RunAsync("audit-forwarding", async sp => IlmTelemetry.AuditForwarded.Add(await sp.GetRequiredService<AuditForwardingService>().ForwardPendingAsync(stoppingToken)), stoppingToken);
                nextAudit = now.AddSeconds(options.AuditForwardingIntervalSeconds);
            }

            if (now >= nextLeaver)
            {
                await RunAsync("leaver-maintenance", sp => sp.GetRequiredService<LeaverMaintenanceService>().RunOnceAsync(stoppingToken), stoppingToken);
                nextLeaver = now.AddSeconds(options.LeaverIntervalSeconds);
            }

            if (now >= nextReconcile)
            {
                await RunAsync("reconciliation", sp => sp.GetRequiredService<ReconciliationService>().RunAsync("scheduled", ActorContext.System("reconciliation-worker"), stoppingToken), stoppingToken);
                nextReconcile = now.AddMinutes(options.ReconciliationIntervalMinutes);
            }
        }
    }

    private async Task RunAsync(string name, Func<IServiceProvider, Task> work, CancellationToken stoppingToken)
    {
        using var activity = IlmTelemetry.Activities.StartActivity("worker." + name);
        try
        {
            using var scope = scopes.CreateScope();
            await work(scope.ServiceProvider);
            IlmTelemetry.WorkerRuns.Add(1, new KeyValuePair<string, object?>("worker", name), new KeyValuePair<string, object?>("outcome", "ok"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            IlmTelemetry.WorkerRuns.Add(1, new KeyValuePair<string, object?>("worker", name), new KeyValuePair<string, object?>("outcome", "error"));
            LogWorkerFailed(logger, name, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background worker {Worker} failed: {ErrorType}")]
    private static partial void LogWorkerFailed(ILogger logger, string worker, string errorType);
}
