using System.Globalization;
using System.Text;
using Ilm.Domain.Feasibility;

namespace Ilm.Application.Feasibility;

/// <summary>Produces OKTA-AD-FEASIBILITY-REPORT.md and the Go / Conditional Go / No-Go recommendation.</summary>
public static class FeasibilityReportGenerator
{
    /// <summary>Checks whose failure makes the path unusable.</summary>
    private static readonly int[] CoreChecks = [1, 2, 3, 14, 17, 21, 23, 24];

    public static (FeasibilityRecommendation Recommendation, string Rationale) Recommend(FeasibilityRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsMock)
        {
            return (FeasibilityRecommendation.NoGo, "Mock evidence only. A recommendation other than No-Go requires a PCATEST run.");
        }

        var failedCore = result.Checks.Where(c => CoreChecks.Contains(c.Number) && c.Outcome != CheckOutcome.Passed).ToList();
        if (failedCore.Count > 0)
        {
            return (FeasibilityRecommendation.NoGo, "Core checks did not pass: " + string.Join(", ", failedCore.Select(c => $"#{c.Number}")) + ".");
        }

        var notPassed = result.Checks.Where(c => c.Outcome is CheckOutcome.Failed or CheckOutcome.Skipped or CheckOutcome.Inconclusive).ToList();
        if (notPassed.Count > 0 || result.Conditions.Count > 0)
        {
            return (FeasibilityRecommendation.ConditionalGo,
                $"Core path works; {notPassed.Count} non-core check(s) not passed and {result.Conditions.Count} condition(s) must be met before activation.");
        }

        return (FeasibilityRecommendation.Go, "All checks passed with no conditions.");
    }

    public static string Generate(FeasibilityRunResult result, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(result);
        var (recommendation, rationale) = Recommend(result);
        var o = result.Options;
        var sb = new StringBuilder();
        string Row(int n) => Find(result, n);

        sb.AppendLine("# Okta-to-AD provisioning feasibility report");
        sb.AppendLine();
        if (result.IsMock)
        {
            sb.AppendLine("> **Mock run.** This report was generated against the in-process mock Okta org and mock directory.");
            sb.AppendLine("> It demonstrates the harness and report format only. It is **not** evidence about PCATEST or production");
            sb.AppendLine("> behaviour, and it can never support enabling Okta provisioning.");
            sb.AppendLine();
        }

        sb.AppendLine("| Item | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Run ID | `{runId:D}` |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Environment | {o.Environment} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Tested Okta org | {o.OktaOrg} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Tested AD integration | {o.AdIntegration} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Assignment mechanism | {o.AssignmentMechanism} `{o.AssignmentId}` |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Target connector | {o.TargetConnectorId} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Started / completed (UTC) | {result.StartedUtc:u} / {result.CompletedUtc:u} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| **Recommendation** | **{Label(recommendation)}** |");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Rationale:** {rationale}");
        sb.AppendLine();

        sb.AppendLine("## Source and profile settings");
        sb.AppendLine();
        sb.AppendLine(o.SourceProfileSettings);
        sb.AppendLine();
        sb.AppendLine("## Push mappings");
        sb.AppendLine();
        sb.AppendLine(o.PushMappings);
        sb.AppendLine();
        sb.AppendLine("## Target OU behaviour");
        sb.AppendLine();
        sb.AppendLine(Row(5));
        sb.AppendLine();
        sb.AppendLine("## Create result");
        sb.AppendLine();
        sb.AppendLine(string.Join(Environment.NewLine, new[] { 1, 2, 3, 6, 7, 8, 9, 10, 11, 12, 13 }.Select(n => "- " + Row(n))));
        sb.AppendLine();
        sb.AppendLine("## Update result");
        sb.AppendLine();
        sb.AppendLine("- " + Row(20));
        sb.AppendLine();
        sb.AppendLine("## Suspension result");
        sb.AppendLine();
        sb.AppendLine("- " + Row(16));
        sb.AppendLine();
        sb.AppendLine("## Deactivation result");
        sb.AppendLine();
        sb.AppendLine("- " + Row(17));
        sb.AppendLine("- " + Row(18));
        sb.AppendLine();
        sb.AppendLine("## Reactivation result");
        sb.AppendLine();
        sb.AppendLine("- " + Row(19));
        sb.AppendLine("- " + Row(25));
        sb.AppendLine();
        sb.AppendLine("## Session result");
        sb.AppendLine();
        sb.AppendLine(result.SessionObservation);
        sb.AppendLine();
        sb.AppendLine("## Duplicate result");
        sb.AppendLine();
        sb.AppendLine(string.Join(Environment.NewLine, new[] { 14, 15, 23, 24 }.Select(n => "- " + Row(n))));
        sb.AppendLine();
        sb.AppendLine("## Timing observations");
        sb.AppendLine();
        sb.AppendLine("- " + Row(4));
        sb.AppendLine("- No expected provisioning duration is assumed by ILM; workflows poll and time out with an alert.");
        sb.AppendLine();
        sb.AppendLine("## Failure behaviour");
        sb.AppendLine();
        sb.AppendLine("- " + Row(22));
        sb.AppendLine("- " + Row(21));
        sb.AppendLine();
        sb.AppendLine("## Licensing dependencies discovered");
        sb.AppendLine();
        sb.AppendLine(o.LicensingNotes);
        sb.AppendLine();
        sb.AppendLine("## Unsupported assumptions");
        sb.AppendLine();
        foreach (var u in result.UnsupportedAssumptions)
        {
            sb.AppendLine("- " + u);
        }

        sb.AppendLine();
        sb.AppendLine("## Conditions for activation");
        sb.AppendLine();
        if (result.Conditions.Count == 0)
        {
            sb.AppendLine("- None recorded.");
        }

        foreach (var c in result.Conditions)
        {
            sb.AppendLine("- " + c);
        }

        sb.AppendLine();
        sb.AppendLine("## Clean-up");
        sb.AppendLine();
        sb.AppendLine("- " + Row(26));
        foreach (var left in result.LeftForManualCleanup)
        {
            sb.AppendLine("- " + left);
        }

        sb.AppendLine();
        sb.AppendLine("## All checks");
        sb.AppendLine();
        sb.AppendLine("| # | Check | Outcome | Observation |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var c in result.Checks)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {c.Number} | {c.Title} | {c.Outcome} | {c.Observation.Replace("|", "\\|", StringComparison.Ordinal)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Final recommendation");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**{Label(recommendation)}.** {rationale}");
        sb.AppendLine();
        sb.AppendLine("OktaApiProvisioningStrategy stays disabled until a Security Approver approves a PCATEST report with a Go or Conditional Go");
        sb.AppendLine("recommendation and an approved configuration version references it by run ID and report hash.");
        return sb.ToString();
    }

    public static string Label(FeasibilityRecommendation recommendation) => recommendation switch
    {
        FeasibilityRecommendation.Go => "Go",
        FeasibilityRecommendation.ConditionalGo => "Conditional Go",
        FeasibilityRecommendation.NoGo => "No-Go",
        _ => "Not assessed",
    };

    private static string Find(FeasibilityRunResult result, int number)
    {
        var c = result.Checks.FirstOrDefault(x => x.Number == number);
        return c is null ? $"#{number}: not run." : $"#{c.Number} {c.Title}: **{c.Outcome}**. {c.Observation}";
    }
}
