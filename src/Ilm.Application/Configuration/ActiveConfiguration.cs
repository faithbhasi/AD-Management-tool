using Ilm.Domain.Authority;
using Ilm.Domain.Configuration;
using Ilm.Domain.Protection;

namespace Ilm.Application.Configuration;

/// <summary>The currently active, approved configuration, materialised for fast use.</summary>
public sealed class ActiveConfiguration
{
    public ActiveConfiguration(long version, IlmConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Version = version;
        Document = document;
        AuthorityRules = document.AuthorityRules.Select(r => r.ToRule(version)).ToList();
        ProtectionAdditions = document.ProtectionAdditions.Select(p => p.ToEntry()).ToList();
    }

    public long Version { get; }

    public IlmConfigurationDocument Document { get; }

    public IReadOnlyList<AuthorityRule> AuthorityRules { get; }

    public IReadOnlyList<ProtectedObjectEntry> ProtectionAdditions { get; }

    public bool IsEnabled(Feature feature) => FeatureDefaults.IsEnabled(Document.FeatureFlags, feature);

    public DirectoryConnectorDefinition? FindConnector(string connectorId) =>
        Document.Connectors.FirstOrDefault(c => string.Equals(c.Id, connectorId, StringComparison.OrdinalIgnoreCase));

    public ScopeDefinition? FindScope(string scopeId) =>
        Document.Scopes.FirstOrDefault(s => string.Equals(s.Id, scopeId, StringComparison.OrdinalIgnoreCase));
}

public interface IActiveConfigurationProvider
{
    Task<ActiveConfiguration> GetAsync(CancellationToken cancellationToken);

    void Invalidate();
}
