using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;

namespace Ilm.Application.Directory;

/// <summary>Read operations for one configured forest or domain. All filtering is server-side.</summary>
public interface IDirectoryReader
{
    string ConnectorId { get; }

    DirectoryConnectorDefinition Definition { get; }

    Task<DirectoryObject?> GetByGuidAsync(Guid objectGuid, string? domainController, CancellationToken cancellationToken);

    Task<DirectoryObject?> GetBySidAsync(string objectSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryObject>> SearchAsync(DirectorySearch search, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryObject>> ListChildOrganizationalUnitsAsync(Guid parentOuGuid, CancellationToken cancellationToken);

    Task<MembershipResult> GetTransitiveGroupsAsync(Guid objectGuid, CancellationToken cancellationToken);

    /// <summary>
    /// Finds groups in this forest that (transitively) contain foreign security principals for any of the given SIDs.
    /// </summary>
    Task<MembershipResult> GetForeignPrincipalGroupsAsync(IReadOnlyCollection<string> foreignSids, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryObject>> FindUsersByAttributeAsync(string attribute, string value, CancellationToken cancellationToken);

    Task<DomainControllerSelection> SelectWritableDomainControllerAsync(CancellationToken cancellationToken);

    Task<AccountStateReading?> ReadAccountStateAsync(Guid objectGuid, string domainController, CancellationToken cancellationToken);

    Task<ConnectorHealthReport> CheckHealthAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Raw write channel. Only <see cref="GuardedDirectoryWriter"/> may use it; it enforces connector mode,
/// protection and DC pinning before any request reaches the directory.
/// </summary>
public interface IDirectoryWriteChannel
{
    string ConnectorId { get; }

    /// <summary>Sets ACCOUNTDISABLE with compare-and-swap on the given DC. Never clears any bit.</summary>
    Task<DirectoryWriteResult> SetAccountDisableBitAsync(Guid objectGuid, string domainController, int expectedCurrentValue, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> SetAttributeAsync(Guid objectGuid, string domainController, string attribute, string value, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> CreateDisabledUserAsync(NewUserRequest request, string domainController, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> SetPasswordAsync(Guid objectGuid, string domainController, ReadOnlyMemory<char> password, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> RequirePasswordChangeAsync(Guid objectGuid, string domainController, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> EnableAccountAsync(Guid objectGuid, string domainController, int expectedCurrentValue, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> AddGroupMemberAsync(Guid groupGuid, Guid memberGuid, string domainController, CancellationToken cancellationToken);

    Task<DirectoryWriteResult> RemoveGroupMemberAsync(Guid groupGuid, Guid memberGuid, string domainController, CancellationToken cancellationToken);
}

public sealed record NewUserRequest(
    Guid TargetOuGuid,
    string CommonName,
    string SamAccountName,
    string UserPrincipalName,
    string? GivenName,
    string? Surname,
    string? DisplayName,
    string? Mail,
    string? Department,
    string? EmployeeId);

public interface IDirectoryConnectorRegistry
{
    IReadOnlyList<string> ConnectorIds { get; }

    IDirectoryReader GetReader(string connectorId);

    IDirectoryWriteChannel GetWriteChannel(string connectorId);

    ForestRole RoleOf(string connectorId);

    /// <summary>
    /// A read-only view of a connector that may not be active yet (a configuration proposal or the bootstrap), so a
    /// proposed document is validated against the directories it describes rather than the active configuration.
    /// </summary>
    IDirectoryReader CreateReaderFor(Domain.Configuration.DirectoryConnectorDefinition definition);
}
