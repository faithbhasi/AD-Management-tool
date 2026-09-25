namespace Ilm.Application.Health;

public sealed record ComponentHealth(string Component, bool Healthy, bool IsMock, string Detail);

/// <summary>Reports the health of an external dependency (directory, Okta, session systems, audit sinks).</summary>
public interface IConnectorHealthSource
{
    Task<IReadOnlyList<ComponentHealth>> CheckAsync(CancellationToken cancellationToken);
}
