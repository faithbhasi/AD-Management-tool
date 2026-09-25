using Ilm.Application.Configuration;
using Ilm.Application.Protection;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;

namespace Ilm.Application.Directory;

public sealed record GuardedWriteOutcome(DirectoryWriteResult Result, ProtectionDecision Protection, string ModeDecision);

/// <summary>
/// The only path to <see cref="IDirectoryWriteChannel"/>. Before every write it checks the connector mode,
/// re-evaluates protection at commit time on the pinned DC's view, and refuses anything that is not conclusively clear.
/// </summary>
public sealed class GuardedDirectoryWriter(
    IDirectoryConnectorRegistry registry,
    IActiveConfigurationProvider configuration,
    ProtectionService protection,
    TimeProvider time)
{
    public async Task<GuardedWriteOutcome> DisableAccountAsync(string connectorId, Guid objectGuid, string domainController, bool requireStandardUser, CancellationToken cancellationToken)
    {
        var gate = await GateAsync(connectorId, objectGuid, DirectoryOperation.DisableAccount, cancellationToken);
        if (gate.Denied is not null)
        {
            return gate.Denied;
        }

        var reader = registry.GetReader(connectorId);
        var obj = await reader.GetByGuidAsync(objectGuid, domainController, cancellationToken);
        if (obj is null)
        {
            return Deny(SafeErrorCategory.NotFound, "The account was not found on the selected domain controller.", gate.Protection, gate.ModeDecision);
        }

        if (requireStandardUser && obj.Kind != DirectoryObjectKind.User)
        {
            return Deny(SafeErrorCategory.ConnectorModeDenied, "Containment-only connectors may disable user accounts only.", gate.Protection, gate.ModeDecision);
        }

        if (obj.UserAccountControl is not { } current)
        {
            return Deny(SafeErrorCategory.VerificationFailed, "userAccountControl could not be read.", gate.Protection, gate.ModeDecision);
        }

        if (UserAccountControlExtensions.IsDisabled(current))
        {
            return new GuardedWriteOutcome(
                new DirectoryWriteResult(true, SafeErrorCategory.None, domainController, current, current, AlreadyInDesiredState: true, "Account was already disabled."),
                gate.Protection,
                gate.ModeDecision);
        }

        var result = await registry.GetWriteChannel(connectorId).SetAccountDisableBitAsync(objectGuid, domainController, current, cancellationToken);
        return new GuardedWriteOutcome(result, gate.Protection, gate.ModeDecision);
    }

    public async Task<GuardedWriteOutcome> SetContainmentMarkerAsync(string connectorId, Guid objectGuid, string domainController, string value, CancellationToken cancellationToken)
    {
        var gate = await GateAsync(connectorId, objectGuid, DirectoryOperation.SetContainmentMarker, cancellationToken);
        if (gate.Denied is not null)
        {
            return gate.Denied;
        }

        var definition = (await configuration.GetAsync(cancellationToken)).FindConnector(connectorId)!;
        var result = await registry.GetWriteChannel(connectorId)
            .SetAttributeAsync(objectGuid, domainController, definition.ContainmentMarkerAttribute!, value, cancellationToken);
        return new GuardedWriteOutcome(result, gate.Protection, gate.ModeDecision);
    }

    /// <summary>Checks mode and protection for any operation. Used by strategies before planning writes.</summary>
    public async Task<(string? Denial, ProtectionDecision Protection)> PreflightAsync(string connectorId, Guid objectGuid, DirectoryOperation operation, CancellationToken cancellationToken)
    {
        var gate = await GateAsync(connectorId, objectGuid, operation, cancellationToken);
        return (gate.Denied?.Result.Detail, gate.Protection);
    }

    public async Task<string?> ModeDenialAsync(string connectorId, DirectoryOperation operation, CancellationToken cancellationToken)
    {
        var config = await configuration.GetAsync(cancellationToken);
        var definition = config.FindConnector(connectorId);
        if (definition is null)
        {
            return $"Connector '{connectorId}' is not configured.";
        }

        return ConnectorModePolicy.Deny(ToContext(definition), operation);
    }

    private async Task<Gate> GateAsync(string connectorId, Guid objectGuid, DirectoryOperation operation, CancellationToken cancellationToken)
    {
        var modeDenial = await ModeDenialAsync(connectorId, operation, cancellationToken);
        var modeDecision = modeDenial is null ? $"{operation}:Permitted" : $"{operation}:Denied";
        if (modeDenial is not null)
        {
            var unknown = ProtectionDecision.UnknownBecause("Not evaluated: connector mode denied the operation.");
            return new Gate(Deny(SafeErrorCategory.ConnectorModeDenied, modeDenial, unknown, modeDecision), unknown, modeDecision);
        }

        // Commit-time protection recheck. Anything other than Clear is refused.
        var decision = await protection.EvaluateAsync(connectorId, objectGuid, cancellationToken);
        if (decision.Status == ProtectionStatus.Protected)
        {
            return new Gate(Deny(SafeErrorCategory.ProtectedObject, "The object is protected (Tier 0 or Tier 0-adjacent).", decision, modeDecision), decision, modeDecision);
        }

        if (decision.Status == ProtectionStatus.Unknown)
        {
            return new Gate(Deny(SafeErrorCategory.ProtectionUnknown, "Protection could not be conclusively evaluated.", decision, modeDecision), decision, modeDecision);
        }

        return new Gate(null, decision, modeDecision);
    }

    private ConnectorModeContext ToContext(Domain.Configuration.DirectoryConnectorDefinition d) => new(
        d.Mode,
        d.Role,
        d.ContainmentMarkerApproved,
        d.MigrationExceptionExpiresUtc,
        d.MigrationExceptionOperations.ToHashSet(),
        time.GetUtcNow().UtcDateTime);

    private static GuardedWriteOutcome Deny(SafeErrorCategory category, string detail, ProtectionDecision protection, string modeDecision) =>
        new(DirectoryWriteResult.Denied(category, detail), protection, modeDecision);

    private sealed record Gate(GuardedWriteOutcome? Denied, ProtectionDecision Protection, string ModeDecision);
}
