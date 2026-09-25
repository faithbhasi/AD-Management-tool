using Ilm.Application.Abstractions;
using Ilm.Application.Configuration;
using Ilm.Domain.Security;

namespace Ilm.Application.Directory;

public sealed record ScopeDecision(bool InScope, string Detail, string? MatchedScopeId)
{
    public string ToAuditString() => InScope ? $"InScope:{MatchedScopeId}" : $"OutOfScope:{Detail}";
}

public sealed record ResolvedScope(string ScopeId, string ConnectorId, Guid OuGuid, string? DistinguishedName, string DisplayName);

/// <summary>
/// OU scopes are stored by objectGUID and resolved to a DN at request time, so renames and moves
/// never silently widen or break a scope. An unresolvable scope is inactive.
/// </summary>
public sealed class ScopeEvaluator(IDirectoryConnectorRegistry registry, IActiveConfigurationProvider configuration)
{
    public async Task<IReadOnlyList<ResolvedScope>> ResolveScopesAsync(IEnumerable<string> scopeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeIds);
        var config = await configuration.GetAsync(cancellationToken);
        var result = new List<ResolvedScope>();
        foreach (var scopeId in scopeIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var scope = config.FindScope(scopeId);
            if (scope is null || !Guid.TryParse(scope.OuObjectGuid, out var guid))
            {
                continue;
            }

            string? dn = null;
            try
            {
                var ou = await registry.GetReader(scope.ConnectorId).GetByGuidAsync(guid, null, cancellationToken);
                dn = ou?.DistinguishedName;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                dn = null;
            }

            result.Add(new ResolvedScope(scope.Id, scope.ConnectorId, guid, dn, scope.DisplayName));
        }

        return result;
    }

    public async Task<ScopeDecision> EvaluateAsync(ActorContext actor, AppRole role, string connectorId, string objectDistinguishedName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRole(role))
        {
            return new ScopeDecision(false, $"Actor does not hold {role}.", null);
        }

        var scopes = await ResolveScopesAsync(actor.ScopesFor(role), cancellationToken);
        foreach (var scope in scopes.Where(s => string.Equals(s.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase)))
        {
            if (scope.DistinguishedName is null)
            {
                continue;
            }

            if (DistinguishedNameHelper.IsWithin(objectDistinguishedName, scope.DistinguishedName))
            {
                return new ScopeDecision(true, $"Within {scope.DisplayName}.", scope.ScopeId);
            }
        }

        return new ScopeDecision(false, "The object is outside every OU scope held for this role.", null);
    }
}
