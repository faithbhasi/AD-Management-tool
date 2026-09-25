using Ilm.Domain.Authority;

namespace Ilm.Application.Provisioning;

/// <summary>Resolves strategies by kind. Disabled strategies are reported as such, never silently substituted.</summary>
public sealed class StrategyRegistry(IEnumerable<IIdentityProvisioningStrategy> strategies)
{
    private readonly Dictionary<StrategyKind, IIdentityProvisioningStrategy> byKind = strategies.ToDictionary(s => s.Kind);

    public IIdentityProvisioningStrategy Get(StrategyKind kind) =>
        byKind.TryGetValue(kind, out var strategy) ? strategy : throw new InvalidOperationException($"Strategy {kind} is not registered.");

    public IReadOnlyCollection<IIdentityProvisioningStrategy> All => byKind.Values;

    public async Task<IReadOnlyDictionary<StrategyKind, bool>> GetEnabledStatesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<StrategyKind, bool>();
        foreach (var strategy in byKind.Values)
        {
            result[strategy.Kind] = await strategy.IsEnabledAsync(cancellationToken);
        }

        return result;
    }
}
