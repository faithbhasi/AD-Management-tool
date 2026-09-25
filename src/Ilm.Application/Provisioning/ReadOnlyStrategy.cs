using Ilm.Application.Directory;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;

namespace Ilm.Application.Provisioning;

/// <summary>Search and reconciliation only. Any write step makes the plan invalid.</summary>
public sealed class ReadOnlyStrategy(IDirectoryConnectorRegistry registry) : IIdentityProvisioningStrategy
{
    public StrategyKind Kind => StrategyKind.ReadOnly;

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new ProvisioningPlan(
            Kind,
            context.Identity.Id,
            [new PlannedStep(1, "Read", DirectoryOperation.ReadObject, "Read the current state for reconciliation.", Mandatory: true)],
            ["No changes are made by the read-only strategy."]));
    }

    public Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var writes = plan.Steps.Where(s => s.DirectoryOperation is { } op && !ConnectorModePolicy.IsReadOperation(op)).ToList();
        return Task.FromResult(writes.Count == 0
            ? PlanValidationResult.Valid
            : PlanValidationResult.Invalid("The read-only strategy cannot perform write operations."));
    }

    public Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.ConnectorModeDenied, "The read-only strategy never changes state.", null, [], [], false));

    public async Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Identity.ConnectorId is null || context.Identity.ObjectGuid is null)
        {
            return new ReconciliationResult(false, "The identity has no directory connector.", null);
        }

        var obj = await registry.GetReader(context.Identity.ConnectorId).GetByGuidAsync(context.Identity.ObjectGuid.Value, null, cancellationToken);
        return new ReconciliationResult(obj is not null, obj is null ? "Not found." : "Read.", null);
    }
}
