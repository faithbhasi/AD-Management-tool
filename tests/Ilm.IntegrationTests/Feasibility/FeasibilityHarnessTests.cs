using Ilm.Application.Abstractions;
using Ilm.Application.Feasibility;
using Ilm.Application.Provisioning;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Identity;
using Ilm.Infrastructure.Development;
using Ilm.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.IntegrationTests.Feasibility;

/// <summary>The PCATEST harness exercised against the mock Okta org and mock directory (mock evidence only).</summary>
public sealed class FeasibilityHarnessTests : IAsyncLifetime
{
    private IlmTestHost host = null!;

    public async Task InitializeAsync() => host = await IlmTestHost.CreateAsync();

    public async Task DisposeAsync() => await host.DisposeAsync();

    private static FeasibilityOptions Options(Action<FeasibilityOptions>? _ = null) => new()
    {
        Environment = "mock",
        OktaOrg = "https://okta.example.test",
        AdIntegration = "mock agent",
        AssignmentMechanism = "Group",
        AssignmentId = FictionalIds.GroupAdProvisioning,
        TargetConnectorId = FictionalIds.TargetConnector,
        ExpectedOuDistinguishedName = "OU=Finance,OU=Staff,OU=ILM Managed,DC=corp,DC=example,DC=test",
        SyntheticLoginPrefix = "ilmfeas-",
        SyntheticEmailDomain = "example.test",
        Department = "Finance",
        PollInterval = TimeSpan.Zero,
        MaxPolls = 8,
    };

    private Task<FeasibilityRunResult> Run(FeasibilityOptions options) =>
        host.WithAsync<OktaAdFeasibilityHarness, FeasibilityRunResult>(h => h.RunAsync(options, isMock: true, CancellationToken.None));

    private static CheckOutcome Outcome(FeasibilityRunResult r, int n) => r.Checks.Single(c => c.Number == n).Outcome;

    [Fact]
    public async Task Full_mock_run_exercises_all_26_checks_and_recommends_no_go()
    {
        var result = await Run(Options());
        Assert.Equal(26, result.Checks.Count);
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 1)); // staged create
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 2)); // group assignment
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 3)); // AD object created
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 5)); // target OU
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 14)); // duplicate rejected
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 18)); // deactivation disables AD
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 21)); // system log correlation
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 22)); // agent outage
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 23)); // retry without duplicates
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 24)); // link rather than duplicate
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 26)); // clean-up
        Assert.Equal(FeasibilityRecommendation.NoGo, FeasibilityReportGenerator.Recommend(result).Recommendation);
        Assert.DoesNotContain(host.Okta.Users.Values, u => u.Login.StartsWith("ilmfeas-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_ad_provisioning_is_reported_as_failure_not_assumed()
    {
        host.Okta.AdIntegration.ProvisioningGroupIds.Clear();
        var result = await Run(Options());
        Assert.Equal(CheckOutcome.Failed, Outcome(result, 3));
        Assert.Contains("did not create an AD user", result.Checks.Single(c => c.Number == 3).Observation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delayed_asynchronous_provisioning_is_measured_not_hardcoded()
    {
        host.Okta.AdIntegration.PushDelayTicks = 4;
        var result = await Run(Options());
        Assert.Equal(CheckOutcome.Passed, Outcome(result, 3));
        Assert.Equal(CheckOutcome.Observed, Outcome(result, 4));
        Assert.Contains("poll(s)", result.Checks.Single(c => c.Number == 4).Observation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_overwrite_is_detected_as_a_condition()
    {
        var result = await Run(Options());
        Assert.Contains("Overwritten", result.Checks.Single(c => c.Number == 20).Observation, StringComparison.Ordinal);
        Assert.Contains(result.Conditions, c => c.Contains("never write Okta-mapped attributes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deactivation_that_does_not_propagate_fails_with_a_condition()
    {
        host.Okta.AdIntegration.DeprovisionOnDeactivate = false;
        var result = await Run(Options());
        Assert.Equal(CheckOutcome.Failed, Outcome(result, 18));
        Assert.Contains(result.Conditions, c => c.Contains("did not disable AD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_ad_user_is_matched_not_duplicated()
    {
        var result = await Run(Options() with { ExistingAdUserLogin = "jordan.unmapped@example.test" });
        Assert.Equal(CheckOutcome.Observed, Outcome(result, 15));
        Assert.Contains("retained", result.Checks.Single(c => c.Number == 15).Observation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_revocation_is_reported()
    {
        var result = await Run(Options());
        Assert.Contains("session revocation accepted", result.SessionObservation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feasibility_service_stores_report_and_mock_cannot_be_submitted()
    {
        var run = await host.WithAsync<FeasibilityService, FeasibilityRun>(s => s.RunAsync(Options(), FeasibilityMode.Mock, ActorContext.System("test"), CancellationToken.None));
        Assert.Equal(FeasibilityRecommendation.NoGo, run.Recommendation);
        Assert.Contains("Mock run", run.ReportMarkdown, StringComparison.Ordinal);
        var casey = await host.ActorAsync(Operators.Casey);
        await Assert.ThrowsAsync<DomainException>(() => host.WithAsync<FeasibilityService, FeasibilityRun>(s => s.SubmitForApprovalAsync(run.Id, casey, CancellationToken.None)));
    }

    [Fact]
    public async Task Pcatest_run_refuses_the_simulated_okta_org_and_mock_directory()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => host.WithAsync<FeasibilityService, FeasibilityRun>(s =>
            s.RunAsync(Options(), FeasibilityMode.Pcatest, Application.Abstractions.ActorContext.System("test"), CancellationToken.None)));
        Assert.Equal(SafeErrorCategory.ValidationFailed, ex.Category);
        Assert.Contains("simulated Okta org", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Okta_provisioning_strategy_stays_disabled_without_approved_feasibility()
    {
        await using var scope = host.Scope();
        var strategy = scope.ServiceProvider.GetRequiredService<OktaApiProvisioningStrategy>();
        Assert.False(await strategy.IsEnabledAsync(CancellationToken.None));
        var context = new ProvisioningContext
        {
            Action = LifecycleAction.Joiner,
            Identity = new ExternalIdentity { System = SystemKind.Okta, Population = "target-okta" },
            Authority = new AuthorityDecision(AuthorityOutcome.Resolved, AttributeSet.CoreProfile, SystemKind.Okta, null, StrategyKind.OktaApiProvisioning, [], 1, "test"),
            Actor = ActorContext.System("test"),
        };
        var result = await strategy.ExecuteOktaJoinerAsync(context, new("new@example.test", "new@example.test", "New", "User", "Finance", null, null), FictionalIds.GroupAdProvisioning, FictionalIds.TargetConnector, TimeSpan.Zero, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
        Assert.DoesNotContain(host.Okta.Users.Values, u => u.Login == "new@example.test");
    }

    [Fact]
    public async Task Initially_enabled_strategies_are_exactly_read_only_containment_only_legacy_and_manual()
    {
        await using var scope = host.Scope();
        var states = await scope.ServiceProvider.GetRequiredService<StrategyRegistry>().GetEnabledStatesAsync(CancellationToken.None);
        Assert.True(states[StrategyKind.ReadOnly]);
        Assert.True(states[StrategyKind.ContainmentOnlyLegacy]);
        Assert.True(states[StrategyKind.ManualControlled]);
        Assert.False(states[StrategyKind.DirectActiveDirectory]);
        Assert.False(states[StrategyKind.OktaApiProvisioning]);
        Assert.False(states[StrategyKind.MigrationTransition]);
    }
}
