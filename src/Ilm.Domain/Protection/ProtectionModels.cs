namespace Ilm.Domain.Protection;

public enum ProtectionStatus
{
    Clear = 0,
    Protected,
    Unknown,
}

public enum ProtectionSource
{
    HardcodedFloor = 0,
    PlatformIdentity,
    ImportedAttackPath,
    ConfiguredAddition,
    Evaluation,
}

public enum ProtectionCategory
{
    Tier0Group = 0,
    Tier0Identity,
    DomainController,
    CertificateAuthorityServer,
    AdfsServer,
    OktaAgentServer,
    EntraSyncIdentity,
    CitrixInfrastructure,
    PrivilegedPkiIdentity,
    BreakGlassIdentity,
    PortalHost,
    PortalRuntimeIdentity,
    DatabaseServiceIdentity,
    ManagedPasswordRetriever,
    DcSyncRights,
    AdminSdHolderControl,
    DomainRootControl,
    PrivilegedGpoEditor,
    UnconstrainedDelegation,
    ManagedServiceAccount,
    ProtectedByAdminCount,
    Tier0Adjacent,
    Other,
}

public enum ProtectedObjectMatch
{
    Sid = 0,
    ObjectGuid,
    DnsHostName,
    SamAccountName,
}

/// <summary>An imported or configured protection addition. Additions can only add protection.</summary>
public sealed record ProtectedObjectEntry(
    ProtectedObjectMatch MatchOn,
    string Value,
    ProtectionCategory Category,
    ProtectionSource Source,
    string? SourceReference);

public sealed record ProtectionReason(ProtectionSource Source, ProtectionCategory Category, string Detail);

public sealed record ProtectionDecision(ProtectionStatus Status, IReadOnlyList<ProtectionReason> Reasons, IReadOnlyList<string> Unknowns)
{
    /// <summary>Automated operations are allowed only when protection is conclusively Clear.</summary>
    public bool AllowsAutomation => Status == ProtectionStatus.Clear;

    public string ToAuditString() => Status switch
    {
        ProtectionStatus.Clear => "Clear",
        ProtectionStatus.Protected => "Protected:" + string.Join(",", Reasons.Select(r => $"{r.Source}/{r.Category}")),
        _ => "Unknown:" + string.Join(",", Unknowns),
    };

    public static ProtectionDecision UnknownBecause(string reason) => new(ProtectionStatus.Unknown, [], [reason]);
}

/// <summary>
/// Everything ILM knows about a directory object when deciding protection.
/// Incomplete facts produce an Unknown decision, which always denies automation.
/// </summary>
public sealed record DirectoryObjectFacts
{
    public required string ConnectorId { get; init; }

    public required string ForestId { get; init; }

    public required Guid ObjectGuid { get; init; }

    public string? ObjectSid { get; init; }

    public string DistinguishedName { get; init; } = string.Empty;

    public string? SamAccountName { get; init; }

    public string? DnsHostName { get; init; }

    public IReadOnlyList<string> ObjectClasses { get; init; } = [];

    public int? UserAccountControl { get; init; }

    public int? AdminCount { get; init; }

    public int? PrimaryGroupId { get; init; }

    /// <summary>True when every protection-relevant attribute was read successfully.</summary>
    public bool AttributesComplete { get; init; }

    /// <summary>Transitive group SIDs in the object's own forest and, via foreign security principals, in other forests.</summary>
    public IReadOnlyCollection<string> TransitiveGroupSids { get; init; } = [];

    public IReadOnlyCollection<Guid> TransitiveGroupGuids { get; init; } = [];

    public bool MembershipComplete { get; init; }

    public IReadOnlyList<string> IncompleteReasons { get; init; } = [];
}

/// <summary>
/// The portal's own identities. These are permanently protected and cannot be removed by configuration.
/// </summary>
public sealed record PlatformIdentities
{
    public IReadOnlyCollection<string> RuntimeIdentitySids { get; init; } = [];

    public IReadOnlyCollection<string> HostComputerSids { get; init; } = [];

    public IReadOnlyCollection<string> HostDnsNames { get; init; } = [];

    public IReadOnlyCollection<string> DatabaseServiceIdentitySids { get; init; } = [];

    public IReadOnlyCollection<string> BreakGlassSids { get; init; } = [];

    /// <summary>Principals in PrincipalsAllowedToRetrieveManagedPassword of the portal gMSA.</summary>
    public IReadOnlyCollection<string> ManagedPasswordRetrieverSids { get; init; } = [];

    /// <summary>False when the retriever list could not be read; every evaluation is then Unknown.</summary>
    public bool Complete { get; init; }
}
