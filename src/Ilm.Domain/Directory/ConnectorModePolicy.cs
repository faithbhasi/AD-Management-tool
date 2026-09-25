namespace Ilm.Domain.Directory;

/// <summary>
/// The single source of truth for which directory operations each connector mode allows.
/// Every write in ILM passes through this check before an LDAP request is built.
/// </summary>
public static class ConnectorModePolicy
{
    private static readonly HashSet<DirectoryOperation> ReadOperations =
    [
        DirectoryOperation.Search,
        DirectoryOperation.ReadObject,
        DirectoryOperation.ReadMembership,
        DirectoryOperation.VerifyAccountState,
    ];

    public static bool IsReadOperation(DirectoryOperation operation) => ReadOperations.Contains(operation);

    public static bool IsModeValidForRole(ConnectorMode mode, ForestRole role) => role switch
    {
        ForestRole.Legacy => mode is ConnectorMode.Disabled or ConnectorMode.ReadOnlyLegacy
            or ConnectorMode.ContainmentOnlyLegacy or ConnectorMode.MigrationException,
        ForestRole.Target => mode is ConnectorMode.Disabled or ConnectorMode.ReadOnlyTarget
            or ConnectorMode.WriteTarget or ConnectorMode.MigrationException,
        _ => false,
    };

    /// <summary>Returns null when permitted, otherwise the reason it is denied.</summary>
    public static string? Deny(ConnectorModeContext context, DirectoryOperation operation)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsModeValidForRole(context.Mode, context.Role))
        {
            return $"Connector mode {context.Mode} is not valid for a {context.Role} forest.";
        }

        // Deletion is never automated, in any mode.
        if (operation == DirectoryOperation.DeleteObject)
        {
            return "Automatic deletion is never permitted.";
        }

        switch (context.Mode)
        {
            case ConnectorMode.Disabled:
                return "The connector is disabled.";

            case ConnectorMode.ReadOnlyLegacy:
            case ConnectorMode.ReadOnlyTarget:
                return ReadOperations.Contains(operation) ? null : $"{context.Mode} permits search and reconciliation only.";

            case ConnectorMode.ContainmentOnlyLegacy:
                if (ReadOperations.Contains(operation) || operation == DirectoryOperation.DisableAccount)
                {
                    return null;
                }

                if (operation == DirectoryOperation.SetContainmentMarker)
                {
                    return context.ContainmentMarkerApproved ? null : "The containment marker has not been separately approved.";
                }

                return "ContainmentOnlyLegacy permits disable, verify and an approved containment marker only.";

            case ConnectorMode.WriteTarget:
                return null;

            case ConnectorMode.MigrationException:
                if (context.MigrationExceptionExpiresUtc is null || context.MigrationExceptionExpiresUtc <= context.NowUtc)
                {
                    return "The migration exception has no expiry or has expired.";
                }

                return ReadOperations.Contains(operation) || context.MigrationExceptionOperations.Contains(operation)
                    ? null
                    : "The operation is not in the approved migration exception.";

            default:
                return "Unknown connector mode.";
        }
    }
}

public sealed record ConnectorModeContext(
    ConnectorMode Mode,
    ForestRole Role,
    bool ContainmentMarkerApproved,
    DateTime? MigrationExceptionExpiresUtc,
    IReadOnlySet<DirectoryOperation> MigrationExceptionOperations,
    DateTime NowUtc);
