using Ilm.Application.Configuration;
using Ilm.Domain.Authority;
using Ilm.Domain.Identity;

namespace Ilm.Application.Authority;

/// <summary>Resolves attribute-set ownership for an identity using the active configuration.</summary>
public sealed class AuthorityService(IActiveConfigurationProvider configuration, TimeProvider time)
{
    public async Task<AuthorityDecision> ResolveAsync(ExternalIdentity identity, LifecycleAction action, AttributeSet set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var config = await configuration.GetAsync(cancellationToken);
        return AuthorityResolver.Resolve(config.AuthorityRules, ContextFor(identity, action, set));
    }

    public async Task<IReadOnlyList<AuthorityDecision>> ResolveAllAsync(ExternalIdentity identity, LifecycleAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var config = await configuration.GetAsync(cancellationToken);
        return Enum.GetValues<AttributeSet>()
            .Select(set => AuthorityResolver.Resolve(config.AuthorityRules, ContextFor(identity, action, set)))
            .ToList();
    }

    private AuthorityContext ContextFor(ExternalIdentity identity, LifecycleAction action, AttributeSet set) => new(
        identity.Population,
        identity.BusinessEntity,
        identity.MigrationWave,
        identity.AccountType,
        action,
        set,
        time.GetUtcNow().UtcDateTime);
}
