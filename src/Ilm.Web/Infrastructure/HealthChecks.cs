using Ilm.Application.Abstractions;
using Ilm.Application.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ilm.Web.Infrastructure;

public sealed class DatabaseHealthCheck(IIlmDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var active = await db.ConfigurationVersions.AnyAsync(v => v.Status == Domain.Configuration.ConfigurationVersionStatus.Active, cancellationToken);
            return active ? HealthCheckResult.Healthy("Database reachable; active configuration present.") : HealthCheckResult.Degraded("No active configuration.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}

public sealed class ConnectorsHealthCheck(IConnectorHealthSource source) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var components = await source.CheckAsync(cancellationToken);
            var unhealthy = components.Where(c => !c.Healthy).Select(c => c.Component).ToList();
            var data = components.ToDictionary(c => c.Component, c => (object)(c.Healthy ? "ok" : "degraded"));
            return unhealthy.Count == 0
                ? HealthCheckResult.Healthy("All connectors healthy.", data)
                : HealthCheckResult.Degraded("Degraded: " + string.Join(", ", unhealthy), data: data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Connector health could not be evaluated.");
        }
    }
}
