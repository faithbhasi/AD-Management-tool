using Ilm.Domain.Directory;

namespace Ilm.Domain.Protection;

/// <summary>
/// The non-configurable Tier 0 protection floor. Nothing in configuration, the UI or approvals can remove
/// an identifier from these sets; configuration can only add protection on top.
/// </summary>
public static class ProtectionFloor
{
    /// <summary>Domain RIDs protected in every domain.</summary>
    public static readonly IReadOnlySet<int> ProtectedDomainRids = new HashSet<int>
    {
        WellKnownSids.RidEnterpriseReadOnlyDomainControllers,
        WellKnownSids.RidAdministrator,
        WellKnownSids.RidKrbtgt,
        WellKnownSids.RidDomainAdmins,
        WellKnownSids.RidDomainControllers,
        WellKnownSids.RidCertPublishers,
        WellKnownSids.RidSchemaAdmins,
        WellKnownSids.RidEnterpriseAdmins,
        WellKnownSids.RidGroupPolicyCreatorOwners,
        WellKnownSids.RidReadOnlyDomainControllers,
        WellKnownSids.RidKeyAdmins,
        WellKnownSids.RidEnterpriseKeyAdmins,
    };

    /// <summary>Builtin-domain SIDs protected everywhere.</summary>
    public static readonly IReadOnlySet<string> ProtectedBuiltinSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownSids.BuiltinAdministrators,
        WellKnownSids.AccountOperators,
        WellKnownSids.ServerOperators,
        WellKnownSids.PrintOperators,
        WellKnownSids.BackupOperators,
        WellKnownSids.Replicator,
    };

    /// <summary>sAMAccountName prefixes of directory-synchronisation and KDC identities.</summary>
    public static readonly IReadOnlyList<string> ProtectedNamePrefixes = ["MSOL_", "AAD_", "Sync_", "krbtgt"];

    private static readonly HashSet<string> ServiceAccountClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "msDS-GroupManagedServiceAccount",
        "msDS-ManagedServiceAccount",
        "msDS-DelegatedManagedServiceAccount",
    };

    public static bool IsFloorSid(string? sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return false;
        }

        if (ProtectedBuiltinSids.Contains(sid))
        {
            return true;
        }

        var rid = WellKnownSids.DomainRid(sid);
        return rid is not null && ProtectedDomainRids.Contains(rid.Value);
    }

    /// <summary>Evaluates the object's own attributes against the floor (not its memberships).</summary>
    public static IEnumerable<ProtectionReason> EvaluateObject(DirectoryObjectFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (IsFloorSid(facts.ObjectSid))
        {
            yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.Tier0Identity, $"Well-known privileged SID {facts.ObjectSid}.");
        }

        if (facts.AdminCount is > 0)
        {
            yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.ProtectedByAdminCount, "adminCount=1 (AdminSDHolder-protected or previously protected).");
        }

        if (facts.UserAccountControl is { } uac)
        {
            if ((uac & (int)UserAccountControl.ServerTrustAccount) != 0 || (uac & (int)UserAccountControl.PartialSecretsAccount) != 0)
            {
                yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.DomainController, "Domain controller account.");
            }

            if ((uac & (int)UserAccountControl.TrustedForDelegation) != 0)
            {
                yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.UnconstrainedDelegation, "Trusted for unconstrained delegation.");
            }
        }

        if (facts.PrimaryGroupId is WellKnownSids.RidDomainControllers or WellKnownSids.RidReadOnlyDomainControllers)
        {
            yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.DomainController, "Primary group is a domain controllers group.");
        }
        else if (facts.PrimaryGroupId is { } primaryGroup && ProtectedDomainRids.Contains(primaryGroup))
        {
            // primaryGroupID membership never appears in the member attribute or in-chain queries.
            yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.Tier0Group, $"Primary group RID {primaryGroup} is protected.");
        }

        if (facts.ObjectClasses.Any(ServiceAccountClasses.Contains))
        {
            yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.ManagedServiceAccount, "Managed service account.");
        }

        if (facts.SamAccountName is { } sam && ProtectedNamePrefixes.Any(p => sam.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ProtectionReason(ProtectionSource.HardcodedFloor, ProtectionCategory.EntraSyncIdentity, "Directory synchronisation or KDC account name.");
        }
    }
}
