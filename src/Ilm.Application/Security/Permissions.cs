using Ilm.Domain.Security;

namespace Ilm.Application.Security;

public enum Permission
{
    ViewDirectory = 0,
    ViewPeople,
    RequestLeaver,
    ApproveLeaver,
    ExecuteContainment,
    CompleteManualTask,
    ProposeIdentityLink,
    ApproveIdentityLink,
    ViewAudit,
    ExportAudit,
    ProposeConfiguration,
    ApproveConfiguration,
    ActivateConfiguration,
    ViewAdministration,
    RunFeasibility,
    ApproveFeasibility,
    RunReconciliation,
    ViewApprovals,
}

/// <summary>Maps application roles to permissions. The Database Administrator role does not exist in the application.</summary>
public static class Permissions
{
    private static readonly Dictionary<Permission, AppRole[]> Map = new()
    {
        [Permission.ViewDirectory] = [AppRole.Reader, AppRole.LifecycleOperator, AppRole.LifecycleApprover, AppRole.SecurityApprover],
        [Permission.ViewPeople] = [AppRole.Reader, AppRole.LifecycleOperator, AppRole.LifecycleApprover, AppRole.SecurityApprover],
        [Permission.RequestLeaver] = [AppRole.LifecycleOperator],
        [Permission.ApproveLeaver] = [AppRole.LifecycleApprover, AppRole.SecurityApprover],
        [Permission.ExecuteContainment] = [AppRole.LifecycleOperator],
        [Permission.CompleteManualTask] = [AppRole.LifecycleOperator, AppRole.SecurityApprover],
        [Permission.ProposeIdentityLink] = [AppRole.LifecycleOperator],
        [Permission.ApproveIdentityLink] = [AppRole.LifecycleApprover, AppRole.SecurityApprover],
        [Permission.ViewAudit] = [AppRole.Auditor, AppRole.SecurityApprover],
        [Permission.ExportAudit] = [AppRole.Auditor],
        [Permission.ProposeConfiguration] = [AppRole.ConfigurationAdministrator],
        [Permission.ApproveConfiguration] = [AppRole.SecurityApprover],
        [Permission.ActivateConfiguration] = [AppRole.ConfigurationAdministrator, AppRole.SecurityApprover],
        [Permission.ViewAdministration] = [AppRole.ConfigurationAdministrator, AppRole.SecurityApprover, AppRole.Auditor],
        [Permission.RunFeasibility] = [AppRole.ConfigurationAdministrator],
        [Permission.ApproveFeasibility] = [AppRole.SecurityApprover],
        [Permission.RunReconciliation] = [AppRole.LifecycleOperator, AppRole.SecurityApprover],
        [Permission.ViewApprovals] = [AppRole.LifecycleOperator, AppRole.LifecycleApprover, AppRole.SecurityApprover, AppRole.ConfigurationAdministrator, AppRole.Auditor],
    };

    public static IReadOnlyList<AppRole> RolesFor(Permission permission) => Map[permission];

    public static bool Has(Abstractions.ActorContext actor, Permission permission)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Map[permission].Any(actor.HasRole);
    }
}
