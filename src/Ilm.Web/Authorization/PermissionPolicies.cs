using Ilm.Application.Security;
using Ilm.Web.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Ilm.Web.Authorization;

public static class PermissionPolicies
{
    public static string Name(Permission permission) => "perm:" + permission;

    public static IServiceCollection AddIlmAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddAuthorization(options =>
        {
            foreach (var permission in Enum.GetValues<Permission>())
            {
                var roles = Permissions.RolesFor(permission).Select(r => r.ToString()).ToHashSet(StringComparer.Ordinal);
                options.AddPolicy(Name(permission), p => p.RequireAuthenticatedUser()
                    .RequireAssertion(ctx => ctx.User.FindAll(IlmClaimTypes.Role).Any(c => roles.Contains(c.Value))));
            }
        });
        return services;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequirePermissionAttribute(Permission permission) : AuthorizeAttribute(PermissionPolicies.Name(permission))
{
    public Permission Permission { get; } = permission;
}
