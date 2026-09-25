using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;
using Microsoft.Extensions.Logging;

namespace Ilm.Application.Protection;

/// <summary>
/// Gathers protection facts (own attributes, recursive membership, cross-forest membership through
/// foreign security principals) and evaluates them. Any failure yields Unknown, never Clear.
/// </summary>
public sealed partial class ProtectionService(
    IDirectoryConnectorRegistry registry,
    IActiveConfigurationProvider configuration,
    IPlatformIdentityProvider platformIdentities,
    ILogger<ProtectionService> logger)
{
    public async Task<ProtectionDecision> EvaluateAsync(string connectorId, Guid objectGuid, CancellationToken cancellationToken)
    {
        try
        {
            var config = await configuration.GetAsync(cancellationToken);
            var platform = await platformIdentities.GetAsync(cancellationToken);
            var reader = registry.GetReader(connectorId);
            var obj = await reader.GetByGuidAsync(objectGuid, null, cancellationToken);
            if (obj is null)
            {
                return ProtectionDecision.UnknownBecause("The object could not be found, so protection cannot be evaluated.");
            }

            return await EvaluateObjectAsync(obj, config, platform, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEvaluationFailed(logger, connectorId, objectGuid, ex.GetType().Name);
            return ProtectionDecision.UnknownBecause("Protection evaluation failed: " + ex.GetType().Name);
        }
    }

    public async Task<ProtectionDecision> EvaluateObjectAsync(DirectoryObject obj, ActiveConfiguration config, PlatformIdentities platform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(config);

        var incomplete = new List<string>();
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = new HashSet<Guid>();
        var membershipComplete = true;

        var own = await registry.GetReader(obj.ConnectorId).GetTransitiveGroupsAsync(obj.ObjectGuid, cancellationToken);
        sids.UnionWith(own.GroupSids);
        guids.UnionWith(own.GroupGuids);
        if (!own.Complete)
        {
            membershipComplete = false;
            incomplete.Add(own.IncompleteReason ?? "Own-forest membership incomplete.");
        }

        // Cross-forest: groups in other forests that contain an FSP for this object or any of its groups.
        var principalSids = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
        if (obj.ObjectSid is not null)
        {
            principalSids.Add(obj.ObjectSid);
        }

        foreach (var otherId in registry.ConnectorIds.Where(id => !string.Equals(id, obj.ConnectorId, StringComparison.OrdinalIgnoreCase)))
        {
            var definition = config.FindConnector(otherId);
            if (definition is null || definition.Mode == ConnectorMode.Disabled)
            {
                continue;
            }

            try
            {
                var foreign = await registry.GetReader(otherId).GetForeignPrincipalGroupsAsync(principalSids, cancellationToken);
                sids.UnionWith(foreign.GroupSids);
                guids.UnionWith(foreign.GroupGuids);
                if (!foreign.Complete)
                {
                    membershipComplete = false;
                    incomplete.Add($"Cross-forest membership in '{otherId}' incomplete: {foreign.IncompleteReason}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                membershipComplete = false;
                incomplete.Add($"Cross-forest membership in '{otherId}' could not be read ({ex.GetType().Name}).");
            }
        }

        var facts = new DirectoryObjectFacts
        {
            ConnectorId = obj.ConnectorId,
            ForestId = obj.ForestId,
            ObjectGuid = obj.ObjectGuid,
            ObjectSid = obj.ObjectSid,
            DistinguishedName = obj.DistinguishedName,
            SamAccountName = obj.SamAccountName,
            DnsHostName = obj.DnsHostName,
            ObjectClasses = obj.ObjectClasses,
            UserAccountControl = obj.UserAccountControl,
            AdminCount = obj.AdminCount,
            PrimaryGroupId = obj.PrimaryGroupId,
            AttributesComplete = obj.UserAccountControl is not null && obj.ObjectClasses.Count > 0,
            TransitiveGroupSids = sids,
            TransitiveGroupGuids = guids,
            MembershipComplete = membershipComplete,
            IncompleteReasons = incomplete,
        };

        return ProtectionEvaluator.Evaluate(facts, config.ProtectionAdditions, platform);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Protection evaluation failed for {ConnectorId}/{ObjectGuid}: {ErrorType}. Treated as Unknown.")]
    private static partial void LogEvaluationFailed(ILogger logger, string connectorId, Guid objectGuid, string errorType);
}
