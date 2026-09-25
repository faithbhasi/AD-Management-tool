using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Text;
using Ilm.Application.Directory;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Microsoft.Extensions.Logging;

namespace Ilm.Infrastructure.Directory.Ldap;

/// <summary>
/// Active Directory connector over System.DirectoryServices.Protocols. Objects are addressed by
/// &lt;GUID=...&gt; or &lt;SID=...&gt; extended DNs; filters are built only by <see cref="LdapFilter"/>; only
/// allowlisted attributes are requested. Every write is issued to the DC chosen for the workflow.
/// UNVERIFIED against a real domain in this repository (see ACTIVE-DIRECTORY.md lab procedure).
/// </summary>
public sealed partial class LdapDirectoryConnector(
    DirectoryConnectorDefinition definition,
    LdapConnectionFactory connections,
    ILogger<LdapDirectoryConnector> logger) : IDirectoryReader, IDirectoryWriteChannel
{
    private const int PageSize = 200;
    private static readonly HashSet<string> LookupAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sAMAccountName", "userPrincipalName", "mail", "proxyAddresses", "employeeID",
    };

    public string ConnectorId => definition.Id;

    public DirectoryConnectorDefinition Definition => definition;

    private string DomainDn => LdapDn.FromDnsName(definition.DomainDnsName);

    private string ForestDn => LdapDn.FromDnsName(definition.ForestDnsName);

    private string DefaultDc => definition.DomainControllers.FirstOrDefault() ?? throw new InvalidOperationException($"Connector '{definition.Id}' has no domain controllers.");

    public async Task<DirectoryObject?> GetByGuidAsync(Guid objectGuid, string? domainController, CancellationToken cancellationToken)
    {
        var entries = await SearchAsync(domainController ?? DefaultDc, LdapDn.GuidReference(objectGuid), "(objectClass=*)", SearchScope.Base, SafeAttributeAllowlist.Object, 1, cancellationToken);
        return entries.Count == 0 ? null : Map(entries[0]);
    }

    public async Task<DirectoryObject?> GetBySidAsync(string objectSid, CancellationToken cancellationToken)
    {
        var entries = await SearchAsync(DefaultDc, LdapDn.SidReference(objectSid), "(objectClass=*)", SearchScope.Base, SafeAttributeAllowlist.Object, 1, cancellationToken);
        return entries.Count == 0 ? null : Map(entries[0]);
    }

    public async Task<IReadOnlyList<DirectoryObject>> SearchAsync(DirectorySearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var kindFilter = search.Kind switch
        {
            DirectoryObjectKind.User => LdapFilter.And("(objectCategory=person)", "(objectClass=user)"),
            DirectoryObjectKind.Computer => "(objectClass=computer)",
            DirectoryObjectKind.Group => "(objectClass=group)",
            DirectoryObjectKind.OrganizationalUnit => "(objectClass=organizationalUnit)",
            _ => throw new ArgumentOutOfRangeException(nameof(search), "Unsupported search kind."),
        };
        var filter = string.IsNullOrWhiteSpace(search.Text)
            ? kindFilter
            : LdapFilter.And(kindFilter, LdapFilter.Or(
                LdapFilter.Prefix("sAMAccountName", search.Text.Trim()),
                LdapFilter.Prefix("displayName", search.Text.Trim()),
                LdapFilter.Prefix("mail", search.Text.Trim()),
                LdapFilter.Prefix("userPrincipalName", search.Text.Trim()),
                LdapFilter.Prefix("cn", search.Text.Trim())));
        var entries = await SearchAsync(DefaultDc, LdapDn.GuidReference(search.BaseOuGuid), filter, SearchScope.Subtree, SafeAttributeAllowlist.Object, Math.Clamp(search.MaxResults, 1, 200), cancellationToken);
        return entries.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<DirectoryObject>> ListChildOrganizationalUnitsAsync(Guid parentOuGuid, CancellationToken cancellationToken)
    {
        var entries = await SearchAsync(DefaultDc, LdapDn.GuidReference(parentOuGuid), "(objectClass=organizationalUnit)", SearchScope.OneLevel, SafeAttributeAllowlist.Object, 500, cancellationToken);
        return entries.Select(Map).ToList();
    }

    public async Task<MembershipResult> GetTransitiveGroupsAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        try
        {
            var obj = await GetByGuidAsync(objectGuid, null, cancellationToken);
            if (obj is null)
            {
                return new MembershipResult([], [], false, "Object not found.");
            }

            var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = new HashSet<Guid>();
            await AddInChainGroupsAsync(obj.DistinguishedName, sids, guids, cancellationToken);

            // primaryGroupID never appears in member; include the primary group and its own chain.
            if (obj.PrimaryGroupId is { } rid && obj.ObjectSid is { } sid)
            {
                var primarySid = sid[..sid.LastIndexOf('-')] + "-" + rid.ToString(CultureInfo.InvariantCulture);
                var primary = await GetBySidAsync(primarySid, cancellationToken);
                if (primary is not null)
                {
                    sids.Add(primarySid);
                    guids.Add(primary.ObjectGuid);
                    await AddInChainGroupsAsync(primary.DistinguishedName, sids, guids, cancellationToken);
                }
            }

            return new MembershipResult(sids, guids, true, null);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException or TimeoutException)
        {
            return new MembershipResult([], [], false, $"Membership query failed ({ex.GetType().Name}).");
        }
    }

    public async Task<MembershipResult> GetForeignPrincipalGroupsAsync(IReadOnlyCollection<string> foreignSids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(foreignSids);
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = new HashSet<Guid>();
        try
        {
            foreach (var foreignSid in foreignSids)
            {
                var fspDn = $"CN={LdapDn.EscapeRdnValue(foreignSid)},CN=ForeignSecurityPrincipals,{DomainDn}";
                var exists = await SearchAsync(DefaultDc, fspDn, "(objectClass=foreignSecurityPrincipal)", SearchScope.Base, ["objectGUID"], 1, cancellationToken, tolerateNoSuchObject: true);
                if (exists.Count > 0)
                {
                    await AddInChainGroupsAsync(fspDn, sids, guids, cancellationToken);
                }
            }

            return new MembershipResult(sids, guids, true, null);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException or TimeoutException)
        {
            return new MembershipResult(sids, guids, false, $"Foreign principal query failed ({ex.GetType().Name}).");
        }
    }

    public async Task<IReadOnlyList<DirectoryObject>> FindUsersByAttributeAsync(string attribute, string value, CancellationToken cancellationToken)
    {
        if (!LookupAttributes.Contains(attribute))
        {
            throw new ArgumentException($"Attribute '{attribute}' is not an allowed lookup attribute.", nameof(attribute));
        }

        var filter = LdapFilter.And("(objectCategory=person)", "(objectClass=user)", LdapFilter.Equal(attribute, value));
        var entries = await SearchAsync(DefaultDc, DomainDn, filter, SearchScope.Subtree, SafeAttributeAllowlist.Object, 10, cancellationToken);
        return entries.Select(Map).ToList();
    }

    /// <summary>
    /// Picks the first configured DC that answers, is writable (not an RODC) and belongs to the expected domain and forest.
    /// </summary>
    public async Task<DomainControllerSelection> SelectWritableDomainControllerAsync(CancellationToken cancellationToken)
    {
        foreach (var dc in definition.DomainControllers)
        {
            try
            {
                var root = await SearchAsync(dc, string.Empty, "(objectClass=*)", SearchScope.Base, ["defaultNamingContext", "rootDomainNamingContext", "dsServiceName", "dnsHostName"], 1, cancellationToken);
                if (root.Count == 0)
                {
                    continue;
                }

                var domainDn = First(root[0], "defaultNamingContext");
                var forestDn = First(root[0], "rootDomainNamingContext");
                if (!string.Equals(domainDn, DomainDn, StringComparison.OrdinalIgnoreCase) || !string.Equals(forestDn, ForestDn, StringComparison.OrdinalIgnoreCase))
                {
                    LogTopologyMismatch(logger, definition.Id, dc);
                    continue;
                }

                var dsa = First(root[0], "dsServiceName");
                if (dsa is not null)
                {
                    var ntds = await SearchAsync(dc, dsa, "(objectClass=*)", SearchScope.Base, ["msDS-isRODC"], 1, cancellationToken);
                    if (ntds.Count > 0 && string.Equals(First(ntds[0], "msDS-isRODC"), "TRUE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                return new DomainControllerSelection(definition.Id, dc, DateTime.UtcNow, domainDn!, forestDn!);
            }
            catch (Exception ex) when (ex is LdapException or DirectoryOperationException or TimeoutException)
            {
                LogDcUnavailable(logger, definition.Id, dc, ex.GetType().Name);
            }
        }

        throw new IOException($"No writable domain controller in '{definition.Id}' passed topology verification.");
    }

    public async Task<AccountStateReading?> ReadAccountStateAsync(Guid objectGuid, string domainController, CancellationToken cancellationToken)
    {
        var entries = await SearchAsync(domainController, LdapDn.GuidReference(objectGuid), "(objectClass=*)", SearchScope.Base, ["userAccountControl"], 1, cancellationToken);
        if (entries.Count == 0 || First(entries[0], "userAccountControl") is not { } raw)
        {
            return null;
        }

        return new AccountStateReading(objectGuid, int.Parse(raw, CultureInfo.InvariantCulture), domainController, DateTime.UtcNow);
    }

    public async Task<ConnectorHealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dc = await SelectWritableDomainControllerAsync(cancellationToken);
            return new ConnectorHealthReport(definition.Id, true, "Writable DC verified.", dc.DomainController);
        }
        catch (IOException ex)
        {
            return new ConnectorHealthReport(definition.Id, false, ex.Message, null);
        }
    }

    public Task<DirectoryWriteResult> SetAccountDisableBitAsync(Guid objectGuid, string domainController, int expectedCurrentValue, CancellationToken cancellationToken)
    {
        var newValue = UserAccountControlExtensions.WithDisableBitSet(expectedCurrentValue);
        return CompareAndSwapUacAsync(objectGuid, domainController, expectedCurrentValue, newValue, cancellationToken);
    }

    public Task<DirectoryWriteResult> EnableAccountAsync(Guid objectGuid, string domainController, int expectedCurrentValue, CancellationToken cancellationToken)
    {
        var newValue = expectedCurrentValue & ~(int)UserAccountControl.AccountDisable & ~(int)UserAccountControl.PasswordNotRequired;
        return CompareAndSwapUacAsync(objectGuid, domainController, expectedCurrentValue, newValue, cancellationToken);
    }

    public Task<DirectoryWriteResult> SetAttributeAsync(Guid objectGuid, string domainController, string attribute, string value, CancellationToken cancellationToken)
    {
        if (SafeAttributeAllowlist.Forbidden.Contains(attribute, StringComparer.OrdinalIgnoreCase) || string.Equals(attribute, "userAccountControl", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("This attribute cannot be written through SetAttribute.", nameof(attribute));
        }

        var mod = new DirectoryAttributeModification { Name = attribute, Operation = DirectoryAttributeOperation.Replace };
        mod.Add(value);
        return ModifyAsync(domainController, LdapDn.GuidReference(objectGuid), [mod], null, null, cancellationToken);
    }

    public async Task<DirectoryWriteResult> CreateDisabledUserAsync(NewUserRequest request, string domainController, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ou = await GetByGuidAsync(request.TargetOuGuid, domainController, cancellationToken);
        if (ou is null || ou.Kind != DirectoryObjectKind.OrganizationalUnit)
        {
            return new DirectoryWriteResult(false, SafeErrorCategory.NotFound, domainController, null, null, false, "Target OU not found.");
        }

        var dn = $"CN={LdapDn.EscapeRdnValue(request.CommonName)},{ou.DistinguishedName}";
        var attributes = new List<DirectoryAttribute>
        {
            new("objectClass", "user"),
            new("sAMAccountName", request.SamAccountName),
            new("userPrincipalName", request.UserPrincipalName),
            new("userAccountControl", ((int)(UserAccountControl.NormalAccount | UserAccountControl.AccountDisable)).ToString(CultureInfo.InvariantCulture)),
        };
        AddIfPresent(attributes, "givenName", request.GivenName);
        AddIfPresent(attributes, "sn", request.Surname);
        AddIfPresent(attributes, "displayName", request.DisplayName);
        AddIfPresent(attributes, "mail", request.Mail);
        AddIfPresent(attributes, "department", request.Department);
        AddIfPresent(attributes, "employeeID", request.EmployeeId);
        return await SendWriteAsync(domainController, new AddRequest(dn, [.. attributes]), null, null, cancellationToken);
    }

    public Task<DirectoryWriteResult> SetPasswordAsync(Guid objectGuid, string domainController, ReadOnlyMemory<char> password, CancellationToken cancellationToken)
    {
        // unicodePwd requires an encrypted channel (LDAPS or Kerberos sealing); the factory guarantees one.
        var quoted = new char[password.Length + 2];
        quoted[0] = '"';
        password.Span.CopyTo(quoted.AsSpan(1));
        quoted[^1] = '"';
        var bytes = Encoding.Unicode.GetBytes(quoted);
        Array.Clear(quoted);
        var mod = new DirectoryAttributeModification { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Replace };
        mod.Add(bytes);
        return ModifyAsync(domainController, LdapDn.GuidReference(objectGuid), [mod], null, null, cancellationToken, () => Array.Clear(bytes));
    }

    public Task<DirectoryWriteResult> RequirePasswordChangeAsync(Guid objectGuid, string domainController, CancellationToken cancellationToken)
    {
        var mod = new DirectoryAttributeModification { Name = "pwdLastSet", Operation = DirectoryAttributeOperation.Replace };
        mod.Add("0");
        return ModifyAsync(domainController, LdapDn.GuidReference(objectGuid), [mod], null, null, cancellationToken);
    }

    public async Task<DirectoryWriteResult> AddGroupMemberAsync(Guid groupGuid, Guid memberGuid, string domainController, CancellationToken cancellationToken)
    {
        var member = await GetByGuidAsync(memberGuid, domainController, cancellationToken);
        if (member is null)
        {
            return new DirectoryWriteResult(false, SafeErrorCategory.NotFound, domainController, null, null, false, "Member not found.");
        }

        var mod = new DirectoryAttributeModification { Name = "member", Operation = DirectoryAttributeOperation.Add };
        mod.Add(member.DistinguishedName);
        return await ModifyAsync(domainController, LdapDn.GuidReference(groupGuid), [mod], null, null, cancellationToken);
    }

    public async Task<DirectoryWriteResult> RemoveGroupMemberAsync(Guid groupGuid, Guid memberGuid, string domainController, CancellationToken cancellationToken)
    {
        var member = await GetByGuidAsync(memberGuid, domainController, cancellationToken);
        if (member is null)
        {
            return new DirectoryWriteResult(false, SafeErrorCategory.NotFound, domainController, null, null, false, "Member not found.");
        }

        var mod = new DirectoryAttributeModification { Name = "member", Operation = DirectoryAttributeOperation.Delete };
        mod.Add(member.DistinguishedName);
        return await ModifyAsync(domainController, LdapDn.GuidReference(groupGuid), [mod], null, null, cancellationToken);
    }

    /// <summary>
    /// Delete-old/add-new in one modify: AD fails the request with noSuchAttribute if the value changed
    /// since it was read, which gives compare-and-swap semantics.
    /// </summary>
    private Task<DirectoryWriteResult> CompareAndSwapUacAsync(Guid objectGuid, string domainController, int expected, int newValue, CancellationToken cancellationToken)
    {
        var delete = new DirectoryAttributeModification { Name = "userAccountControl", Operation = DirectoryAttributeOperation.Delete };
        delete.Add(expected.ToString(CultureInfo.InvariantCulture));
        var add = new DirectoryAttributeModification { Name = "userAccountControl", Operation = DirectoryAttributeOperation.Add };
        add.Add(newValue.ToString(CultureInfo.InvariantCulture));
        return ModifyAsync(domainController, LdapDn.GuidReference(objectGuid), [delete, add], expected, newValue, cancellationToken);
    }

    private Task<DirectoryWriteResult> ModifyAsync(string dc, string dn, DirectoryAttributeModification[] mods, int? prior, int? next, CancellationToken cancellationToken, Action? cleanup = null) =>
        SendWriteAsync(dc, new ModifyRequest(dn, mods), prior, next, cancellationToken, cleanup);

    private async Task<DirectoryWriteResult> SendWriteAsync(string dc, DirectoryRequest request, int? prior, int? next, CancellationToken cancellationToken, Action? cleanup = null)
    {
        using var connection = connections.Create(definition, dc);
        try
        {
            await SendAsync(connection, request, cancellationToken);
            return new DirectoryWriteResult(true, SafeErrorCategory.None, dc, prior, next, false, "Written.");
        }
        catch (DirectoryOperationException ex)
        {
            var category = LdapErrorClassifier.Classify(ex.Response?.ResultCode ?? ResultCode.Other, ex.Response?.ErrorMessage);
            LogWriteFailed(logger, definition.Id, dc, category);
            return new DirectoryWriteResult(false, category, dc, prior, null, false, $"Directory refused the change ({category}).");
        }
        catch (LdapException ex)
        {
            var category = LdapErrorClassifier.Classify((ResultCode)ex.ErrorCode, ex.ServerErrorMessage);
            LogWriteFailed(logger, definition.Id, dc, category);
            return new DirectoryWriteResult(false, category, dc, prior, null, false, $"LDAP error ({category}).");
        }
        finally
        {
            cleanup?.Invoke();
        }
    }

    private async Task<IReadOnlyList<SearchResultEntry>> SearchAsync(string dc, string baseDn, string filter, SearchScope scope, string[] attributes, int maxResults, CancellationToken cancellationToken, bool tolerateNoSuchObject = false)
    {
        using var connection = connections.Create(definition, dc);
        var results = new List<SearchResultEntry>();
        var paging = new PageResultRequestControl(Math.Min(PageSize, maxResults));
        var request = new SearchRequest(baseDn, filter, scope, attributes) { SizeLimit = maxResults };
        if (scope != SearchScope.Base)
        {
            request.Controls.Add(paging);
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = (SearchResponse)await SendAsync(connection, request, cancellationToken);
                results.AddRange(response.Entries.Cast<SearchResultEntry>());
                var pageResponse = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                if (scope == SearchScope.Base || pageResponse is null || pageResponse.Cookie.Length == 0 || results.Count >= maxResults)
                {
                    break;
                }

                paging.Cookie = pageResponse.Cookie;
            }
        }
        catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject && (tolerateNoSuchObject || scope == SearchScope.Base))
        {
            return [];
        }
        catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.SizeLimitExceeded)
        {
            return results;
        }

        return results.Take(maxResults).ToList();
    }

    private static Task<DirectoryResponse> SendAsync(LdapConnection connection, DirectoryRequest request, CancellationToken cancellationToken) =>
        Task.Factory.FromAsync(
            (callback, state) => connection.BeginSendRequest(request, connection.Timeout, PartialResultProcessing.NoPartialResultSupport, callback, state),
            connection.EndSendRequest,
            null).WaitAsync(connection.Timeout + TimeSpan.FromSeconds(5), cancellationToken);

    private async Task AddInChainGroupsAsync(string memberDn, HashSet<string> sids, HashSet<Guid> guids, CancellationToken cancellationToken)
    {
        var groups = await SearchAsync(DefaultDc, DomainDn, LdapFilter.GroupsContainingInChain(memberDn), SearchScope.Subtree, ["objectSid", "objectGUID"], 5000, cancellationToken);
        foreach (var g in groups)
        {
            if (Binary(g, "objectSid") is { } sid)
            {
                sids.Add(SidConverter.ToSddlString(sid));
            }

            if (Binary(g, "objectGUID") is { } guid)
            {
                guids.Add(new Guid(guid));
            }
        }
    }

    private DirectoryObject Map(SearchResultEntry e)
    {
        var classes = All(e, "objectClass");
        var uac = First(e, "userAccountControl");
        return new DirectoryObject
        {
            ConnectorId = definition.Id,
            ForestId = definition.ForestDnsName,
            ObjectGuid = Binary(e, "objectGUID") is { } g ? new Guid(g) : Guid.Empty,
            ObjectSid = Binary(e, "objectSid") is { } s ? SidConverter.ToSddlString(s) : null,
            DistinguishedName = First(e, "distinguishedName") ?? e.DistinguishedName,
            Kind = KindOf(classes),
            ObjectClasses = classes,
            Name = First(e, "name"),
            SamAccountName = First(e, "sAMAccountName"),
            UserPrincipalName = First(e, "userPrincipalName"),
            Mail = First(e, "mail"),
            ProxyAddresses = All(e, "proxyAddresses"),
            DisplayName = First(e, "displayName"),
            GivenName = First(e, "givenName"),
            Surname = First(e, "sn"),
            Department = First(e, "department"),
            Title = First(e, "title"),
            EmployeeId = First(e, "employeeID"),
            ManagerDistinguishedName = First(e, "manager"),
            Description = First(e, "description"),
            UserAccountControl = uac is null ? null : int.Parse(uac, CultureInfo.InvariantCulture),
            AdminCount = First(e, "adminCount") is { } ac ? int.Parse(ac, CultureInfo.InvariantCulture) : null,
            PrimaryGroupId = First(e, "primaryGroupID") is { } pg ? int.Parse(pg, CultureInfo.InvariantCulture) : null,
            DnsHostName = First(e, "dNSHostName"),
            OperatingSystem = First(e, "operatingSystem"),
            OperatingSystemVersion = First(e, "operatingSystemVersion"),
            ManagedBy = First(e, "managedBy"),
            LastLogonTimestampUtc = FileTime(First(e, "lastLogonTimestamp")),
            WhenCreatedUtc = GeneralizedTime(First(e, "whenCreated")),
            WhenChangedUtc = GeneralizedTime(First(e, "whenChanged")),
            SourceAnchor = Binary(e, "mS-DS-ConsistencyGuid") is { } anchor ? Convert.ToBase64String(anchor) : null,
        };
    }

    internal static DirectoryObjectKind KindOf(IReadOnlyList<string> classes)
    {
        bool Has(string c) => classes.Contains(c, StringComparer.OrdinalIgnoreCase);
        if (Has("msDS-GroupManagedServiceAccount") || Has("msDS-ManagedServiceAccount"))
        {
            return DirectoryObjectKind.ManagedServiceAccount;
        }

        if (Has("computer"))
        {
            return DirectoryObjectKind.Computer;
        }

        if (Has("user"))
        {
            return DirectoryObjectKind.User;
        }

        if (Has("group"))
        {
            return DirectoryObjectKind.Group;
        }

        if (Has("organizationalUnit"))
        {
            return DirectoryObjectKind.OrganizationalUnit;
        }

        return Has("foreignSecurityPrincipal") ? DirectoryObjectKind.ForeignSecurityPrincipal : DirectoryObjectKind.Other;
    }

    internal static DateTime? FileTime(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ft) && ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc()
            ? DateTime.FromFileTimeUtc(ft)
            : null;

    internal static DateTime? GeneralizedTime(string? value) =>
        value is not null && DateTime.TryParseExact(value, ["yyyyMMddHHmmss.0'Z'", "yyyyMMddHHmmss'Z'"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;

    private static void AddIfPresent(List<DirectoryAttribute> attributes, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes.Add(new DirectoryAttribute(name, value));
        }
    }

    private static string? First(SearchResultEntry e, string attribute) =>
        e.Attributes.Contains(attribute) ? e.Attributes[attribute].GetValues(typeof(string)).Cast<string>().FirstOrDefault() : null;

    private static List<string> All(SearchResultEntry e, string attribute) =>
        e.Attributes.Contains(attribute) ? e.Attributes[attribute].GetValues(typeof(string)).Cast<string>().ToList() : [];

    private static byte[]? Binary(SearchResultEntry e, string attribute) =>
        e.Attributes.Contains(attribute) ? e.Attributes[attribute].GetValues(typeof(byte[])).Cast<byte[]>().FirstOrDefault() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector {ConnectorId}: DC {Dc} does not belong to the configured domain/forest; skipped.")]
    private static partial void LogTopologyMismatch(ILogger logger, string connectorId, string dc);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector {ConnectorId}: DC {Dc} unavailable ({ErrorType}).")]
    private static partial void LogDcUnavailable(ILogger logger, string connectorId, string dc, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector {ConnectorId}: write on {Dc} failed ({Category}).")]
    private static partial void LogWriteFailed(ILogger logger, string connectorId, string dc, SafeErrorCategory category);
}
