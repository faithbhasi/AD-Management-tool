namespace Ilm.Domain.Leaver;

/// <summary>
/// States exactly which controls must be verified before a leaver is SafelyContained.
/// Authentication containment and directory disable are always mandatory; the validator rejects any attempt to relax them.
/// </summary>
public sealed class SafelyContainedPolicy
{
    public bool RequireAuthenticationContainmentVerified { get; set; } = true;

    public bool RequireDirectoryDisableVerified { get; set; } = true;

    public bool RequireAllIdentityLinksResolved { get; set; } = true;

    public Dictionary<SessionSystem, SessionRequirement> SessionRequirements { get; set; } = new()
    {
        [SessionSystem.Okta] = SessionRequirement.Mandatory,
        [SessionSystem.Entra] = SessionRequirement.Mandatory,
        [SessionSystem.Citrix] = SessionRequirement.Mandatory,
        [SessionSystem.Vpn] = SessionRequirement.BestEffort,
        [SessionSystem.Application] = SessionRequirement.BestEffort,
    };

    public OktaContainmentAction OktaAction { get; set; } = OktaContainmentAction.Deactivate;

    public bool RevokeOktaOAuthTokens { get; set; } = true;

    /// <summary>
    /// When true, ILM may disable an Okta-owned AD account directly if Okta has not disabled it in time.
    /// Requires Security Approver approval of the specific leaver.
    /// </summary>
    public bool AllowEmergencyDirectoryOverride { get; set; }

    public int DirectoryVerificationTimeoutSeconds { get; set; } = 300;

    public int ManualContainmentSlaMinutes { get; set; } = 30;

    public int UrgentContainmentSlaMinutes { get; set; } = 15;

    public SessionRequirement RequirementFor(SessionSystem system) =>
        SessionRequirements.TryGetValue(system, out var requirement) ? requirement : SessionRequirement.Mandatory;
}

public sealed record SafelyContainedAssessment(bool IsSafelyContained, IReadOnlyList<string> UnmetRequirements, IReadOnlyList<string> SatisfiedRequirements);
