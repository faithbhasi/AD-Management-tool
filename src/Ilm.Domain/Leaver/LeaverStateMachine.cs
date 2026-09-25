using Ilm.Domain.Common;

namespace Ilm.Domain.Leaver;

/// <summary>
/// Allowed durable transitions. A leaver can never leave containment silently: every failure
/// state has an exit toward manual containment, and cancellation is impossible once containment starts.
/// </summary>
public static class LeaverStateMachine
{
    private static readonly Dictionary<LeaverState, LeaverState[]> Allowed = new()
    {
        [LeaverState.Requested] = [LeaverState.Validated, LeaverState.ValidationFailed, LeaverState.Cancelled],
        [LeaverState.ValidationFailed] = [LeaverState.Validated, LeaverState.Cancelled],
        [LeaverState.Validated] = [LeaverState.AwaitingApproval, LeaverState.Cancelled],
        [LeaverState.AwaitingApproval] = [LeaverState.Approved, LeaverState.Rejected, LeaverState.Cancelled, LeaverState.Validated],
        [LeaverState.Approved] = [LeaverState.Scheduled, LeaverState.ContainmentStarted, LeaverState.AwaitingApproval, LeaverState.Cancelled],
        [LeaverState.Scheduled] = [LeaverState.ContainmentStarted, LeaverState.AwaitingApproval, LeaverState.Cancelled],
        [LeaverState.ContainmentStarted] =
        [
            LeaverState.AuthenticationContained, LeaverState.ManualContainmentRequired, LeaverState.PartiallyContained,
            LeaverState.FailedBeforeChange, LeaverState.FailedAfterChange,
        ],
        [LeaverState.AuthenticationContained] =
        [
            LeaverState.SessionsRevoked, LeaverState.PartiallyContained, LeaverState.ManualContainmentRequired, LeaverState.FailedAfterChange,
        ],
        [LeaverState.SessionsRevoked] =
        [
            LeaverState.DirectoryAccountDisabled, LeaverState.PartiallyContained, LeaverState.ManualContainmentRequired, LeaverState.FailedAfterChange,
        ],
        [LeaverState.DirectoryAccountDisabled] =
        [
            LeaverState.ContainmentVerificationPending, LeaverState.PartiallyContained, LeaverState.ManualContainmentRequired,
        ],
        [LeaverState.ContainmentVerificationPending] =
        [
            LeaverState.SafelyContained, LeaverState.PartiallyContained, LeaverState.ManualContainmentRequired,
        ],
        [LeaverState.ManualContainmentRequired] = [LeaverState.ContainmentVerificationPending, LeaverState.PartiallyContained],
        [LeaverState.PartiallyContained] = [LeaverState.ContainmentVerificationPending, LeaverState.ManualContainmentRequired],
        [LeaverState.FailedBeforeChange] = [LeaverState.ManualContainmentRequired, LeaverState.ContainmentStarted],
        [LeaverState.FailedAfterChange] = [LeaverState.PartiallyContained, LeaverState.ManualContainmentRequired, LeaverState.ContainmentStarted],
        [LeaverState.SafelyContained] = [LeaverState.RetentionActionsPending, LeaverState.RollbackRequested],
        [LeaverState.RetentionActionsPending] = [LeaverState.OwnershipTransferPending, LeaverState.ReconciliationRequired, LeaverState.RollbackRequested],
        [LeaverState.OwnershipTransferPending] = [LeaverState.ReconciliationPending, LeaverState.ReconciliationRequired, LeaverState.RollbackRequested],
        [LeaverState.ReconciliationPending] = [LeaverState.Completed, LeaverState.ReconciliationRequired],
        [LeaverState.ReconciliationRequired] = [LeaverState.ReconciliationPending, LeaverState.ManualContainmentRequired],
        [LeaverState.RollbackRequested] = [LeaverState.RollbackPartiallyCompleted, LeaverState.Completed],
        [LeaverState.RollbackPartiallyCompleted] = [LeaverState.Completed, LeaverState.ManualContainmentRequired],
        [LeaverState.Completed] = [],
        [LeaverState.Rejected] = [],
        [LeaverState.Cancelled] = [],
    };

    public static bool CanTransition(LeaverState from, LeaverState to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<LeaverState> NextStates(LeaverState from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    public static void EnsureCanTransition(LeaverState from, LeaverState to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, $"Leaver state cannot move from {from} to {to}.");
        }
    }

    public static bool IsTerminal(LeaverState state) =>
        state is LeaverState.Completed or LeaverState.Rejected or LeaverState.Cancelled;

    /// <summary>States in which containment has begun and the request must stay visible until resolved.</summary>
    public static bool IsContainmentInFlight(LeaverState state) => state is
        LeaverState.ContainmentStarted or LeaverState.AuthenticationContained or LeaverState.SessionsRevoked
        or LeaverState.DirectoryAccountDisabled or LeaverState.ContainmentVerificationPending
        or LeaverState.ManualContainmentRequired or LeaverState.PartiallyContained
        or LeaverState.FailedBeforeChange or LeaverState.FailedAfterChange;

    public static bool IsCancellable(LeaverState state) => CanTransition(state, LeaverState.Cancelled);

    /// <summary>States that need operator attention and must raise an alert.</summary>
    public static bool RequiresAttention(LeaverState state) => state is
        LeaverState.ValidationFailed or LeaverState.ManualContainmentRequired or LeaverState.PartiallyContained
        or LeaverState.FailedBeforeChange or LeaverState.FailedAfterChange or LeaverState.ReconciliationRequired
        or LeaverState.RollbackPartiallyCompleted;
}
