using Ilm.Domain.Common;

namespace Ilm.Application.Okta;

public sealed record OktaResult(bool Succeeded, int StatusCode, SafeErrorCategory ErrorCategory, string? OktaErrorCode, TimeSpan? RetryAfter)
{
    public static OktaResult Ok(int status = 200) => new(true, status, SafeErrorCategory.None, null, null);

    public static OktaResult Fail(int status, SafeErrorCategory category, string? code = null, TimeSpan? retryAfter = null) =>
        new(false, status, category, code, retryAfter);
}

public sealed record OktaResult<T>(bool Succeeded, T? Value, int StatusCode, SafeErrorCategory ErrorCategory, string? OktaErrorCode)
{
    public static OktaResult<T> Ok(T value, int status = 200) => new(true, value, status, SafeErrorCategory.None, null);

    public static OktaResult<T> Fail(int status, SafeErrorCategory category, string? code = null) => new(false, default, status, category, code);
}

/// <summary>Okta user lifecycle status as returned by the Users API.</summary>
public static class OktaUserStatus
{
    public const string Staged = "STAGED";
    public const string Provisioned = "PROVISIONED";
    public const string Active = "ACTIVE";
    public const string Recovery = "RECOVERY";
    public const string PasswordExpired = "PASSWORD_EXPIRED";
    public const string LockedOut = "LOCKED_OUT";
    public const string Suspended = "SUSPENDED";
    public const string Deprovisioned = "DEPROVISIONED";
}

public sealed record OktaUser(
    string Id,
    string Status,
    string Login,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Department,
    string? ManagerId,
    DateTime? CreatedUtc,
    DateTime? StatusChangedUtc);

public sealed record OktaCreateUserRequest(
    string Login,
    string Email,
    string FirstName,
    string LastName,
    string? Department,
    string? ManagerId,
    string? EmployeeNumber);

public sealed record OktaUserSearch(string? Email, string? Login, string? EmployeeNumber);

public sealed record OktaLogEvent(
    string Uuid,
    DateTime PublishedUtc,
    string EventType,
    string Outcome,
    string? ActorId,
    string? ActorType,
    string? TargetUserId,
    string? TransactionId,
    string? DisplayMessage);
