using Ilm.Domain.Common;
using Ilm.Domain.Leaver;

namespace Ilm.UnitTests.Leaver;

public sealed class LeaverStateMachineTests
{
    [Theory]
    [InlineData(LeaverState.Requested, LeaverState.Validated)]
    [InlineData(LeaverState.AwaitingApproval, LeaverState.Approved)]
    [InlineData(LeaverState.ContainmentStarted, LeaverState.AuthenticationContained)]
    [InlineData(LeaverState.AuthenticationContained, LeaverState.SessionsRevoked)]
    [InlineData(LeaverState.SessionsRevoked, LeaverState.DirectoryAccountDisabled)]
    [InlineData(LeaverState.DirectoryAccountDisabled, LeaverState.ContainmentVerificationPending)]
    [InlineData(LeaverState.ContainmentVerificationPending, LeaverState.SafelyContained)]
    [InlineData(LeaverState.SafelyContained, LeaverState.RetentionActionsPending)]
    [InlineData(LeaverState.ReconciliationPending, LeaverState.Completed)]
    [InlineData(LeaverState.FailedBeforeChange, LeaverState.ManualContainmentRequired)]
    public void Happy_and_recovery_paths_are_allowed(LeaverState from, LeaverState to) =>
        Assert.True(LeaverStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(LeaverState.Requested, LeaverState.SafelyContained)]
    [InlineData(LeaverState.AwaitingApproval, LeaverState.ContainmentStarted)]
    [InlineData(LeaverState.ContainmentStarted, LeaverState.SafelyContained)]
    [InlineData(LeaverState.ManualContainmentRequired, LeaverState.SafelyContained)]
    [InlineData(LeaverState.Completed, LeaverState.Requested)]
    public void Shortcuts_are_refused(LeaverState from, LeaverState to)
    {
        Assert.False(LeaverStateMachine.CanTransition(from, to));
        Assert.Throws<DomainException>(() => LeaverStateMachine.EnsureCanTransition(from, to));
    }

    [Fact]
    public void Containment_cannot_be_cancelled_once_started()
    {
        foreach (var state in Enum.GetValues<LeaverState>().Where(LeaverStateMachine.IsContainmentInFlight))
        {
            Assert.False(LeaverStateMachine.IsCancellable(state), state.ToString());
        }
    }

    [Fact]
    public void Every_exception_state_has_a_way_out()
    {
        LeaverState[] exceptional =
        [
            LeaverState.ValidationFailed, LeaverState.ManualContainmentRequired, LeaverState.PartiallyContained,
            LeaverState.ReconciliationRequired, LeaverState.RollbackRequested, LeaverState.RollbackPartiallyCompleted,
            LeaverState.FailedBeforeChange, LeaverState.FailedAfterChange,
        ];
        foreach (var state in exceptional)
        {
            Assert.NotEmpty(LeaverStateMachine.NextStates(state));
        }
    }

    [Fact]
    public void Failure_states_require_attention()
    {
        Assert.True(LeaverStateMachine.RequiresAttention(LeaverState.ManualContainmentRequired));
        Assert.True(LeaverStateMachine.RequiresAttention(LeaverState.PartiallyContained));
        Assert.True(LeaverStateMachine.RequiresAttention(LeaverState.ValidationFailed));
        Assert.False(LeaverStateMachine.RequiresAttention(LeaverState.SafelyContained));
    }

    [Fact]
    public void Required_state_names_exist()
    {
        string[] required =
        [
            "Requested", "Validated", "AwaitingApproval", "Approved", "Scheduled", "ContainmentStarted", "AuthenticationContained",
            "SessionsRevoked", "DirectoryAccountDisabled", "ContainmentVerificationPending", "SafelyContained", "RetentionActionsPending",
            "OwnershipTransferPending", "ReconciliationPending", "Completed", "Rejected", "Cancelled", "ValidationFailed",
            "ManualContainmentRequired", "PartiallyContained", "ReconciliationRequired", "RollbackRequested", "RollbackPartiallyCompleted",
            "FailedBeforeChange", "FailedAfterChange",
        ];
        Assert.Equal(required.OrderBy(s => s), Enum.GetNames<LeaverState>().OrderBy(s => s));
    }
}
