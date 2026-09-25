using Ilm.Application.Configuration;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Identity;

namespace Ilm.Application.Provisioning;

/// <summary>
/// Approved migration actions only, on a MigrationException connector with an expiry, an approved identity
/// link and an approved transition state. Disabled initially; no automated migration writes are implemented.
/// </summary>
public sealed class MigrationTransitionStrategy(IActiveConfigurationProvider configuration, TimeProvider time) : IIdentityProvisioningStrategy
{
    public StrategyKind Kind => StrategyKind.MigrationTransition;

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        (await configuration.GetAsync(cancellationToken)).IsEnabled(Feature.MigrationTransition);

    public Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new ProvisioningPlan(Kind, context.Identity.Id,
            [new PlannedStep(1, "ApprovedMigrationAction", null, "Only operations listed in the connector's approved migration exception.", true)],
            ["MigrationException connector with future expiry.", "Approved identity link.", "Approved transition state."]));
    }

    public async Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<string>();
        var config = await configuration.GetAsync(cancellationToken);
        if (!config.IsEnabled(Feature.MigrationTransition))
        {
            errors.Add("Migration transition is disabled by feature flag.");
        }

        var definition = context.Identity.ConnectorId is null ? null : config.FindConnector(context.Identity.ConnectorId);
        if (definition?.Mode != ConnectorMode.MigrationException)
        {
            errors.Add("The connector is not in MigrationException mode.");
        }
        else if (definition.MigrationExceptionExpiresUtc is null || definition.MigrationExceptionExpiresUtc <= time.GetUtcNow().UtcDateTime)
        {
            errors.Add("The migration exception has expired or has no expiry.");
        }

        if (!IdentityLinkPolicy.IsContainmentEligible(context.LinkConfidence))
        {
            errors.Add("An approved identity link is required.");
        }

        if (context.MigrationState is not { IsApprovedTransition: true })
        {
            errors.Add("An approved transition state is required.");
        }

        return errors.Count == 0 ? PlanValidationResult.Valid : new PlanValidationResult(false, errors);
    }

    public async Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        var validation = await ValidatePlanAsync(context, plan, cancellationToken);
        return new ExecutionResult(
            validation.IsValid ? ExecutionOutcome.ManualActionRequired : ExecutionOutcome.Denied,
            validation.IsValid ? SafeErrorCategory.None : SafeErrorCategory.FeatureDisabled,
            validation.IsValid ? "No automated migration actions are implemented; perform the approved action manually." : string.Join(" ", validation.Errors),
            null,
            [],
            [],
            false);
    }

    public Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(new ReconciliationResult(false, "Not implemented for migration transitions.", null));
}
