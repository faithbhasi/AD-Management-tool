namespace Ilm.Domain.Directory;

/// <summary>What a configured directory connector may do.</summary>
public enum ConnectorMode
{
    Disabled = 0,
    ReadOnlyLegacy,
    ContainmentOnlyLegacy,
    ReadOnlyTarget,
    WriteTarget,
    MigrationException,
}

public enum ForestRole
{
    Target = 0,
    Legacy,
}

public enum DirectoryOperation
{
    Search = 0,
    ReadObject,
    ReadMembership,
    VerifyAccountState,
    DisableAccount,
    SetContainmentMarker,
    CreateAccount,
    EnableAccount,
    ResetPassword,
    ModifyAttributes,
    AddGroupMember,
    RemoveGroupMember,
    MoveObject,
    DeleteObject,
}

public enum DirectoryObjectKind
{
    User = 0,
    Computer,
    Group,
    OrganizationalUnit,
    ForeignSecurityPrincipal,
    ManagedServiceAccount,
    Other,
}
