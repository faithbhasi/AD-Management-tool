namespace Ilm.Domain.Common;

/// <summary>
/// Error categories that are safe to show to operators and write to audit.
/// Raw exception text never leaves the process; only a category does.
/// </summary>
public enum SafeErrorCategory
{
    None = 0,
    ValidationFailed,
    NotFound,
    NotAuthorised,
    ProtectedObject,
    ProtectionUnknown,
    AuthorityMissing,
    AuthorityConflict,
    ConnectorModeDenied,
    ConnectorUnavailable,
    Timeout,
    ConcurrencyConflict,
    DuplicateObject,
    ConstraintViolation,
    InsufficientDirectoryRights,
    FeatureDisabled,
    ExternalServiceError,
    VerificationFailed,
    IdempotencyConflict,
    ApprovalInvalid,
    IdentityResolutionInconclusive,
    Unexpected,
}
