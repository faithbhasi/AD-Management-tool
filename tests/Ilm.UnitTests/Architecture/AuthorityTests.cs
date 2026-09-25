using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Protection;

namespace Ilm.UnitTests.Architecture;

public sealed class AuthorityTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AuthorityRule Rule(SystemKind owner, SystemKind? containment = null, string? entity = null, string? wave = null, AttributeSet set = AttributeSet.AccountEnabledState) => new()
    {
        Population = "pop",
        BusinessEntity = entity,
        MigrationWave = wave,
        LifecycleAction = LifecycleAction.Leaver,
        AttributeSet = set,
        AuthoritativeSystem = owner,
        ContainmentOwner = containment ?? owner,
        ProvisioningStrategy = StrategyKind.ManualControlled,
        EffectiveFrom = Now.AddDays(-1),
    };

    private static AuthorityContext Ctx(string? entity = "E1", string? wave = "W1", AttributeSet set = AttributeSet.AccountEnabledState) =>
        new("pop", entity, wave, AccountType.Standard, LifecycleAction.Leaver, set, Now);

    [Fact]
    public void Overlapping_writers_with_identical_keys_are_rejected()
    {
        var conflicts = WriterConflictDetector.FindConflicts([Rule(SystemKind.Okta), Rule(SystemKind.ActiveDirectory)]);
        Assert.NotEmpty(conflicts);
    }

    [Fact]
    public void Incomparable_overlapping_rules_with_different_owners_are_rejected()
    {
        var conflicts = WriterConflictDetector.FindConflicts([Rule(SystemKind.Okta, entity: "E1"), Rule(SystemKind.ActiveDirectory, wave: "W1")]);
        Assert.Single(conflicts);
    }

    [Fact]
    public void Strictly_more_specific_rule_is_an_override_not_a_conflict()
    {
        var rules = new[] { Rule(SystemKind.ActiveDirectory), Rule(SystemKind.Okta, entity: "E1") };
        Assert.Empty(WriterConflictDetector.FindConflicts(rules));
        var decision = AuthorityResolver.Resolve(rules, Ctx());
        Assert.Equal(AuthorityOutcome.Resolved, decision.Outcome);
        Assert.Equal(SystemKind.Okta, decision.AuthoritativeSystem);
    }

    [Fact]
    public void Resolver_reports_conflict_instead_of_picking_silently()
    {
        var decision = AuthorityResolver.Resolve([Rule(SystemKind.Okta, entity: "E1"), Rule(SystemKind.ActiveDirectory, wave: "W1")], Ctx());
        Assert.Equal(AuthorityOutcome.Conflicting, decision.Outcome);
        Assert.Null(decision.AuthoritativeSystem);
    }

    [Fact]
    public void Missing_authority_is_reported_as_missing()
    {
        var decision = AuthorityResolver.Resolve([Rule(SystemKind.Okta, set: AttributeSet.GroupMembership)], Ctx());
        Assert.Equal(AuthorityOutcome.Missing, decision.Outcome);
        Assert.False(decision.IsResolved);
    }

    [Fact]
    public void Ownership_is_decided_per_attribute_set()
    {
        var rules = new[] { Rule(SystemKind.Okta, set: AttributeSet.CoreProfile), Rule(SystemKind.ActiveDirectory, set: AttributeSet.Password) };
        Assert.Equal(SystemKind.Okta, AuthorityResolver.Resolve(rules, Ctx(set: AttributeSet.CoreProfile)).AuthoritativeSystem);
        Assert.Equal(SystemKind.ActiveDirectory, AuthorityResolver.Resolve(rules, Ctx(set: AttributeSet.Password)).AuthoritativeSystem);
        Assert.Equal(AuthorityOutcome.Missing, AuthorityResolver.Resolve(rules, Ctx(set: AttributeSet.MailAttributes)).Outcome);
    }

    [Fact]
    public void Containment_owner_differing_from_writer_is_not_a_second_writer()
    {
        var rules = new[] { Rule(SystemKind.Okta, containment: SystemKind.ActiveDirectory) };
        Assert.Empty(WriterConflictDetector.FindConflicts(rules));
    }

    [Fact]
    public void Non_overlapping_effective_windows_do_not_conflict()
    {
        var early = Rule(SystemKind.Okta);
        early.EffectiveUntil = Now.AddDays(-1).AddHours(12);
        var later = Rule(SystemKind.ActiveDirectory);
        later.EffectiveFrom = Now.AddDays(-1).AddHours(12);
        Assert.Empty(WriterConflictDetector.FindConflicts([early, later]));
    }

    [Fact]
    public void Validator_rejects_overlapping_writers_and_missing_containment_owner()
    {
        var doc = new IlmConfigurationDocument
        {
            AuthorityRules =
            [
                new() { Population = "p", LifecycleAction = LifecycleAction.Leaver, AttributeSet = AttributeSet.AccountEnabledState, AuthoritativeSystem = SystemKind.Okta, ContainmentOwner = SystemKind.Okta },
                new() { Population = "p", LifecycleAction = LifecycleAction.Leaver, AttributeSet = AttributeSet.AccountEnabledState, AuthoritativeSystem = SystemKind.ActiveDirectory, ContainmentOwner = SystemKind.ActiveDirectory },
                new() { Population = "q", LifecycleAction = LifecycleAction.Leaver, AttributeSet = AttributeSet.AccountEnabledState, AuthoritativeSystem = SystemKind.ActiveDirectory },
            ],
        };
        var issues = ConfigurationValidator.Validate(doc, new PlatformIdentities { Complete = true }, Now);
        Assert.Contains(issues, i => i.Code == "OVERLAPPING_WRITERS");
        Assert.Contains(issues, i => i.Code == "CONTAINMENT_OWNER_MISSING");
    }
}
