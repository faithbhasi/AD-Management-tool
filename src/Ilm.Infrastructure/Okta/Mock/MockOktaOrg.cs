using System.Security.Cryptography;
using Ilm.Application.Okta;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Infrastructure.Directory.Mock;

namespace Ilm.Infrastructure.Okta.Mock;

/// <summary>Settings for the simulated Okta → AD provisioning integration.</summary>
public sealed class MockOktaAdIntegration
{
    public string TargetConnectorId { get; set; } = "corp";

    public HashSet<string> ProvisioningGroupIds { get; } = new(StringComparer.Ordinal);

    public bool AgentAvailable { get; set; } = true;

    public bool ProvisionStagedUsers { get; set; }

    public bool DeprovisionOnDeactivate { get; set; } = true;

    public bool DisableOnSuspend { get; set; }

    public bool ReactivateEnables { get; set; } = true;

    public bool PushProfileUpdates { get; set; } = true;

    public int PushDelayTicks { get; set; } = 1;

    public Dictionary<string, Guid> OuByDepartment { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Guid DefaultOuGuid { get; set; }
}

public sealed class MockOktaUser
{
    public required string Id { get; init; }

    public string Status { get; set; } = OktaUserStatus.Staged;

    public Dictionary<string, string?> Profile { get; } = new(StringComparer.Ordinal);

    public HashSet<string> GroupIds { get; } = new(StringComparer.Ordinal);

