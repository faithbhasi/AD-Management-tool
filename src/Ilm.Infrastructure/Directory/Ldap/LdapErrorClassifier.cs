using System.DirectoryServices.Protocols;
using System.Globalization;
using Ilm.Domain.Common;

namespace Ilm.Infrastructure.Directory.Ldap;

/// <summary>
/// Maps native LDAP/AD errors to safe categories. AD appends a Win32 error code as the first eight hex
/// digits of the server error message (for example "0000052D: ..."); only the code is inspected.
/// </summary>
public static class LdapErrorClassifier
{
    public const int ErrorPasswordRestriction = 0x52D;
    public const int ErrorDsNameAlreadyExists = 0x2071;
    public const int ErrorObjectAlreadyExists = 0x1392;
    public const int ErrorInsufficientRights = 0x5;

    public static SafeErrorCategory Classify(ResultCode resultCode, string? serverErrorMessage)
    {
        var win32 = ExtractWin32Code(serverErrorMessage);
        return resultCode switch
        {
            ResultCode.EntryAlreadyExists => SafeErrorCategory.DuplicateObject,
            ResultCode.AttributeOrValueExists => SafeErrorCategory.DuplicateObject,
            ResultCode.ConstraintViolation when win32 is ErrorDsNameAlreadyExists or ErrorObjectAlreadyExists => SafeErrorCategory.DuplicateObject,
            ResultCode.ConstraintViolation => SafeErrorCategory.ConstraintViolation,
            ResultCode.UnwillingToPerform when win32 == ErrorPasswordRestriction => SafeErrorCategory.ConstraintViolation,
            ResultCode.UnwillingToPerform => SafeErrorCategory.ConstraintViolation,
            ResultCode.InsufficientAccessRights => SafeErrorCategory.InsufficientDirectoryRights,
            ResultCode.NoSuchObject => SafeErrorCategory.NotFound,
            ResultCode.NoSuchAttribute => SafeErrorCategory.ConcurrencyConflict,
            ResultCode.Busy or ResultCode.Unavailable => SafeErrorCategory.ConnectorUnavailable,
            ResultCode.TimeLimitExceeded => SafeErrorCategory.Timeout,
            ResultCode.StrongAuthRequired or ResultCode.ConfidentialityRequired => SafeErrorCategory.InsufficientDirectoryRights,
            _ => SafeErrorCategory.ExternalServiceError,
        };
    }

    public static int? ExtractWin32Code(string? serverErrorMessage)
    {
        if (string.IsNullOrEmpty(serverErrorMessage) || serverErrorMessage.Length < 8)
        {
            return null;
        }

        return int.TryParse(serverErrorMessage.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) ? code : null;
    }
}
