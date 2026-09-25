using Ilm.Application.Directory;
using Ilm.Domain.Authority;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Protection;

namespace Ilm.Modules.ReadOnly;

public sealed record PermittedAction(string Name, bool Available, string Reason);

public sealed record ScopeRoot(string ScopeId, string ConnectorId, Guid OuGuid, string DisplayName, string? DistinguishedName);

public sealed record DirectoryObjectView(
    DirectoryObject Object,
    ProtectionDecision Protection,
    ScopeDecision Scope,
    Person? Person,
    ExternalIdentity? Identity,
    IReadOnlyList<AuthorityDecision> LeaverAuthority,
    IReadOnlyList<AuthorityDecision> JoinerAuthority,
    IReadOnlyList<PermittedAction> Actions,
    IReadOnlyList<string> Warnings);

public sealed record IdentityView(
    ExternalIdentity Identity,
    IdentityLink? Link,
    ProtectionDecision? Protection,
    IReadOnlyList<AuthorityDecision> LeaverAuthority,
    string? CurrentDistinguishedName);

public sealed record PersonView(
    Person Person,
    IReadOnlyList<IdentityView> Identities,
    IReadOnlyList<IdentityLink> Links,
    MigrationState? Migration,
    LeaverRequest? ActiveLeaver,
    IReadOnlyList<PermittedAction> Actions);
