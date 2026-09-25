using Ilm.Application.Okta;
using Ilm.Application.Sessions;
using Ilm.Domain.Leaver;

namespace Ilm.Infrastructure.Sessions;

/// <summary>Clears Okta sessions (and optionally OAuth tokens) through the supported Users API.</summary>
public sealed class OktaSessionConnector(IOktaSessionClient sessions) : ISessionConnector
{
    public SessionSystem System => SessionSystem.Okta;

    public string Coverage =>
        "Clears the user's Okta sessions and, when requested, OAuth tokens issued by Okta. It does not close sessions " +
        "that downstream web or native applications maintain independently.";

    public async Task<SessionRevocationResult> RevokeAsync(SessionRevocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OktaUserId is null)
        {
            return new SessionRevocationResult(SessionRevocationStatus.NotConfigured, "The person has no Okta identity.", false);
        }

        var result = await sessions.RevokeSessionsAsync(request.OktaUserId, request.RevokeOAuthTokens, cancellationToken);
        return result.Succeeded
            ? new SessionRevocationResult(SessionRevocationStatus.Succeeded, $"Okta accepted session revocation (HTTP {result.StatusCode}).", false)
            : new SessionRevocationResult(SessionRevocationStatus.Failed, $"Okta session revocation failed (HTTP {result.StatusCode}, {result.ErrorCategory}).", false);
    }
}

/// <summary>
/// A typed connector for a session system that has not been integrated yet. It always asks for manual action,
/// so the gap is visible instead of silently ignored.
/// </summary>
public sealed class NotConfiguredSessionConnector(SessionSystem system, string coverage) : ISessionConnector
{
    public SessionSystem System { get; } = system;

    public string Coverage { get; } = coverage;

    public Task<SessionRevocationResult> RevokeAsync(SessionRevocationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new SessionRevocationResult(SessionRevocationStatus.NotConfigured, $"{System} session revocation is not integrated; manual action is required.", false));
}

/// <summary>Development and test double for Entra, Citrix, VPN and application session systems.</summary>
public sealed class MockSessionConnector(SessionSystem system) : ISessionConnector
{
    private readonly HashSet<string> revoked = new(StringComparer.Ordinal);

    public SessionSystem System { get; } = system;

    public string Coverage => $"Mock {System} connector for development. Not evidence of real session termination.";

    /// <summary>Result to return; tests change this to exercise failure paths.</summary>
    public SessionRevocationStatus NextStatus { get; set; } = SessionRevocationStatus.Succeeded;

    public int Calls { get; private set; }

    public Task<SessionRevocationResult> RevokeAsync(SessionRevocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Calls++;
        if (NextStatus == SessionRevocationStatus.Succeeded)
        {
            revoked.Add(request.IdempotencyKey);
        }

        return Task.FromResult(new SessionRevocationResult(NextStatus, $"Mock {System}: {NextStatus}.", false));
    }
}
