using Ilm.Application.Directory;
using Ilm.Application.Okta;
using Ilm.Domain.Directory;

namespace Ilm.Infrastructure.Directory;

/// <summary>Reads the AD object produced by Okta provisioning through the configured target connector.</summary>
public sealed class AdTargetStateReconciler(IDirectoryConnectorRegistry registry) : IAdTargetStateReconciler
{
    public async Task<AdTargetState> ReadAsync(string connectorId, AdTargetLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var reader = registry.GetReader(connectorId);
        IReadOnlyList<DirectoryObject> matches = [];
        if (lookup.ObjectGuid is { } guid)
        {
            var o = await reader.GetByGuidAsync(guid, null, cancellationToken);
            matches = o is null ? [] : [o];
        }
        else if (lookup.UserPrincipalName is not null)
        {
            matches = await reader.FindUsersByAttributeAsync("userPrincipalName", lookup.UserPrincipalName, cancellationToken);
        }

        if (matches.Count == 0 && lookup.SamAccountName is not null)
        {
            matches = await reader.FindUsersByAttributeAsync("sAMAccountName", lookup.SamAccountName, cancellationToken);
        }

        if (matches.Count == 0 && lookup.Mail is not null)
        {
            matches = await reader.FindUsersByAttributeAsync("mail", lookup.Mail, cancellationToken);
        }

        if (matches.Count == 0)
        {
            return new AdTargetState(false, 0, null, null, null, null, null, null, [], null, null, null, [], "No matching AD object.");
        }

        var m = matches[0];
        var groups = await reader.GetTransitiveGroupsAsync(m.ObjectGuid, cancellationToken);
        return new AdTargetState(
            true,
            matches.Count,
            m.ObjectGuid,
            m.DistinguishedName,
            m.ParentDistinguishedName,
            m.SamAccountName,
            m.UserPrincipalName,
            m.Mail,
            m.ProxyAddresses,
            m.Department,
            m.ManagerDistinguishedName,
            m.UserAccountControl is { } uac ? !UserAccountControlExtensions.IsDisabled(uac) : null,
            groups.GroupSids.ToList(),
            matches.Count > 1 ? "More than one AD object matched." : null);
    }
}

/// <summary>
/// Writes one probe attribute on a synthetic feasibility user. Refuses objects whose sAMAccountName or UPN
/// lacks the synthetic prefix, and never writes to legacy forests.
/// </summary>
public sealed class DirectoryFeasibilityProbe(IDirectoryConnectorRegistry registry) : IFeasibilityDirectoryProbe
{
    public async Task<bool> SetProbeAttributeAsync(string connectorId, Guid objectGuid, string requiredSamPrefix, string attribute, string value, CancellationToken cancellationToken)
    {
        if (registry.RoleOf(connectorId) != ForestRole.Target || string.IsNullOrWhiteSpace(requiredSamPrefix))
        {
            return false;
        }

        var reader = registry.GetReader(connectorId);
        var obj = await reader.GetByGuidAsync(objectGuid, null, cancellationToken);
        var synthetic = obj is not null
            && ((obj.SamAccountName?.StartsWith(requiredSamPrefix, StringComparison.OrdinalIgnoreCase) ?? false)
                || (obj.UserPrincipalName?.StartsWith(requiredSamPrefix, StringComparison.OrdinalIgnoreCase) ?? false));
        if (!synthetic)
        {
            return false;
        }

        var dc = await reader.SelectWritableDomainControllerAsync(cancellationToken);
        var result = await registry.GetWriteChannel(connectorId).SetAttributeAsync(objectGuid, dc.DomainController, attribute, value, cancellationToken);
        return result.Succeeded;
    }
}
