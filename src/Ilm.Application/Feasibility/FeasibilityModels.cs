namespace Ilm.Application.Feasibility;

public enum CheckOutcome
{
    Passed = 0,
    Failed,
    Observed,
    Skipped,
    Inconclusive,
}

public sealed record FeasibilityCheckResult(int Number, string Title, CheckOutcome Outcome, string Observation, long DurationMs);

/// <summary>
/// Inputs for one harness run. Operator-observed settings (profile source, push mappings, licensing) cannot
/// all be read through supported APIs, so they are entered by the operator and reported as such.
/// </summary>
public sealed record FeasibilityOptions
{
    public required string Environment { get; init; }

    public required string OktaOrg { get; init; }

    public required string AdIntegration { get; init; }

    /// <summary>"Group" or "Application".</summary>
    public required string AssignmentMechanism { get; init; }

    public required string AssignmentId { get; init; }

    public required string TargetConnectorId { get; init; }

    public string? ExpectedOuDistinguishedName { get; init; }

    public string? ExpectedSamPattern { get; init; }

    public IReadOnlyList<string> ExpectedGroupSids { get; init; } = [];

    public required string SyntheticLoginPrefix { get; init; }

    public required string SyntheticEmailDomain { get; init; }

    public string Department { get; init; } = "Synthetic Testing";

    public string? ManagerOktaId { get; init; }

    public string? ExistingAdUserLogin { get; init; }

    public bool OperatorConfirmedAgentOutageWindow { get; init; }

    public string SourceProfileSettings { get; init; } = "Not recorded by operator.";

    public string PushMappings { get; init; } = "Not recorded by operator.";

    public string PasswordBehaviourNotes { get; init; } = "Not recorded by operator.";

    public string LicensingNotes { get; init; } = "Not recorded by operator.";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);

    public int MaxPolls { get; init; } = 40;

    public string ProbeAttribute { get; init; } = "department";
}

public sealed record FeasibilityRunResult(
    FeasibilityOptions Options,
    bool IsMock,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    IReadOnlyList<FeasibilityCheckResult> Checks,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> UnsupportedAssumptions,
    IReadOnlyList<string> LeftForManualCleanup,
    string SessionObservation);