    public HashSet<string> AppIds { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<string>> AppRoles { get; } = new(StringComparer.Ordinal);

    public List<string> Factors { get; } = [];

    public int ActiveSessions { get; set; } = 1;

    public Guid? AdObjectGuid { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime StatusChangedUtc { get; set; } = DateTime.UtcNow;

    public string Login => Profile.TryGetValue("login", out var l) ? l ?? string.Empty : string.Empty;
}

/// <summary>
/// A fictional Okta org implementing every Okta port, plus a simulated Okta AD Agent that pushes changes
/// into the in-memory target forest after a configurable number of directory reads.
/// </summary>
public sealed class MockOktaOrg :
    IOktaUserClient, IOktaLifecycleClient, IOktaSessionClient, IOktaGroupClient, IOktaApplicationClient,
    IOktaSystemLogClient, IOktaFeasibilityCleanup, IFeasibilityEnvironmentControl
{
    public const string IlmActorId = "ilm-service-app";

    private readonly object gate = new();
    private readonly InMemoryDirectoryStore directory;
    private readonly List<PendingPush> pending = [];

    public MockOktaOrg(InMemoryDirectoryStore directory)
    {
        this.directory = directory;
        directory.RegisterReadHook(ProcessDuePushes);
    }

    public Dictionary<string, MockOktaUser> Users { get; } = new(StringComparer.Ordinal);

    public List<OktaLogEvent> SystemLog { get; } = [];

    public MockOktaAdIntegration AdIntegration { get; } = new();

    /// <summary>Fault injection: the Okta API returns 503.</summary>
    public bool ApiAvailable { get; set; } = true;

    public bool FailSessionRevocation { get; set; }

    public bool IsSimulated => true;

    public bool CanSimulateAgentOutage => true;

    public void SetAgentAvailable(bool available)
    {
        lock (gate)
        {
            AdIntegration.AgentAvailable = available;
        }
    }

    public MockOktaUser AddUser(string id, string login, string firstName, string lastName, string status, string? department = null)
    {
        lock (gate)
        {
            var user = new MockOktaUser { Id = id, Status = status };
            user.Profile["login"] = login;
            user.Profile["email"] = login;
            user.Profile["firstName"] = firstName;
            user.Profile["lastName"] = lastName;
            user.Profile["department"] = department;
            user.Factors.Add("okta_verify:push");
            Users[id] = user;
            return user;
        }
    }

    public Task<OktaResult<OktaUser>> CreateUserAsync(OktaCreateUserRequest request, bool activate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (!ApiAvailable)
            {
                return Task.FromResult(OktaResult<OktaUser>.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            if (Users.Values.Any(u => string.Equals(u.Login, request.Login, StringComparison.OrdinalIgnoreCase)))
            {
                Log("user.lifecycle.create", "FAILURE", null);
                return Task.FromResult(OktaResult<OktaUser>.Fail(400, SafeErrorCategory.DuplicateObject, "E0000001"));
            }

            var user = new MockOktaUser { Id = NewId("00u"), Status = activate ? OktaUserStatus.Active : OktaUserStatus.Staged };
            user.Profile["login"] = request.Login;
            user.Profile["email"] = request.Email;
            user.Profile["firstName"] = request.FirstName;
            user.Profile["lastName"] = request.LastName;
            user.Profile["department"] = request.Department;
            user.Profile["managerId"] = request.ManagerId;
            user.Profile["employeeNumber"] = request.EmployeeNumber;
            Users[user.Id] = user;
            Log("user.lifecycle.create", "SUCCESS", user.Id);
            if (activate)
            {
                Log("user.lifecycle.activate", "SUCCESS", user.Id);
            }

            return Task.FromResult(OktaResult<OktaUser>.Ok(Map(user)));
        }
    }

    public Task<OktaResult<IReadOnlyList<OktaUser>>> SearchUsersAsync(OktaUserSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        lock (gate)
        {
            if (!ApiAvailable)
            {
                return Task.FromResult(OktaResult<IReadOnlyList<OktaUser>>.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            IReadOnlyList<OktaUser> matches = Users.Values
                .Where(u => (search.Login is null || string.Equals(u.Login, search.Login, StringComparison.OrdinalIgnoreCase))
                    && (search.Email is null || string.Equals(u.Profile.GetValueOrDefault("email"), search.Email, StringComparison.OrdinalIgnoreCase))
                    && (search.EmployeeNumber is null || string.Equals(u.Profile.GetValueOrDefault("employeeNumber"), search.EmployeeNumber, StringComparison.Ordinal)))
                .Select(Map)
                .ToList();
            return Task.FromResult(OktaResult<IReadOnlyList<OktaUser>>.Ok(matches));
        }
    }

    public Task<OktaResult<OktaUser>> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!ApiAvailable)
            {
                return Task.FromResult(OktaResult<OktaUser>.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            return Task.FromResult(Users.TryGetValue(userId, out var u) ? OktaResult<OktaUser>.Ok(Map(u)) : OktaResult<OktaUser>.Fail(404, SafeErrorCategory.NotFound, "E0000007"));
        }
    }

    public Task<OktaResult<OktaUser>> UpdateProfileAsync(string userId, IReadOnlyDictionary<string, string> profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (gate)
        {
            if (!Users.TryGetValue(userId, out var u))
            {
                return Task.FromResult(OktaResult<OktaUser>.Fail(404, SafeErrorCategory.NotFound, "E0000007"));
            }

            foreach (var (k, v) in profile)
            {
                u.Profile[k] = v;
            }

            Log("user.account.update_profile", "SUCCESS", u.Id);
            if (AdIntegration.PushProfileUpdates && u.AdObjectGuid is not null)
            {
                Queue(u.Id, PushKind.ProfileUpdate);
            }

            return Task.FromResult(OktaResult<OktaUser>.Ok(Map(u)));
        }
    }

    public Task<OktaResult<IReadOnlyList<string>>> GetFactorTypesAsync(string userId, CancellationToken cancellationToken) =>
        WithUser<IReadOnlyList<string>>(userId, u => u.Factors.ToList());

    public Task<OktaResult<IReadOnlyList<string>>> GetAssignedApplicationIdsAsync(string userId, CancellationToken cancellationToken) =>
        WithUser<IReadOnlyList<string>>(userId, u => u.AppIds.ToList());

    public Task<OktaResult> ActivateAsync(string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "user.lifecycle.activate", u =>
        {
            var wasDeprovisioned = u.Status == OktaUserStatus.Deprovisioned;
            u.Status = OktaUserStatus.Active;
            if (wasDeprovisioned && AdIntegration.ReactivateEnables && u.AdObjectGuid is not null)
            {
                Queue(u.Id, PushKind.Enable);
            }

            QueueProvisioningIfAssigned(u);
            return true;
        });

    public Task<OktaResult> SuspendAsync(string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "user.lifecycle.suspend", u =>
        {
            if (u.Status != OktaUserStatus.Active)
            {
                return false;
            }

            u.Status = OktaUserStatus.Suspended;
            if (AdIntegration.DisableOnSuspend && u.AdObjectGuid is not null)
            {
                Queue(u.Id, PushKind.Disable);
            }

            return true;
        });

    public Task<OktaResult> UnsuspendAsync(string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "user.lifecycle.unsuspend", u =>
        {
            if (u.Status != OktaUserStatus.Suspended)
            {
                return false;
            }

            u.Status = OktaUserStatus.Active;
            return true;
        });

    public Task<OktaResult> DeactivateAsync(string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "user.lifecycle.deactivate", u =>
        {
            u.Status = OktaUserStatus.Deprovisioned;
            u.ActiveSessions = 0;
            if (AdIntegration.DeprovisionOnDeactivate && u.AdObjectGuid is not null)
            {
                Queue(u.Id, PushKind.Disable);
            }

            return true;
        });

    public Task<OktaResult<string>> GetLifecycleStateAsync(string userId, CancellationToken cancellationToken) =>
        WithUser(userId, u => u.Status);

    public Task<OktaResult> RevokeSessionsAsync(string userId, bool revokeOAuthTokens, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!ApiAvailable || FailSessionRevocation)
            {
                return Task.FromResult(OktaResult.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            if (!Users.TryGetValue(userId, out var u))
            {
                return Task.FromResult(OktaResult.Fail(404, SafeErrorCategory.NotFound, "E0000007"));
            }

            u.ActiveSessions = 0;
            Log(revokeOAuthTokens ? "user.session.clear+oauth2.tokens.revoke" : "user.session.clear", "SUCCESS", u.Id);
            return Task.FromResult(OktaResult.Ok(204));
        }
    }

    public Task<OktaResult> AssignUserToGroupAsync(string groupId, string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "group.user_membership.add", u =>
        {
            u.GroupIds.Add(groupId);
            QueueProvisioningIfAssigned(u);
            return true;
        });

    public Task<OktaResult> RemoveUserFromGroupAsync(string groupId, string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "group.user_membership.remove", u => u.GroupIds.Remove(groupId) || true);

    public Task<OktaResult<IReadOnlyList<string>>> GetUserGroupIdsAsync(string userId, CancellationToken cancellationToken) =>
        WithUser<IReadOnlyList<string>>(userId, u => u.GroupIds.ToList());

    public Task<OktaResult> AssignUserToApplicationAsync(string appId, string userId, CancellationToken cancellationToken) =>
        Lifecycle(userId, "application.user_membership.add", u =>
        {
            u.AppIds.Add(appId);
            QueueProvisioningIfAssigned(u);
            return true;
        });

    public Task<OktaResult<IReadOnlyList<string>>> GetAssignmentRolesAsync(string appId, string userId, CancellationToken cancellationToken) =>
        WithUser<IReadOnlyList<string>>(userId, u => u.AppRoles.TryGetValue(appId, out var roles) ? roles.ToList() : []);

    public Task<OktaResult<IReadOnlyList<OktaLogEvent>>> QueryAsync(DateTime sinceUtc, string? targetUserId, string? eventTypePrefix, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!ApiAvailable)
            {
                return Task.FromResult(OktaResult<IReadOnlyList<OktaLogEvent>>.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            IReadOnlyList<OktaLogEvent> events = SystemLog
                .Where(e => e.PublishedUtc >= sinceUtc
                    && (targetUserId is null || e.TargetUserId == targetUserId)
                    && (eventTypePrefix is null || e.EventType.StartsWith(eventTypePrefix, StringComparison.Ordinal)))
                .ToList();
            return Task.FromResult(OktaResult<IReadOnlyList<OktaLogEvent>>.Ok(events));
        }
    }

    public Task<OktaResult> DeleteSyntheticUserAsync(string userId, string requiredLoginPrefix, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!Users.TryGetValue(userId, out var u))
            {
                return Task.FromResult(OktaResult.Fail(404, SafeErrorCategory.NotFound));
            }

            if (string.IsNullOrWhiteSpace(requiredLoginPrefix) || !u.Login.StartsWith(requiredLoginPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OktaResult.Fail(403, SafeErrorCategory.NotAuthorised, "NOT_SYNTHETIC"));
            }

            if (u.Status != OktaUserStatus.Deprovisioned)
            {
                return Task.FromResult(OktaResult.Fail(400, SafeErrorCategory.ValidationFailed, "E0000001"));
            }

            Users.Remove(userId);
            Log("user.lifecycle.delete.completed", "SUCCESS", userId);
            return Task.FromResult(OktaResult.Ok(204));
        }
    }

    /// <summary>Records an Okta change made by someone other than ILM (for out-of-band detection tests).</summary>
    public void SimulateOutOfBandLifecycle(string userId, string eventType, string actorId)
    {
        lock (gate)
        {
            if (Users.TryGetValue(userId, out var u) && eventType == "user.lifecycle.deactivate")
            {
                u.Status = OktaUserStatus.Deprovisioned;
            }

            SystemLog.Add(new OktaLogEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, eventType, "SUCCESS", actorId, "User", userId, Guid.NewGuid().ToString("N"), "Out-of-band change"));
        }
    }

    private void QueueProvisioningIfAssigned(MockOktaUser u)
    {
        var assigned = u.GroupIds.Any(AdIntegration.ProvisioningGroupIds.Contains) || u.AppIds.Any(AdIntegration.ProvisioningGroupIds.Contains);
        var eligible = u.Status == OktaUserStatus.Active || (AdIntegration.ProvisionStagedUsers && u.Status == OktaUserStatus.Staged);
        if (assigned && eligible && u.AdObjectGuid is null && !pending.Any(p => p.UserId == u.Id && p.Kind == PushKind.Create))
        {
            Queue(u.Id, PushKind.Create);
        }
    }

    private void Queue(string userId, PushKind kind) => pending.Add(new PendingPush(userId, kind, AdIntegration.PushDelayTicks));

    /// <summary>The simulated Okta AD Agent: applies pushes whose delay has elapsed.</summary>
    private void ProcessDuePushes()
    {
        lock (gate)
        {
            if (pending.Count == 0)
            {
                return;
            }

            if (!AdIntegration.AgentAvailable)
            {
                foreach (var p in pending.Where(p => !p.FailureLogged))
                {
                    p.FailureLogged = true;
                    Log("application.provision.user.push", "FAILURE", p.UserId);
                }

                return;
            }

            foreach (var push in pending.ToList())
            {
                if (push.TicksRemaining-- > 0)
                {
                    continue;
                }

                pending.Remove(push);
                if (Users.TryGetValue(push.UserId, out var user))
                {
                    Apply(push.Kind, user);
                }
            }
        }
    }

    private void Apply(PushKind kind, MockOktaUser user)
    {
        if (!directory.HasForest(AdIntegration.TargetConnectorId))
        {
            return;
        }

        var forest = directory.Forest(AdIntegration.TargetConnectorId);
        switch (kind)
        {
            case PushKind.Create:
                var existing = forest.Objects.Values.FirstOrDefault(o => o.Kind == DirectoryObjectKind.User && string.Equals(o.Get("userPrincipalName"), user.Login, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    user.AdObjectGuid = existing.Guid;
                    Log("application.provision.user.import_match", "SUCCESS", user.Id);
                    return;
                }

                var department = user.Profile.GetValueOrDefault("department");
                var ouGuid = department is not null && AdIntegration.OuByDepartment.TryGetValue(department, out var mapped) ? mapped : AdIntegration.DefaultOuGuid;
                if (!forest.Objects.TryGetValue(ouGuid, out var ou))
                {
                    Log("application.provision.user.push", "FAILURE", user.Id);
                    return;
                }

                var local = user.Login.Split('@')[0];
                var sam = local.Length > 20 ? local[..20] : local;
                var ad = new MockObject
                {
                    DistinguishedName = $"CN={user.Profile.GetValueOrDefault("firstName")} {user.Profile.GetValueOrDefault("lastName")},{ou.DistinguishedName}",
                    Kind = DirectoryObjectKind.User,
                    ObjectClasses = ["top", "person", "organizationalPerson", "user"],
                    Sid = forest.NewSid(),
                    UserAccountControl = (int)UserAccountControl.NormalAccount,
                    PrimaryGroupId = 513,
                };
                ad.Attributes["cn"] = $"{user.Profile.GetValueOrDefault("firstName")} {user.Profile.GetValueOrDefault("lastName")}";
                ad.Attributes["sAMAccountName"] = sam;
                ad.Attributes["userPrincipalName"] = user.Login;
                ad.Attributes["mail"] = user.Profile.GetValueOrDefault("email");
                ad.Attributes["department"] = department;
                ad.Attributes["displayName"] = ad.Attributes["cn"];
                if (user.Profile.GetValueOrDefault("managerId") is { } managerId && Users.TryGetValue(managerId, out var manager)
                    && manager.AdObjectGuid is { } mg && forest.Objects.TryGetValue(mg, out var managerAd))
                {
                    ad.Attributes["manager"] = managerAd.DistinguishedName;
                }

                ad.ProxyAddresses.Add("SMTP:" + user.Profile.GetValueOrDefault("email"));
                forest.Objects[ad.Guid] = ad;
                user.AdObjectGuid = ad.Guid;
                Log("application.provision.user.push", "SUCCESS", user.Id);
                break;

            case PushKind.Disable when user.AdObjectGuid is { } g && forest.Objects.TryGetValue(g, out var toDisable):
                toDisable.UserAccountControl = UserAccountControlExtensions.WithDisableBitSet(toDisable.UserAccountControl ?? (int)UserAccountControl.NormalAccount);
                Log("application.provision.user.deactivate", "SUCCESS", user.Id);
                break;

            case PushKind.Enable when user.AdObjectGuid is { } g && forest.Objects.TryGetValue(g, out var toEnable):
                toEnable.UserAccountControl = (toEnable.UserAccountControl ?? 0) & ~(int)UserAccountControl.AccountDisable;
                Log("application.provision.user.reactivate", "SUCCESS", user.Id);
                break;

            case PushKind.ProfileUpdate when user.AdObjectGuid is { } g && forest.Objects.TryGetValue(g, out var toUpdate):
                toUpdate.Attributes["department"] = user.Profile.GetValueOrDefault("department");
                toUpdate.Attributes["title"] = user.Profile.GetValueOrDefault("title");
                Log("application.provision.user.push_profile", "SUCCESS", user.Id);
                break;
        }
    }

    private Task<OktaResult> Lifecycle(string userId, string eventType, Func<MockOktaUser, bool> change)
    {
        lock (gate)
        {
            if (!ApiAvailable)
            {
                return Task.FromResult(OktaResult.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            if (!Users.TryGetValue(userId, out var u))
            {
                return Task.FromResult(OktaResult.Fail(404, SafeErrorCategory.NotFound, "E0000007"));
            }

            if (!change(u))
            {
                Log(eventType, "FAILURE", userId);
                return Task.FromResult(OktaResult.Fail(400, SafeErrorCategory.ValidationFailed, "E0000001"));
            }

            u.StatusChangedUtc = DateTime.UtcNow;
            Log(eventType, "SUCCESS", userId);
            return Task.FromResult(OktaResult.Ok());
        }
    }

    private Task<OktaResult<T>> WithUser<T>(string userId, Func<MockOktaUser, T> read)
    {
        lock (gate)
        {
            if (!ApiAvailable)
            {
                return Task.FromResult(OktaResult<T>.Fail(503, SafeErrorCategory.ConnectorUnavailable));
            }

            return Task.FromResult(Users.TryGetValue(userId, out var u) ? OktaResult<T>.Ok(read(u)) : OktaResult<T>.Fail(404, SafeErrorCategory.NotFound, "E0000007"));
        }
    }

    private void Log(string eventType, string outcome, string? targetUserId) =>
        SystemLog.Add(new OktaLogEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, eventType, outcome, IlmActorId, "PublicClientApp", targetUserId, Guid.NewGuid().ToString("N"), eventType));

    private static OktaUser Map(MockOktaUser u) => new(
        u.Id,
        u.Status,
        u.Login,
        u.Profile.GetValueOrDefault("email"),
        u.Profile.GetValueOrDefault("firstName"),
        u.Profile.GetValueOrDefault("lastName"),
        u.Profile.GetValueOrDefault("department"),
        u.Profile.GetValueOrDefault("managerId"),
        u.CreatedUtc,
        u.StatusChangedUtc);

    public static string NewId(string prefix)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[17];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return prefix + new string(chars);
    }

    private enum PushKind
    {
        Create,
        Disable,
        Enable,
        ProfileUpdate,
    }

    private sealed class PendingPush(string userId, PushKind kind, int ticks)
    {
        public string UserId { get; } = userId;

        public PushKind Kind { get; } = kind;

        public int TicksRemaining { get; set; } = ticks;

        public bool FailureLogged { get; set; }
    }
}
