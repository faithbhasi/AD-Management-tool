using Ilm.Application.Abstractions;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Domain.Identity;
using Ilm.Domain.Protection;

namespace Ilm.Application.Provisioning;

/// <summary>Everything a strategy needs to plan and execute one lifecycle action on one identity.</summary>
public sealed record ProvisioningContext
{
    public required LifecycleAction Action { get; init; }

    public required ExternalIdentity Identity { get; init; }

    public Person? Person { get; init; }

    public LinkConfidence LinkConfidence { get; init; }

    public required AuthorityDecision Authority { get; init; }

    public ProtectionDecision? Protection { get; init; }

    public bool ApprovedLeaverRequest { get; init; }

    public MigrationState? MigrationState { get; init; }

    public Guid OperationId { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string? DomainController { get; init; }

    public required ActorContext Actor { get; init; }
}

public sealed record PlannedStep(int Order, string Operation, DirectoryOperation? DirectoryOperation, string Description, bool Mandatory);

public sealed record ProvisioningPlan(StrategyKind Strategy, Guid IdentityId, IReadOnlyList<PlannedStep> Steps, IReadOnlyList<string> Preconditions)
{
    public string Hash => Hashing.Sha256Hex(
        Strategy + "|" + IdentityId + "|" + string.Join(";", Steps.Select(s => $"{s.Order}:{s.Operation}:{s.Mandatory}")));
}

public sealed record PlanValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static readonly PlanValidationResult Valid = new(true, []);

    public static PlanValidationResult Invalid(params string[] errors) => new(false, errors);
}

public enum ExecutionOutcome
{
    Succeeded = 0,
    AlreadyInDesiredState,
    ManualActionRequired,
    Denied,
    FailedBeforeChange,
    FailedAfterChange,
}

public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    SafeErrorCategory ErrorCategory,
    string Detail,
    string? DomainController,
    IReadOnlyList<string> AttemptedSteps,
    IReadOnlyList<string> VerifiedSteps,
    bool ChangedExternalState,
    ProtectionDecision? Protection = null);

public sealed record ReconciliationResult(bool InDesiredState, string Detail, string? DomainController);

/// <summary>
/// A way of carrying out lifecycle actions for a population. Only one strategy may write a given
/// population, action and attribute set; disabled strategies refuse to execute.
/// </summary>
public interface IIdentityProvisioningStrategy
{
    StrategyKind Kind { get; }

    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);

    Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken);

    Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken);

    Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken);

    Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken);
}
