namespace Ilm.Domain.Authority;

/// <summary>
/// Attribute ownership sets. Ownership is always decided per set, never with one broad "user ownership" setting.
/// </summary>
public enum AttributeSet
{
    CoreProfile = 0,
    EmploymentAttributes,
    NamingAttributes,
    MailAttributes,
    GroupMembership,
    ApplicationAssignments,
    AccountEnabledState,
    Password,
    AuthenticationSessions,
    LicenceAssignments,
    ManagerAndOwnership,
    DeviceAccess,
}

public enum LifecycleAction
{
    Joiner = 0,
    Mover,
    Leaver,
    Rehire,
    PasswordReset,
    Reconcile,
}

public enum StrategyKind
{
    ReadOnly = 0,
    ContainmentOnlyLegacy,
    DirectActiveDirectory,
    OktaApiProvisioning,
    MigrationTransition,
    ManualControlled,
}
