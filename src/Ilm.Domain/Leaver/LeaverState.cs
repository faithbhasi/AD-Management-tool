namespace Ilm.Domain.Leaver;

public enum LeaverState
{
    Requested = 0,
    Validated,
    AwaitingApproval,
    Approved,
    Scheduled,
    ContainmentStarted,
    AuthenticationContained,
    SessionsRevoked,
    DirectoryAccountDisabled,
    ContainmentVerificationPending,
    SafelyContained,
    RetentionActionsPending,
    OwnershipTransferPending,
    ReconciliationPending,
    Completed,

    // Exception states
    Rejected,
    Cancelled,
    ValidationFailed,
    ManualContainmentRequired,
    PartiallyContained,
    ReconciliationRequired,
    RollbackRequested,
    RollbackPartiallyCompleted,
    FailedBeforeChange,
    FailedAfterChange,
}

public enum LeaverUrgency
{
    Planned = 0,
    Urgent,
}
