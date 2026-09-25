using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Identity;
using Ilm.Domain.Protection;

namespace Ilm.Application.Provisioning;

/// <summary>
/// Disables an existing standard account in a legacy forest and verifies it. Needs no linked target identity,
/// but does need a conclusively resolved person and account, an approved leaver, protection clearance and a pinned DC.
/// </summary>
public sealed class ContainmentOnlyLegacyStrategy(
    IDirectoryConnectorRegistry registry,
    IActiveConfigurationProvider configuration,
    GuardedDirectoryWriter writer) : IIdentityProvisioningStrategy
{
    public StrategyKind Kind => StrategyKind.ContainmentOnlyLegacy;

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        (await configuration.GetAsync(cancellationToken)).IsEnabled(Feature.LegacyContainment);

    public Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var steps = new List<PlannedStep>
        {
            new(1, "SelectWritableDc", null, "Select and record one writable DC in the legacy domain.", true),
            new(2, "DisableAccount", DirectoryOperation.DisableAccount, "Set ACCOUNTDISABLE (compare-and-swap; no other bit or attribute changes).", true),
            new(3, "VerifyDisabled", DirectoryOperation.VerifyAccountState, "Re-read userAccountControl on the same DC.", true),
        };
        return Task.FromResult(new ProvisioningPlan(
            Kind,
            context.Identity.Id,
            steps,
            [
                "Approved leaver request.",
                "Person and legacy account conclusively resolved (Authoritative or HumanApproved link).",
                "Protection conclusively Clear.",
                "Connector mode ContainmentOnlyLegacy.",
            ]));
    }

    public async Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        var errors = new List<string>();

        if (!await IsEnabledAsync(cancellationToken))
        {
            errors.Add("Legacy containment is disabled by feature flag.");
        }

        if (context.Action != LifecycleAction.Leaver)
        {
            errors.Add("ContainmentOnlyLegacy supports leaver containment only.");
        }

        if (!context.ApprovedLeaverRequest)
        {
            errors.Add("An approved leaver request is required.");
        }

        if (!IdentityLinkPolicy.IsContainmentEligible(context.LinkConfidence))
        {
            errors.Add("The legacy account is not conclusively linked to the person.");
        }

        if (context.Identity.AccountType != AccountType.Standard)
        {
            errors.Add("ContainmentOnlyLegacy disables standard accounts only; other account types use manual containment.");
        }

        if (context.Identity.ConnectorId is null || context.Identity.ObjectGuid is null)
        {
            errors.Add("The account has no directory connector or objectGUID.");
        }
        else
        {
            var definition = (await configuration.GetAsync(cancellationToken)).FindConnector(context.Identity.ConnectorId);
            if (definition?.Mode != ConnectorMode.ContainmentOnlyLegacy || definition.Role != ForestRole.Legacy)
            {
                errors.Add("The connector is not a ContainmentOnlyLegacy legacy connector.");
            }
        }

        if (context.Protection is not { Status: ProtectionStatus.Clear })
        {
            errors.Add("Protection is not conclusively Clear.");
        }

        var allowed = new HashSet<DirectoryOperation?> { null, DirectoryOperation.DisableAccount, DirectoryOperation.VerifyAccountState };
        if (plan.Steps.Any(s => !allowed.Contains(s.DirectoryOperation)))
        {
            errors.Add("The plan contains operations outside the containment-only allowlist.");
        }

        return errors.Count == 0 ? PlanValidationResult.Valid : new PlanValidationResult(false, errors);
    }

    public async Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var validation = await ValidatePlanAsync(context, plan, cancellationToken);
        if (!validation.IsValid)
        {
            return new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.ValidationFailed, string.Join(" ", validation.Errors), null, [], [], false);
        }

        return await DirectoryContainment.DisableAndVerifyAsync(registry, writer, context, requireStandardUser: true, cancellationToken);
    }

    public Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        DirectoryContainment.ReconcileDisabledAsync(registry, context, cancellationToken);
}

/// <summary>Shared disable-and-verify used by the legacy and direct strategies.</summary>
internal static class DirectoryContainment
{
    public static async Task<ExecutionResult> DisableAndVerifyAsync(
        IDirectoryConnectorRegistry registry,
        GuardedDirectoryWriter writer,
        ProvisioningContext context,
        bool requireStandardUser,
        CancellationToken cancellationToken)
    {
        var connectorId = context.Identity.ConnectorId!;
        var guid = context.Identity.ObjectGuid!.Value;
        var attempted = new List<string>();
        var verified = new List<string>();
        var reader = registry.GetReader(connectorId);

        string dc;
        if (context.DomainController is { } pinned)
        {
            dc = pinned;
        }
        else
        {
            try
            {
                dc = (await reader.SelectWritableDomainControllerAsync(cancellationToken)).DomainController;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ExecutionResult(ExecutionOutcome.FailedBeforeChange, SafeErrorCategory.ConnectorUnavailable, "No writable domain controller could be selected.", null, attempted, verified, false);
            }
        }

        attempted.Add($"SelectWritableDc:{dc}");
        attempted.Add("DisableAccount");
        var outcome = await writer.DisableAccountAsync(connectorId, guid, dc, requireStandardUser, cancellationToken);
        var result = outcome.Result;
        if (!result.Succeeded)
        {
            var denied = result.ErrorCategory is SafeErrorCategory.ProtectedObject or SafeErrorCategory.ProtectionUnknown or SafeErrorCategory.ConnectorModeDenied;
            return new ExecutionResult(
                denied ? ExecutionOutcome.Denied : ExecutionOutcome.FailedBeforeChange,
                result.ErrorCategory,
                result.Detail,
                dc,
                attempted,
                verified,
                false,
                outcome.Protection);
        }

        attempted.Add("VerifyDisabled");
        var state = await reader.ReadAccountStateAsync(guid, dc, cancellationToken);
        if (state is { IsDisabled: true })
        {
            verified.Add($"Disabled on {dc} (userAccountControl={state.UserAccountControl})");
            return new ExecutionResult(
                result.AlreadyInDesiredState ? ExecutionOutcome.AlreadyInDesiredState : ExecutionOutcome.Succeeded,
                SafeErrorCategory.None,
                result.AlreadyInDesiredState ? "Already disabled; verified." : "Disabled and verified.",
                dc,
                attempted,
                verified,
                !result.AlreadyInDesiredState,
                outcome.Protection);
        }

        return new ExecutionResult(ExecutionOutcome.FailedAfterChange, SafeErrorCategory.VerificationFailed, "The disable was written but could not be verified.", dc, attempted, verified, true, outcome.Protection);
    }

    public static async Task<ReconciliationResult> ReconcileDisabledAsync(IDirectoryConnectorRegistry registry, ProvisioningContext context, CancellationToken cancellationToken)
    {
        if (context.Identity.ConnectorId is null || context.Identity.ObjectGuid is null)
        {
            return new ReconciliationResult(false, "No directory identity.", null);
        }

        var reader = registry.GetReader(context.Identity.ConnectorId);
        var dc = context.DomainController ?? (await reader.SelectWritableDomainControllerAsync(cancellationToken)).DomainController;
        var state = await reader.ReadAccountStateAsync(context.Identity.ObjectGuid.Value, dc, cancellationToken);
        return state is null
            ? new ReconciliationResult(false, "Account not found.", dc)
            : new ReconciliationResult(state.IsDisabled, state.IsDisabled ? "Disabled." : "Enabled.", dc);
    }
}
