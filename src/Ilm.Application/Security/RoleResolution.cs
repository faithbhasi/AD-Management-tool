using Ilm.Domain.Security;

namespace Ilm.Application.Security;

public sealed record OperatorIdentity(string Issuer, string Subject);

public sealed record RoleResolution(
    bool Succeeded,
    IReadOnlyDictionary<AppRole, IReadOnlyCollection<string>> Roles,
    string Source,
    string? FailureReason,
    DateTime ResolvedUtc,
    bool FromCache)
{
    public static RoleResolution Failed(string source, string reason, DateTime nowUtc) =>
        new(false, new Dictionary<AppRole, IReadOnlyCollection<string>>(), source, reason, nowUtc, false);
}

/// <summary>
/// Resolves an operator's roles server-side from immutable identifiers. Token group claims are never used.
/// If verification cannot complete, the operator receives no roles.
/// </summary>
public interface IPrivilegedRoleResolver
{
    Task<RoleResolution> ResolveAsync(OperatorIdentity identity, bool bypassCache, CancellationToken cancellationToken);
}
