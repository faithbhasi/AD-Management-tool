using System.Security.Claims;
using Ilm.Application.Security;
using Microsoft.AspNetCore.Authentication;

namespace Ilm.Web.Authentication;

/// <summary>
/// Adds ILM roles resolved server-side (briefly cached). Role claims arriving from any other source are
/// removed first, so a token can never grant a privileged role. Identities that are not active in ILM
/// (unknown, disabled, migrated, or awaiting an issuer migration) get no roles.
/// </summary>
public sealed class IlmClaimsTransformation(IPrivilegedRoleResolver resolver, AppUserService users) : IClaimsTransformation
{
    public const string RoleIdentityType = "ilm-roles";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true || principal.Identities.Any(i => i.AuthenticationType == RoleIdentityType))
        {
            return principal;
        }

        var subject = principal.FindFirstValue(IlmClaimTypes.Subject);
        var issuer = principal.FindFirstValue(IlmClaimTypes.Issuer);
        if (subject is null || issuer is null)
        {
            return principal;
        }

        var clean = new ClaimsPrincipal();
        foreach (var identity in principal.Identities)
        {
            var copy = new ClaimsIdentity(
                identity.Claims.Where(c => c.Type != IlmClaimTypes.Role && !c.Type.StartsWith(IlmClaimTypes.ScopePrefix, StringComparison.Ordinal)
                    && c.Type is not ("groups" or "role" or "roles" or ClaimTypes.Role)),
                identity.AuthenticationType,
                identity.NameClaimType,
                identity.RoleClaimType);
            clean.AddIdentity(copy);
        }

        var blocker = await users.RoleBlockerAsync(issuer, subject, CancellationToken.None);
        var resolution = blocker is null
            ? await resolver.ResolveAsync(new OperatorIdentity(issuer, subject), bypassCache: false, CancellationToken.None)
            : RoleResolution.Failed("operator-status", blocker, DateTime.UtcNow);
        var roles = new ClaimsIdentity(RoleIdentityType, IlmClaimTypes.Name, IlmClaimTypes.Role);
        roles.AddClaim(new Claim(IlmClaimTypes.RoleSource, resolution.Source));
        if (!resolution.Succeeded)
        {
            roles.AddClaim(new Claim(IlmClaimTypes.RoleResolutionFailed, resolution.FailureReason ?? "unknown"));
        }

        foreach (var (role, scopes) in resolution.Roles)
        {
            roles.AddClaim(new Claim(IlmClaimTypes.Role, role.ToString()));
            foreach (var scope in scopes)
            {
                roles.AddClaim(new Claim(IlmClaimTypes.ScopePrefix + role, scope));
            }
        }

        clean.AddIdentity(roles);
        return clean;
    }
}
