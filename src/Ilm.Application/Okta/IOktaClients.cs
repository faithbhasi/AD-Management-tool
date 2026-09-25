namespace Ilm.Application.Okta;

/// <summary>Okta Users API: create and search. Supported APIs only; OAuth 2.0 service-app authentication.</summary>
public interface IOktaUserClient
{
    /// <summary>Creates a user; with <paramref name="activate"/> false the user is STAGED.</summary>
    Task<OktaResult<OktaUser>> CreateUserAsync(OktaCreateUserRequest request, bool activate, CancellationToken cancellationToken);

    Task<OktaResult<IReadOnlyList<OktaUser>>> SearchUsersAsync(OktaUserSearch search, CancellationToken cancellationToken);

    Task<OktaResult<OktaUser>> GetUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Partial profile update (POST /api/v1/users/{id}); used by the feasibility harness to trigger a push.</summary>
    Task<OktaResult<OktaUser>> UpdateProfileAsync(string userId, IReadOnlyDictionary<string, string> profile, CancellationToken cancellationToken);

    Task<OktaResult<IReadOnlyList<string>>> GetFactorTypesAsync(string userId, CancellationToken cancellationToken);

    Task<OktaResult<IReadOnlyList<string>>> GetAssignedApplicationIdsAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// Clean-up for synthetic feasibility users only. Implementations must refuse any login that does not carry
/// the configured synthetic prefix. Never used by lifecycle workflows.
/// </summary>
public interface IOktaFeasibilityCleanup
{
    Task<OktaResult> DeleteSyntheticUserAsync(string userId, string requiredLoginPrefix, CancellationToken cancellationToken);
}

/// <summary>
/// Directory probe for the feasibility harness: writes one attribute on a synthetic test user to detect
/// whether the next Okta push overwrites ILM changes. Refuses non-synthetic objects.
/// </summary>
public interface IFeasibilityDirectoryProbe
{
    Task<bool> SetProbeAttributeAsync(string connectorId, Guid objectGuid, string requiredSamPrefix, string attribute, string value, CancellationToken cancellationToken);
}

/// <summary>Environment controls that only a mock can offer (for example, simulating an Okta AD Agent outage).</summary>
public interface IFeasibilityEnvironmentControl
{
    bool CanSimulateAgentOutage { get; }

    void SetAgentAvailable(bool available);
}

public interface IOktaLifecycleClient
{
    Task<OktaResult> ActivateAsync(string userId, CancellationToken cancellationToken);

    Task<OktaResult> SuspendAsync(string userId, CancellationToken cancellationToken);

    Task<OktaResult> UnsuspendAsync(string userId, CancellationToken cancellationToken);

    Task<OktaResult> DeactivateAsync(string userId, CancellationToken cancellationToken);

    Task<OktaResult<string>> GetLifecycleStateAsync(string userId, CancellationToken cancellationToken);
}

public interface IOktaSessionClient
{
    /// <summary>Clears the user's Okta sessions; optionally revokes OAuth tokens issued to the user.</summary>
    Task<OktaResult> RevokeSessionsAsync(string userId, bool revokeOAuthTokens, CancellationToken cancellationToken);
}

public interface IOktaGroupClient
{
    Task<OktaResult> AssignUserToGroupAsync(string groupId, string userId, CancellationToken cancellationToken);

    Task<OktaResult> RemoveUserFromGroupAsync(string groupId, string userId, CancellationToken cancellationToken);

    /// <summary>Immutable group IDs of the user's groups, read server-side.</summary>
    Task<OktaResult<IReadOnlyList<string>>> GetUserGroupIdsAsync(string userId, CancellationToken cancellationToken);
}

public interface IOktaApplicationClient
{
    Task<OktaResult> AssignUserToApplicationAsync(string appId, string userId, CancellationToken cancellationToken);

    /// <summary>Role values from the user's assignment to the given application, or an empty list when unassigned.</summary>
    Task<OktaResult<IReadOnlyList<string>>> GetAssignmentRolesAsync(string appId, string userId, CancellationToken cancellationToken);
}

public interface IOktaSystemLogClient
{
    Task<OktaResult<IReadOnlyList<OktaLogEvent>>> QueryAsync(DateTime sinceUtc, string? targetUserId, string? eventTypePrefix, CancellationToken cancellationToken);
}

/// <summary>Reads the AD object that Okta provisioning produced, for reconciliation after an Okta-driven change.</summary>
public interface IAdTargetStateReconciler
{
    Task<AdTargetState> ReadAsync(string connectorId, AdTargetLookup lookup, CancellationToken cancellationToken);
}

public sealed record AdTargetLookup(string? SamAccountName, string? UserPrincipalName, string? Mail, Guid? ObjectGuid);

public sealed record AdTargetState(
    bool Found,
    int MatchCount,
    Guid? ObjectGuid,
    string? DistinguishedName,
    string? ParentOuDistinguishedName,
    string? SamAccountName,
    string? UserPrincipalName,
    string? Mail,
    IReadOnlyList<string> ProxyAddresses,
    string? Department,
    string? ManagerDistinguishedName,
    bool? Enabled,
    IReadOnlyList<string> GroupSids,
    string? Detail);
