using Ilm.Application.Feasibility;
using Ilm.Domain.Feasibility;

namespace Ilm.UnitTests.Feasibility;

public sealed class FeasibilityReportTests
{
    private static FeasibilityRunResult Result(bool mock, Func<int, CheckOutcome> outcome, params string[] conditions) => new(
        new FeasibilityOptions
        {
            Environment = "PCATEST", OktaOrg = "https://pcatest.okta.example.test", AdIntegration = "AD", AssignmentMechanism = "Group",
            AssignmentId = "00gAdPushTarget00001", TargetConnectorId = "corp", SyntheticLoginPrefix = "ilmfeas-", SyntheticEmailDomain = "example.test",
        },
        mock,
        DateTime.UtcNow,
        DateTime.UtcNow,
        Enumerable.Range(1, 26).Select(n => new FeasibilityCheckResult(n, OktaAdFeasibilityHarness.CheckTitles[n - 1], outcome(n), "obs", 0)).ToList(),
        conditions,
        [],
        [],
        "session ok");

    [Fact]
    public void Mock_evidence_is_always_no_go() =>
        Assert.Equal(FeasibilityRecommendation.NoGo, FeasibilityReportGenerator.Recommend(Result(true, _ => CheckOutcome.Passed)).Recommendation);

    [Fact]
    public void Core_failure_is_no_go() =>
        Assert.Equal(FeasibilityRecommendation.NoGo, FeasibilityReportGenerator.Recommend(Result(false, n => n == 3 ? CheckOutcome.Failed : CheckOutcome.Passed)).Recommendation);

    [Fact]
    public void Conditions_make_it_conditional() =>
        Assert.Equal(FeasibilityRecommendation.ConditionalGo, FeasibilityReportGenerator.Recommend(Result(false, _ => CheckOutcome.Passed, "Push overwrites ILM changes.")).Recommendation);

    [Fact]
    public void All_passed_without_conditions_is_go() =>
        Assert.Equal(FeasibilityRecommendation.Go, FeasibilityReportGenerator.Recommend(Result(false, _ => CheckOutcome.Passed)).Recommendation);

    [Fact]
    public void Report_contains_every_required_section()
    {
        var report = FeasibilityReportGenerator.Generate(Result(false, _ => CheckOutcome.Passed), Guid.NewGuid());
        string[] sections =
        [
            "Tested Okta org", "Tested AD integration", "Assignment mechanism", "Source and profile settings", "Push mappings",
            "Target OU behaviour", "Create result", "Update result", "Suspension result", "Deactivation result", "Reactivation result",
            "Session result", "Duplicate result", "Timing observations", "Failure behaviour", "Licensing dependencies discovered",
            "Unsupported assumptions", "Final recommendation",
        ];
        foreach (var s in sections)
        {
            Assert.Contains(s, report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void There_are_exactly_26_checks() => Assert.Equal(26, OktaAdFeasibilityHarness.CheckTitles.Count);
}
