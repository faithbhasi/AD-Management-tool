namespace Ilm.Domain.Security;

/// <summary>Application roles. The Database Administrator has no application role by design.</summary>
public enum AppRole
{
    Reader = 0,
    LifecycleOperator,
    LifecycleApprover,
    SecurityApprover,
    ConfigurationAdministrator,
    Auditor,
}
