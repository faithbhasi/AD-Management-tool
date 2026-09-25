using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Domain.Directory;
using Ilm.Infrastructure.Directory.Ldap;
using Ilm.Infrastructure.Directory.Mock;
using Microsoft.Extensions.Logging;

namespace Ilm.Infrastructure.Directory;

/// <summary>
/// Builds connectors from the active configuration. "Mock" connectors read the fictional in-memory
/// directory; "Ldap" connectors use System.DirectoryServices.Protocols with the process identity.
/// </summary>
public sealed class DirectoryConnectorRegistry(
    IActiveConfigurationProvider configuration,
    InMemoryDirectoryStore mockStore,
    LdapConnectionFactory ldapConnections,
    ILoggerFactory loggers) : IDirectoryConnectorRegistry
{
    private readonly Dictionary<string, object> cache = new(StringComparer.OrdinalIgnoreCase);
    private ActiveConfiguration? snapshot;

    private ActiveConfiguration Snapshot => snapshot ??= configuration.GetAsync(CancellationToken.None).GetAwaiter().GetResult();

    public IReadOnlyList<string> ConnectorIds => Snapshot.Document.Connectors.Select(c => c.Id).ToList();

    public IDirectoryReader GetReader(string connectorId) => (IDirectoryReader)Get(connectorId);

    public IDirectoryWriteChannel GetWriteChannel(string connectorId) => (IDirectoryWriteChannel)Get(connectorId);

    public ForestRole RoleOf(string connectorId) =>
        (Snapshot.FindConnector(connectorId) ?? throw new KeyNotFoundException($"Connector '{connectorId}' is not configured.")).Role;

    private object Get(string connectorId)
    {
        if (cache.TryGetValue(connectorId, out var existing))
        {
            return existing;
        }

        var definition = Snapshot.FindConnector(connectorId) ?? throw new KeyNotFoundException($"Connector '{connectorId}' is not configured.");
        object connector = definition.Implementation switch
        {
            "Mock" => new MockDirectoryConnector(mockStore, definition),
            "Ldap" => new LdapDirectoryConnector(definition, ldapConnections, loggers.CreateLogger<LdapDirectoryConnector>()),
            _ => throw new InvalidOperationException($"Unknown connector implementation '{definition.Implementation}'."),
        };
        cache[connectorId] = connector;
        return connector;
    }
}
