using Ilm.Application.Configuration;
using Ilm.Application.Okta;
using Ilm.Application.Security;
using Ilm.Domain.Configuration;
using Ilm.Domain.Security;
using Microsoft.Extensions.Caching.Memory;

namespace Ilm.Infrastructure.Security;

/// <summary>
/// Resolves roles server-side: immutable Okta group IDs (or app-assignment role values) mapped through the
/// approved configuration. Token claims are never consulted. The cache is capped at five minutes and is
/// bypassed for privileged commits. Any failure yields no roles.
/// </summary>
public sealed class OktaPrivilegedRoleResolver(
    IOktaGroupClient groups,
    IOktaApplicationClient applications,
    IActiveConfigurationProvider configuration,
    IMemoryCache cache,
    TimeProvider time) : IPrivilegedRoleResolver
{
    public async Task<RoleResolution> ResolveAsync(OperatorIdentity identity, bool bypassCache, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = time.GetUtcNow().UtcDateTime;
        var key = $"ilm-roles:{identity.Issuer}|{identity.Subject}";
        if (!bypassCache && cache.TryGetValue<RoleResolution>(key, out var cached) && cached is not null)
        {
            return cached with { FromCache = true };
        }

        ActiveConfiguration config;
        try
        {
            config = await configuration.GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RoleResolution.Failed("configuration", "Active configuration unavailable.", now);
        }

        var settings = config.Document.RoleResolution;
        IReadOnlyList<string> values;
        string source;
        if (settings.Mode == RoleResolutionMode.OktaAppAssignment)
        {
            var result = await applications.GetAssignmentRolesAsync(settings.OktaAppId!, identity.Subject, cancellationToken);
            if (!result.Succeeded)
            {
                return RoleResolution.Failed("okta-app-assignment", $"Assignment lookup failed ({result.ErrorCategory}).", now);
            }

            values = result.Value!;
            source = "okta-app-assignment";
        }
        else
        {
            var result = await groups.GetUserGroupIdsAsync(identity.Subject, cancellationToken);
            if (!result.Succeeded)
            {
                return RoleResolution.Failed("okta-group-membership", $"Group membership lookup failed ({result.ErrorCategory}).", now);
            }

            values = result.Value!;
            source = "okta-group-membership";
        }

        var roles = new Dictionary<AppRole, HashSet<string>>();
        foreach (var mapping in config.Document.RoleMappings)
        {
            var matched = settings.Mode == RoleResolutionMode.OktaAppAssignment
                ? mapping.AppAssignmentRole is not null && values.Contains(mapping.AppAssignmentRole, StringComparer.Ordinal)
                : mapping.OktaGroupId is not null && values.Contains(mapping.OktaGroupId, StringComparer.Ordinal);
            if (!matched)
            {
                continue;
            }

            if (!roles.TryGetValue(mapping.Role, out var scopes))
            {
                roles[mapping.Role] = scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            scopes.UnionWith(mapping.ScopeIds);
        }

        var resolution = new RoleResolution(
            true,
            roles.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<string>)kv.Value.ToList()),
            source,
            null,
            now,
            false);
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(settings.CacheSeconds, 0, ConfigurationValidator.MaxRoleCacheSeconds));
        if (lifetime > TimeSpan.Zero)
        {
            cache.Set(key, resolution, lifetime);
        }

        return resolution;
    }
}
