namespace Ilm.Domain.Protection;

/// <summary>
/// Combines the hardcoded floor, platform identities, imported attack-path lists and configured additions.
/// Protected always wins; otherwise any incompleteness yields Unknown, which denies automation.
/// </summary>
public static class ProtectionEvaluator
{
    public static ProtectionDecision Evaluate(
        DirectoryObjectFacts facts,
        IReadOnlyCollection<ProtectedObjectEntry> additions,
        PlatformIdentities platform)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(platform);

        var reasons = new List<ProtectionReason>(ProtectionFloor.EvaluateObject(facts));

        foreach (var sid in facts.TransitiveGroupSids)
        {
            if (ProtectionFloor.IsFloorSid(sid))
            {
                reasons.Add(new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.Tier0Group, $"Recursive member of protected group {sid}."));
            }
        }

        reasons.AddRange(EvaluatePlatform(facts, platform));
        reasons.AddRange(EvaluateAdditions(facts, additions));

        if (reasons.Count > 0)
        {
            return new ProtectionDecision(ProtectionStatus.Protected, reasons, []);
        }

        var unknowns = new List<string>();
        if (!facts.AttributesComplete)
        {
            unknowns.Add("Protection-relevant attributes could not be read.");
        }

        if (!facts.MembershipComplete)
        {
            unknowns.Add("Recursive or cross-forest group membership could not be fully evaluated.");
        }

        if (!platform.Complete)
        {
            unknowns.Add("Platform identities (including gMSA password retrievers) could not be loaded.");
        }

        unknowns.AddRange(facts.IncompleteReasons);

        return unknowns.Count > 0
            ? new ProtectionDecision(ProtectionStatus.Unknown, [], unknowns.Distinct().ToList())
            : new ProtectionDecision(ProtectionStatus.Clear, [], []);
    }

    private static IEnumerable<ProtectionReason> EvaluatePlatform(DirectoryObjectFacts facts, PlatformIdentities platform)
    {
        var sid = facts.ObjectSid;
        if (sid is not null)
        {
            if (platform.RuntimeIdentitySids.Contains(sid, StringComparer.OrdinalIgnoreCase))
            {
                yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.PortalRuntimeIdentity, "Portal runtime identity.");
            }

            if (platform.HostComputerSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
            {
                yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.PortalHost, "Portal host computer.");
            }

            if (platform.DatabaseServiceIdentitySids.Contains(sid, StringComparer.OrdinalIgnoreCase))
            {
                yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.DatabaseServiceIdentity, "Database service identity.");
            }

            if (platform.BreakGlassSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
            {
                yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.BreakGlassIdentity, "Approved break-glass identity.");
            }

            if (platform.ManagedPasswordRetrieverSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
            {
                yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.ManagedPasswordRetriever, "Can retrieve the portal gMSA password.");
            }
        }

        foreach (var groupSid in facts.TransitiveGroupSids)
        {
            if (platform.ManagedPasswordRetrieverSids.Contains(groupSid, StringComparer.OrdinalIgnoreCase))
            {
                yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.ManagedPasswordRetriever, $"Member of gMSA password retriever group {groupSid}.");
            }
        }

        if (facts.DnsHostName is { } dns && platform.HostDnsNames.Contains(dns, StringComparer.OrdinalIgnoreCase))
        {
            yield return new(ProtectionSource.PlatformIdentity, ProtectionCategory.PortalHost, "Portal host computer.");
        }
    }

    private static IEnumerable<ProtectionReason> EvaluateAdditions(DirectoryObjectFacts facts, IReadOnlyCollection<ProtectedObjectEntry> additions)
    {
        foreach (var entry in additions)
        {
            var matched = entry.MatchOn switch
            {
                ProtectedObjectMatch.Sid => Same(entry.Value, facts.ObjectSid)
                    || facts.TransitiveGroupSids.Contains(entry.Value, StringComparer.OrdinalIgnoreCase),
                ProtectedObjectMatch.ObjectGuid => Guid.TryParse(entry.Value, out var g)
                    && (g == facts.ObjectGuid || facts.TransitiveGroupGuids.Contains(g)),
                ProtectedObjectMatch.DnsHostName => Same(entry.Value, facts.DnsHostName),
                ProtectedObjectMatch.SamAccountName => Same(entry.Value, facts.SamAccountName),
                _ => false,
            };

            if (matched)
            {
                yield return new ProtectionReason(entry.Source, entry.Category, $"Listed by {entry.Source} ({entry.SourceReference ?? "no reference"}).");
            }
        }
    }

    private static bool Same(string a, string? b) => b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
