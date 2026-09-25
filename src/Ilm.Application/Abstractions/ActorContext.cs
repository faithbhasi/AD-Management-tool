using Ilm.Domain.Security;

namespace Ilm.Application.Abstractions;

/// <summary>
/// Who is performing an operation, with roles resolved server-side. Passed explicitly to every service
/// so that no operation depends on ambient state.
/// </summary>
public sealed record ActorContext
{
    public Guid? AppUserId { get; init; }

    public string Issuer { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyDictionary<AppRole, IReadOnlyCollection<string>> Roles { get; init; } =
        new Dictionary<AppRole, IReadOnlyCollection<string>>();

    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    /// <summary>True when the roles were resolved without the cache (required for privileged commits).</summary>
    public bool RolesFreshlyResolved { get; init; }

    public bool IsSystem { get; init; }

    public string Label => IsSystem ? $"system:{DisplayName}" : $"{DisplayName} ({Issuer}|{Subject})";

    public bool HasRole(AppRole role) => Roles.ContainsKey(role);

    public IReadOnlyCollection<string> ScopesFor(AppRole role) =>
        Roles.TryGetValue(role, out var scopes) ? scopes : [];

    public string EffectiveRolesText => string.Join(",", Roles.Keys.OrderBy(r => r.ToString(), StringComparer.Ordinal));

    public static ActorContext System(string name) => new()
    {
        Issuer = "urn:ilm:system",
        Subject = name,
        DisplayName = name,
        IsSystem = true,
        RolesFreshlyResolved = true,
    };
}
