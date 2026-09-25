using Ilm.Domain.Directory;

namespace Ilm.Infrastructure.Directory.Mock;

/// <summary>
/// A fictional, in-memory directory used for development and tests. It models forests, OUs, users,
/// computers, groups, foreign security principals and per-object fault injection. Thread-safe.
/// </summary>
public sealed class InMemoryDirectoryStore
{
    private readonly Dictionary<string, MockForest> forests = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> readHooks = [];

    public object Gate { get; } = new();

    /// <summary>Registers a callback run before every read (used by the mock Okta AD agent to apply due pushes).</summary>
    public void RegisterReadHook(Action hook)
    {
        lock (Gate)
        {
            readHooks.Add(hook);
        }
    }

    internal void RunReadHooks()
    {
        lock (Gate)
        {
            foreach (var hook in readHooks.ToList())
            {
                hook();
            }
        }
    }

    public MockForest AddForest(string connectorId, string forestDns, string domainDns, string domainSid, params string[] domainControllers)
    {
        lock (Gate)
        {
            var forest = new MockForest(connectorId, forestDns, domainDns, domainSid, domainControllers);
            forests[connectorId] = forest;
            return forest;
        }
    }

    public MockForest Forest(string connectorId)
    {
        lock (Gate)
        {
            return forests.TryGetValue(connectorId, out var f) ? f : throw new KeyNotFoundException($"Mock forest '{connectorId}' does not exist.");
        }
    }

    public bool HasForest(string connectorId)
    {
        lock (Gate)
        {
            return forests.ContainsKey(connectorId);
        }
    }

    public IReadOnlyList<MockForest> Forests
    {
        get
        {
            lock (Gate)
            {
                return forests.Values.ToList();
            }
        }
    }
}

public sealed class MockForest(string connectorId, string forestDns, string domainDns, string domainSid, IReadOnlyList<string> domainControllers)
{
    private int nextRid = 5000;

    public string ConnectorId { get; } = connectorId;

    public string ForestDns { get; } = forestDns;

    public string DomainDns { get; } = domainDns;

    public string DomainDn { get; } = string.Join(",", domainDns.Split('.').Select(p => "DC=" + p));

    public string DomainSid { get; } = domainSid;

    public IReadOnlyList<string> DomainControllers { get; } = domainControllers;

    public Dictionary<Guid, MockObject> Objects { get; } = [];

    /// <summary>Simulates an unreachable forest.</summary>
    public bool Available { get; set; } = true;

    /// <summary>Simulates the next N write failures (for example, insufficient rights).</summary>
    public int FailNextWrites { get; set; }

    public int WriteCount { get; set; }

    public string NewSid() => $"{DomainSid}-{Interlocked.Increment(ref nextRid)}";

    public MockObject? FindByDn(string dn) =>
        Objects.Values.FirstOrDefault(o => string.Equals(o.DistinguishedName, dn, StringComparison.OrdinalIgnoreCase));

    public MockObject? FindBySid(string sid) =>
        Objects.Values.FirstOrDefault(o => string.Equals(o.Sid, sid, StringComparison.OrdinalIgnoreCase));
}

public sealed class MockObject
{
    public Guid Guid { get; init; } = Guid.NewGuid();

    public string? Sid { get; set; }

    public required string DistinguishedName { get; set; }

    public DirectoryObjectKind Kind { get; init; }

    public List<string> ObjectClasses { get; init; } = [];

    public Dictionary<string, string?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ProxyAddresses { get; } = [];

    public int? UserAccountControl { get; set; }

    public int? AdminCount { get; set; }

    public int? PrimaryGroupId { get; set; }

    /// <summary>For groups: direct members (users, groups, foreign security principals).</summary>
    public HashSet<Guid> Members { get; } = [];

    /// <summary>For foreign security principals: the SID in the other forest.</summary>
    public string? ForeignSid { get; init; }

    public DateTime? LastLogonTimestampUtc { get; set; }

    public DateTime WhenCreatedUtc { get; set; } = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    public DateTime WhenChangedUtc { get; set; } = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Fault injection: attribute reads fail (protection becomes Unknown).</summary>
    public bool FailAttributeReads { get; set; }

    /// <summary>Fault injection: membership queries fail (protection becomes Unknown).</summary>
    public bool FailMembershipReads { get; set; }

    public string? Get(string attribute) => Attributes.TryGetValue(attribute, out var v) ? v : null;
}
