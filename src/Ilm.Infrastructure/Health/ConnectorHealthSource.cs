using Ilm.Application.Audit;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Health;
using Ilm.Application.Okta;
using Ilm.Application.Sessions;
using Ilm.Domain.Directory;
using Ilm.Infrastructure.Okta.Http;
using Ilm.Infrastructure.Sessions;
using Microsoft.Extensions.Options;

namespace Ilm.Infrastructure.Health;

/// <summary>Collects connector health for the dashboard and the readiness endpoint.</summary>
public sealed class ConnectorHealthSource(
    IDirectoryConnectorRegistry registry,
    IActiveConfigurationProvider configuration,
    IOktaUserClient okta,
    IEnumerable<ISessionConnector> sessions,
    AuditForwardingService forwarding,
    IOptions<OktaOptions> oktaOptions,
    IOptions<Audit.AuditOptions> auditOptions) : IConnectorHealthSource
{
    public async Task<IReadOnlyList<ComponentHealth>> CheckAsync(CancellationToken cancellationToken)
    {
        var results = new List<ComponentHealth>();
        var config = await configuration.GetAsync(cancellationToken);
        foreach (var definition in config.Document.Connectors)
        {
            if (definition.Mode == ConnectorMode.Disabled)
            {
                results.Add(new ComponentHealth($"directory:{definition.Id}", true, definition.Implementation == "Mock", "Disabled by configuration."));
                continue;
            }

            try
            {
                var report = await registry.GetReader(definition.Id).CheckHealthAsync(cancellationToken);
                results.Add(new ComponentHealth($"directory:{definition.Id}", report.Healthy, definition.Implementation == "Mock", $"{definition.Mode}; {report.Detail}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new ComponentHealth($"directory:{definition.Id}", false, definition.Implementation == "Mock", $"Unavailable ({ex.GetType().Name})."));
            }
        }

        var probe = await okta.SearchUsersAsync(new OktaUserSearch(null, "health-probe@example.test", null), cancellationToken);
        results.Add(new ComponentHealth("okta:management-api", probe.Succeeded, oktaOptions.Value.Mode == OktaMode.Mock, probe.Succeeded ? "Reachable." : $"Unavailable ({probe.ErrorCategory})."));

        foreach (var s in sessions)
        {
            var notConfigured = s is NotConfiguredSessionConnector;
            results.Add(new ComponentHealth($"sessions:{s.System}", !notConfigured, s is MockSessionConnector, notConfigured ? "Not integrated: containment falls back to a manual task." : s.Coverage));
        }

        var lag = await forwarding.GetLagAsync(cancellationToken);
        results.Add(new ComponentHealth("audit:forwarding", lag <= auditOptions.Value.MaxForwardingLag, false, $"{lag} record(s) awaiting off-box forwarding."));
        return results;
    }
}
