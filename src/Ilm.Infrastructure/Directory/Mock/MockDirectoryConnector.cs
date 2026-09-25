using Ilm.Application.Directory;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;

namespace Ilm.Infrastructure.Directory.Mock;

/// <summary>Reader and raw write channel over the in-memory fictional directory.</summary>
public sealed class MockDirectoryConnector(InMemoryDirectoryStore store, DirectoryConnectorDefinition definition) : IDirectoryReader, IDirectoryWriteChannel
{
    private static readonly HashSet<string> LookupAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sAMAccountName", "userPrincipalName", "mail", "proxyAddresses", "employeeID",
    };

    public string ConnectorId => definition.Id;

    public DirectoryConnectorDefinition Definition => definition;

    private MockForest Forest
    {
        get
        {
            store.RunReadHooks();
            var forest = store.Forest(definition.Id);
            if (!forest.Available)
            {
                throw new IOException($"Mock directory '{definition.Id}' is unavailable.");
            }

            return forest;
        }
    }

    public Task<DirectoryObject?> GetByGuidAsync(Guid objectGuid, string? domainController, CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            return Task.FromResult(forest.Objects.TryGetValue(objectGuid, out var o) ? Map(forest, o) : null);
        }
    }

    public Task<DirectoryObject?> GetBySidAsync(string objectSid, CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            var o = forest.FindBySid(objectSid);
            return Task.FromResult(o is null ? null : Map(forest, o));
        }
    }

    public Task<IReadOnlyList<DirectoryObject>> SearchAsync(DirectorySearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        lock (store.Gate)
        {
            var forest = Forest;
            if (!forest.Objects.TryGetValue(search.BaseOuGuid, out var baseOu))
            {
                return Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
            }

            var text = search.Text?.Trim();
            var results = forest.Objects.Values
                .Where(o => o.Kind == search.Kind && DistinguishedNameHelper.IsWithin(o.DistinguishedName, baseOu.DistinguishedName))
                .Where(o => string.IsNullOrEmpty(text)
                    || StartsWith(o.Get("sAMAccountName"), text) || StartsWith(o.Get("displayName"), text)
                    || StartsWith(o.Get("mail"), text) || StartsWith(o.Get("userPrincipalName"), text) || StartsWith(o.Get("cn"), text))
                .OrderBy(o => o.Get("sAMAccountName") ?? o.Get("cn"), StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(search.MaxResults, 1, 200))
                .Select(o => Map(forest, o))
                .ToList();
            return Task.FromResult<IReadOnlyList<DirectoryObject>>(results);
        }
    }

    public Task<IReadOnlyList<DirectoryObject>> ListChildOrganizationalUnitsAsync(Guid parentOuGuid, CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            if (!forest.Objects.TryGetValue(parentOuGuid, out var parent))
            {
                return Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
            }

            var children = forest.Objects.Values
                .Where(o => o.Kind == DirectoryObjectKind.OrganizationalUnit && string.Equals(ParentOf(o.DistinguishedName), parent.DistinguishedName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Select(o => Map(forest, o))
                .ToList();
            return Task.FromResult<IReadOnlyList<DirectoryObject>>(children);
        }
    }

    public Task<MembershipResult> GetTransitiveGroupsAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            if (!forest.Objects.TryGetValue(objectGuid, out var start))
            {
                return Task.FromResult(new MembershipResult([], [], false, "Object not found."));
            }

            if (start.FailMembershipReads)
            {
                return Task.FromResult(new MembershipResult([], [], false, "Membership query failed (simulated)."));
            }

            var seeds = new List<Guid> { start.Guid };
            if (start.PrimaryGroupId is { } rid && forest.FindBySid($"{forest.DomainSid}-{rid}") is { } primary)
            {
                seeds.Add(primary.Guid);
            }

            var groups = TransitiveGroups(forest, seeds);
            if (start.PrimaryGroupId is { } pg && forest.FindBySid($"{forest.DomainSid}-{pg}") is { } primaryGroup)
            {
                groups.Add(primaryGroup);
            }

            return Task.FromResult(new MembershipResult(
                groups.Where(g => g.Sid is not null).Select(g => g.Sid!).ToList(),
                groups.Select(g => g.Guid).ToList(),
                true,
                null));
        }
    }

    public Task<MembershipResult> GetForeignPrincipalGroupsAsync(IReadOnlyCollection<string> foreignSids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(foreignSids);
        lock (store.Gate)
        {
            var forest = Forest;
            var fsps = forest.Objects.Values
                .Where(o => o.Kind == DirectoryObjectKind.ForeignSecurityPrincipal && o.ForeignSid is not null && foreignSids.Contains(o.ForeignSid, StringComparer.OrdinalIgnoreCase))
                .Select(o => o.Guid)
                .ToList();
            var groups = TransitiveGroups(forest, fsps);
            return Task.FromResult(new MembershipResult(
                groups.Where(g => g.Sid is not null).Select(g => g.Sid!).ToList(),
                groups.Select(g => g.Guid).ToList(),
                true,
                null));
        }
    }

    public Task<IReadOnlyList<DirectoryObject>> FindUsersByAttributeAsync(string attribute, string value, CancellationToken cancellationToken)
    {
        if (!LookupAttributes.Contains(attribute))
        {
            throw new ArgumentException($"Attribute '{attribute}' is not an allowed lookup attribute.", nameof(attribute));
        }

        lock (store.Gate)
        {
            var forest = Forest;
            var matches = forest.Objects.Values
                .Where(o => o.Kind == DirectoryObjectKind.User)
                .Where(o => string.Equals(attribute, "proxyAddresses", StringComparison.OrdinalIgnoreCase)
                    ? o.ProxyAddresses.Contains(value, StringComparer.OrdinalIgnoreCase)
                    : string.Equals(o.Get(attribute), value, StringComparison.OrdinalIgnoreCase))
                .Select(o => Map(forest, o))
                .ToList();
            return Task.FromResult<IReadOnlyList<DirectoryObject>>(matches);
        }
    }

    public Task<DomainControllerSelection> SelectWritableDomainControllerAsync(CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            var dc = forest.DomainControllers.FirstOrDefault() ?? throw new IOException("No domain controller configured.");
            var forestDn = string.Join(",", forest.ForestDns.Split('.').Select(p => "DC=" + p));
            return Task.FromResult(new DomainControllerSelection(definition.Id, dc, DateTime.UtcNow, forest.DomainDn, forestDn));
        }
    }

    public Task<AccountStateReading?> ReadAccountStateAsync(Guid objectGuid, string domainController, CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            return Task.FromResult(forest.Objects.TryGetValue(objectGuid, out var o) && o.UserAccountControl is { } uac
                ? new AccountStateReading(objectGuid, uac, domainController, DateTime.UtcNow)
                : null);
        }
    }

    public Task<ConnectorHealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        lock (store.Gate)
        {
            var forest = store.Forest(definition.Id);
            return Task.FromResult(new ConnectorHealthReport(definition.Id, forest.Available, forest.Available ? "Mock directory reachable." : "Mock directory unavailable.", forest.DomainControllers.FirstOrDefault()));
        }
    }

    public Task<DirectoryWriteResult> SetAccountDisableBitAsync(Guid objectGuid, string domainController, int expectedCurrentValue, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            var o = forest.Objects[objectGuid];
            if (o.UserAccountControl != expectedCurrentValue)
            {
                return new DirectoryWriteResult(false, SafeErrorCategory.ConcurrencyConflict, domainController, o.UserAccountControl, null, false, "userAccountControl changed since it was read.");
            }

            var newValue = UserAccountControlExtensions.WithDisableBitSet(expectedCurrentValue);
            o.UserAccountControl = newValue;
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, expectedCurrentValue, newValue, false, "ACCOUNTDISABLE set.");
        }, objectGuid);

    public Task<DirectoryWriteResult> SetAttributeAsync(Guid objectGuid, string domainController, string attribute, string value, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            forest.Objects[objectGuid].Attributes[attribute] = value;
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, null, null, false, $"{attribute} set.");
        }, objectGuid);

    public Task<DirectoryWriteResult> CreateDisabledUserAsync(NewUserRequest request, string domainController, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(domainController, forest =>
        {
            if (!forest.Objects.TryGetValue(request.TargetOuGuid, out var ou))
            {
                return new DirectoryWriteResult(false, SafeErrorCategory.NotFound, domainController, null, null, false, "Target OU not found.");
            }

            if (forest.Objects.Values.Any(o => string.Equals(o.Get("sAMAccountName"), request.SamAccountName, StringComparison.OrdinalIgnoreCase)))
            {
                return new DirectoryWriteResult(false, SafeErrorCategory.DuplicateObject, domainController, null, null, false, "sAMAccountName already exists.");
            }

            var user = new MockObject
            {
                DistinguishedName = $"CN={request.CommonName},{ou.DistinguishedName}",
                Kind = DirectoryObjectKind.User,
                ObjectClasses = ["top", "person", "organizationalPerson", "user"],
                Sid = forest.NewSid(),
                UserAccountControl = (int)(UserAccountControl.NormalAccount | UserAccountControl.AccountDisable),
                PrimaryGroupId = 513,
            };
            user.Attributes["cn"] = request.CommonName;
            user.Attributes["sAMAccountName"] = request.SamAccountName;
            user.Attributes["userPrincipalName"] = request.UserPrincipalName;
            user.Attributes["givenName"] = request.GivenName;
            user.Attributes["sn"] = request.Surname;
            user.Attributes["displayName"] = request.DisplayName;
            user.Attributes["mail"] = request.Mail;
            user.Attributes["department"] = request.Department;
            user.Attributes["employeeID"] = request.EmployeeId;
            forest.Objects[user.Guid] = user;
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, null, user.UserAccountControl, false, $"Created {user.Guid:D} disabled.");
        }, null);
    }

    public Task<DirectoryWriteResult> SetPasswordAsync(Guid objectGuid, string domainController, ReadOnlyMemory<char> password, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            forest.Objects[objectGuid].Attributes["pwdSet"] = "true";
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, null, null, false, "Password set (mock; value discarded).");
        }, objectGuid);

    public Task<DirectoryWriteResult> RequirePasswordChangeAsync(Guid objectGuid, string domainController, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            forest.Objects[objectGuid].Attributes["pwdLastSet"] = "0";
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, null, null, false, "pwdLastSet=0.");
        }, objectGuid);

    public Task<DirectoryWriteResult> EnableAccountAsync(Guid objectGuid, string domainController, int expectedCurrentValue, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            var o = forest.Objects[objectGuid];
            if (o.UserAccountControl != expectedCurrentValue)
            {
                return new DirectoryWriteResult(false, SafeErrorCategory.ConcurrencyConflict, domainController, o.UserAccountControl, null, false, "userAccountControl changed since it was read.");
            }

            var newValue = expectedCurrentValue & ~(int)UserAccountControl.AccountDisable & ~(int)UserAccountControl.PasswordNotRequired;
            o.UserAccountControl = newValue;
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, expectedCurrentValue, newValue, false, "Enabled.");
        }, objectGuid);

    public Task<DirectoryWriteResult> AddGroupMemberAsync(Guid groupGuid, Guid memberGuid, string domainController, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            forest.Objects[groupGuid].Members.Add(memberGuid);
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, null, null, false, "Member added.");
        }, groupGuid);

    public Task<DirectoryWriteResult> RemoveGroupMemberAsync(Guid groupGuid, Guid memberGuid, string domainController, CancellationToken cancellationToken) =>
        Write(domainController, forest =>
        {
            forest.Objects[groupGuid].Members.Remove(memberGuid);
            return new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, null, null, false, "Member removed.");
        }, groupGuid);

    private Task<DirectoryWriteResult> Write(string domainController, Func<MockForest, DirectoryWriteResult> action, Guid? objectGuid)
    {
        lock (store.Gate)
        {
            var forest = Forest;
            if (!forest.DomainControllers.Contains(domainController, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(new DirectoryWriteResult(false, SafeErrorCategory.ConnectorUnavailable, domainController, null, null, false, "Unknown domain controller."));
            }

            if (objectGuid is { } g && !forest.Objects.ContainsKey(g))
            {
                return Task.FromResult(new DirectoryWriteResult(false, SafeErrorCategory.NotFound, domainController, null, null, false, "Object not found."));
            }

            if (forest.FailNextWrites > 0)
            {
                forest.FailNextWrites--;
                return Task.FromResult(new DirectoryWriteResult(false, SafeErrorCategory.InsufficientDirectoryRights, domainController, null, null, false, "Insufficient access rights (simulated)."));
            }

            forest.WriteCount++;
            var result = action(forest);
            if (result.Succeeded && objectGuid is { } changed)
            {
                forest.Objects[changed].WhenChangedUtc = DateTime.UtcNow;
            }

            return Task.FromResult(result);
        }
    }

    private static HashSet<MockObject> TransitiveGroups(MockForest forest, IEnumerable<Guid> seeds)
    {
        var result = new HashSet<MockObject>();
        var queue = new Queue<Guid>(seeds);
        var seen = new HashSet<Guid>(queue);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var group in forest.Objects.Values.Where(o => o.Kind == DirectoryObjectKind.Group && o.Members.Contains(current)))
            {
                result.Add(group);
                if (seen.Add(group.Guid))
                {
                    queue.Enqueue(group.Guid);
                }
            }
        }

        return result;
    }

    private static bool StartsWith(string? value, string text) =>
        value is not null && value.StartsWith(text, StringComparison.OrdinalIgnoreCase);

    private static string ParentOf(string dn)
    {
        var i = DistinguishedNameHelper.FirstUnescapedComma(dn);
        return i < 0 ? string.Empty : dn[(i + 1)..];
    }

    private DirectoryObject Map(MockForest forest, MockObject o) => new()
    {
        ConnectorId = definition.Id,
        ForestId = forest.ForestDns,
        ObjectGuid = o.Guid,
        ObjectSid = o.Sid,
        DistinguishedName = o.DistinguishedName,
        Kind = o.Kind,
        ObjectClasses = o.FailAttributeReads ? [] : o.ObjectClasses,
        Name = o.Get("cn") ?? o.Get("ou"),
        SamAccountName = o.Get("sAMAccountName"),
        UserPrincipalName = o.Get("userPrincipalName"),
        Mail = o.Get("mail"),
        ProxyAddresses = o.ProxyAddresses.ToList(),
        DisplayName = o.Get("displayName"),
        GivenName = o.Get("givenName"),
        Surname = o.Get("sn"),
        Department = o.Get("department"),
        Title = o.Get("title"),
        EmployeeId = o.Get("employeeID"),
        ManagerDistinguishedName = o.Get("manager"),
        Description = o.Get("description"),
        UserAccountControl = o.FailAttributeReads ? null : o.UserAccountControl,
        AdminCount = o.FailAttributeReads ? null : o.AdminCount,
        PrimaryGroupId = o.PrimaryGroupId,
        DnsHostName = o.Get("dNSHostName"),
        OperatingSystem = o.Get("operatingSystem"),
        OperatingSystemVersion = o.Get("operatingSystemVersion"),
        ManagedBy = o.Get("managedBy"),
        LastLogonTimestampUtc = o.LastLogonTimestampUtc,
        WhenCreatedUtc = o.WhenCreatedUtc,
        WhenChangedUtc = o.WhenChangedUtc,
        SourceAnchor = o.Get("mS-DS-ConsistencyGuid"),
    };
}
