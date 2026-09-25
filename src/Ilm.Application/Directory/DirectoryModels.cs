using Ilm.Domain.Common;
using Ilm.Domain.Directory;

namespace Ilm.Application.Directory;

/// <summary>A directory object restricted to the safe attribute allowlist.</summary>
public sealed record DirectoryObject
{
    public required string ConnectorId { get; init; }

    public required string ForestId { get; init; }

    public required Guid ObjectGuid { get; init; }

    public string? ObjectSid { get; init; }

    public required string DistinguishedName { get; init; }

    public DirectoryObjectKind Kind { get; init; }

    public IReadOnlyList<string> ObjectClasses { get; init; } = [];

    public string? Name { get; init; }

    public string? SamAccountName { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? Mail { get; init; }

    public IReadOnlyList<string> ProxyAddresses { get; init; } = [];

    public string? DisplayName { get; init; }

    public string? GivenName { get; init; }

    public string? Surname { get; init; }

    public string? Department { get; init; }

    public string? Title { get; init; }

    public string? EmployeeId { get; init; }

    public string? ManagerDistinguishedName { get; init; }

    public string? Description { get; init; }

    public int? UserAccountControl { get; init; }

    public int? AdminCount { get; init; }

    public int? PrimaryGroupId { get; init; }

    public string? DnsHostName { get; init; }

    public string? OperatingSystem { get; init; }

    public string? OperatingSystemVersion { get; init; }

    public string? ManagedBy { get; init; }

    public DateTime? LastLogonTimestampUtc { get; init; }

    public DateTime? WhenCreatedUtc { get; init; }

    public DateTime? WhenChangedUtc { get; init; }

    public string? SourceAnchor { get; init; }

    public bool IsDisabled => UserAccountControl is { } uac && UserAccountControlExtensions.IsDisabled(uac);

    public string ParentDistinguishedName
    {
        get
        {
            var index = DistinguishedNameHelper.FirstUnescapedComma(DistinguishedName);
            return index < 0 ? string.Empty : DistinguishedName[(index + 1)..];
        }
    }
}

public sealed record DirectorySearch(
    Guid BaseOuGuid,
    DirectoryObjectKind Kind,
    string? Text,
    int MaxResults = 100);

public sealed record DomainControllerSelection(string ConnectorId, string DomainController, DateTime SelectedUtc, string VerifiedDomainDn, string VerifiedForestDn);

public sealed record MembershipResult(IReadOnlyCollection<string> GroupSids, IReadOnlyCollection<Guid> GroupGuids, bool Complete, string? IncompleteReason);

public sealed record AccountStateReading(Guid ObjectGuid, int UserAccountControl, string DomainController, DateTime ReadUtc)
{
    public bool IsDisabled => UserAccountControlExtensions.IsDisabled(UserAccountControl);
}

public sealed record DirectoryWriteResult(
    bool Succeeded,
    SafeErrorCategory ErrorCategory,
    string DomainController,
    int? PriorUserAccountControl,
    int? NewUserAccountControl,
    bool AlreadyInDesiredState,
    string Detail)
{
    public static DirectoryWriteResult Denied(SafeErrorCategory category, string detail) =>
        new(false, category, string.Empty, null, null, false, detail);
}

public sealed record ConnectorHealthReport(string ConnectorId, bool Healthy, string Detail, string? DomainController);

public static class DistinguishedNameHelper
{
    /// <summary>Index of the first RDN separator, honouring backslash escapes.</summary>
    public static int FirstUnescapedComma(string dn)
    {
        ArgumentNullException.ThrowIfNull(dn);
        for (var i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\')
            {
                i++;
                continue;
            }

            if (dn[i] == ',')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when <paramref name="dn"/> equals or is below <paramref name="containerDn"/>.</summary>
    public static bool IsWithin(string dn, string containerDn)
    {
        ArgumentNullException.ThrowIfNull(dn);
        ArgumentNullException.ThrowIfNull(containerDn);
        if (containerDn.Length == 0)
        {
            return false;
        }

        return string.Equals(dn, containerDn, StringComparison.OrdinalIgnoreCase)
            || dn.EndsWith("," + containerDn, StringComparison.OrdinalIgnoreCase);
    }
}
