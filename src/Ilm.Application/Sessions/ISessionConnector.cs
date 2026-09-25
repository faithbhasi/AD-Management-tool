using Ilm.Domain.Leaver;

namespace Ilm.Application.Sessions;

public sealed record SessionRevocationRequest(
    Guid PersonId,
    Guid OperationId,
    string IdempotencyKey,
    string? OktaUserId,
    string? EntraObjectId,
    string? UserPrincipalName,
    string? SamAccountName,
    bool RevokeOAuthTokens);

public sealed record SessionRevocationResult(SessionRevocationStatus Status, string Detail, bool VerifiedByReadBack);

/// <summary>
/// Revokes live sessions in one system. Disabling an AD account does not end existing sessions, so each
/// session-holding system needs its own connector. Okta revocation does not close sessions that downstream
/// applications maintain independently.
/// </summary>
public interface ISessionConnector
{
    SessionSystem System { get; }

    /// <summary>A plain-language statement of what this connector does and does not terminate.</summary>
    string Coverage { get; }

    Task<SessionRevocationResult> RevokeAsync(SessionRevocationRequest request, CancellationToken cancellationToken);
}
