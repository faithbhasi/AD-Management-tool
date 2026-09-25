using System.Security.Claims;
using Ilm.Application.Abstractions;
using Ilm.Application.Security;
using Ilm.Domain.Security;

namespace Ilm.Web.Authentication;

/// <summary>Builds the <see cref="ActorContext"/> for the current operator.</summary>
public sealed class CurrentActorAccessor(IHttpContextAccessor accessor, IPrivilegedRoleResolver resolver, AppUserService users)
{
    /// <summary>Roles from the request's (briefly cached) resolution. For views.</summary>
    public ActorContext Current()
    {
        var user = accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context.");
        return Build(user, RolesFromClaims(user), fresh: false);
    }

    /// <summary>Roles re-resolved without cache. Required for every privileged commit (approve, execute, activate).</summary>
    public async Task<ActorContext> FreshAsync(CancellationToken cancellationToken)
    {
        var user = accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context.");
        var subject = user.FindFirstValue(IlmClaimTypes.Subject);
        var issuer = user.FindFirstValue(IlmClaimTypes.Issuer);
        if (subject is null || issuer is null)
        {
            return Build(user, new Dictionary<AppRole, IReadOnlyCollection<string>>(), fresh: true);
        }

        if (await users.RoleBlockerAsync(issuer, subject, cancellationToken) is not null)
        {
            return Build(user, new Dictionary<AppRole, IReadOnlyCollection<string>>(), fresh: false);
        }

        var resolution = await resolver.ResolveAsync(new OperatorIdentity(issuer, subject), bypassCache: true, cancellationToken);
        return Build(user, resolution.Succeeded ? resolution.Roles : new Dictionary<AppRole, IReadOnlyCollection<string>>(), fresh: resolution.Succeeded);
    }

    public static IReadOnlyDictionary<AppRole, IReadOnlyCollection<string>> RolesFromClaims(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var result = new Dictionary<AppRole, IReadOnlyCollection<string>>();
        foreach (var claim in user.FindAll(IlmClaimTypes.Role))
        {
            if (Enum.TryParse<AppRole>(claim.Value, out var role))
            {
                result[role] = user.FindAll(IlmClaimTypes.ScopePrefix + role).Select(c => c.Value).ToList();
            }
        }

        return result;
    }

    private ActorContext Build(ClaimsPrincipal user, IReadOnlyDictionary<AppRole, IReadOnlyCollection<string>> roles, bool fresh) => new()
    {
        AppUserId = Guid.TryParse(user.FindFirstValue(IlmClaimTypes.AppUserId), out var id) ? id : null,
        Issuer = user.FindFirstValue(IlmClaimTypes.Issuer) ?? string.Empty,
        Subject = user.FindFirstValue(IlmClaimTypes.Subject) ?? string.Empty,
        DisplayName = user.FindFirstValue(IlmClaimTypes.Name) ?? string.Empty,
        Roles = roles,
        RolesFreshlyResolved = fresh,
        CorrelationId = Guid.TryParse(accessor.HttpContext?.TraceIdentifier, out var c) ? c : Guid.NewGuid(),
    };
}
