using Ilm.Domain.Common;
using Ilm.Domain.Leaver;

namespace Ilm.UnitTests.Leaver;

public sealed class SafelyContainedEvaluatorTests
{
    private static ContainmentAction A(ContainmentStep step, SystemKind system, ContainmentActionStatus status, SessionSystem? session = null, bool mandatory = true) =>
        new() { Step = step, System = system, Status = status, SessionSystem = session, Mandatory = mandatory, TargetLabel = $"{system}" };

    private static List<ContainmentAction> AllGood() =>
    [
        A(ContainmentStep.AuthenticationContainment, SystemKind.Okta, ContainmentActionStatus.Verified),
        A(ContainmentStep.DirectoryDisable, SystemKind.ActiveDirectory, ContainmentActionStatus.Verified),
        A(ContainmentStep.SessionRevocation, SystemKind.Okta, ContainmentActionStatus.Succeeded, SessionSystem.Okta),
        A(ContainmentStep.SessionRevocation, SystemKind.Entra, ContainmentActionStatus.Succeeded, SessionSystem.Entra),
        A(ContainmentStep.SessionRevocation, SystemKind.Citrix, ContainmentActionStatus.Succeeded, SessionSystem.Citrix),
        A(ContainmentStep.SessionRevocation, SystemKind.Vpn, ContainmentActionStatus.NotConfigured, SessionSystem.Vpn, mandatory: false),
    ];

    [Fact]
    public void All_mandatory_controls_verified_is_safely_contained()
    {
        var result = SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), AllGood(), 0);
        Assert.True(result.IsSafelyContained, string.Join("; ", result.UnmetRequirements));
    }

    [Fact]
    public void Attempted_but_unverified_authentication_is_not_enough()
    {
        var actions = AllGood();
        actions[0].Status = ContainmentActionStatus.Succeeded;
        Assert.False(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), actions, 0).IsSafelyContained);
    }

    [Fact]
    public void Unverified_directory_disable_is_not_enough()
    {
        var actions = AllGood();
        actions[1].Status = ContainmentActionStatus.InProgress;
        Assert.False(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), actions, 0).IsSafelyContained);
    }

    [Fact]
    public void Mandatory_session_failure_blocks_but_best_effort_does_not()
    {
        var actions = AllGood();
        actions[3].Status = ContainmentActionStatus.Failed;
        Assert.False(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), actions, 0).IsSafelyContained);

        var bestEffortOnly = AllGood();
        bestEffortOnly[5].Status = ContainmentActionStatus.Failed;
        Assert.True(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), bestEffortOnly, 0).IsSafelyContained);
    }

    [Fact]
    public void Unresolved_identity_links_block()
    {
        var result = SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), AllGood(), 1);
        Assert.False(result.IsSafelyContained);
        Assert.Contains(result.UnmetRequirements, r => r.Contains("identity link", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_authentication_or_directory_control_blocks()
    {
        Assert.False(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), AllGood().Skip(1).ToList(), 0).IsSafelyContained);
        Assert.False(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), AllGood().Where(a => a.Step != ContainmentStep.DirectoryDisable).ToList(), 0).IsSafelyContained);
    }

    [Fact]
    public void Legacy_directory_account_as_authentication_authority_satisfies_both_controls()
    {
        List<ContainmentAction> actions =
        [
            A(ContainmentStep.AuthenticationContainment, SystemKind.ActiveDirectory, ContainmentActionStatus.Verified),
            A(ContainmentStep.SessionRevocation, SystemKind.Okta, ContainmentActionStatus.Skipped, SessionSystem.Okta),
            A(ContainmentStep.SessionRevocation, SystemKind.Entra, ContainmentActionStatus.Succeeded, SessionSystem.Entra),
            A(ContainmentStep.SessionRevocation, SystemKind.Citrix, ContainmentActionStatus.Succeeded, SessionSystem.Citrix),
        ];
        Assert.True(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), actions, 0).IsSafelyContained);
    }

    [Fact]
    public void Manual_action_awaiting_is_not_contained()
    {
        var actions = AllGood();
        actions[1].Status = ContainmentActionStatus.ManualActionRequired;
        Assert.False(SafelyContainedEvaluator.Evaluate(new SafelyContainedPolicy(), actions, 0).IsSafelyContained);
    }
}
