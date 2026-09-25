using Ilm.Domain.Common;

namespace Ilm.Domain.Authority;

public enum AuthorityOutcome
{
    Resolved = 0,
    Missing,
    Conflicting,
}

/// <summary>The resolved owner of one attribute set, or why none could be resolved.</summary>
public sealed record AuthorityDecision(
    AuthorityOutcome Outcome,
    AttributeSet AttributeSet,
    SystemKind? AuthoritativeSystem,
    SystemKind? ContainmentOwner,
    StrategyKind? Strategy,
    IReadOnlyList<Guid> RuleIds,
    long? ConfigurationVersion,
    string Explanation)
{
    public bool IsResolved => Outcome == AuthorityOutcome.Resolved;

    public string ToAuditString() => Outcome switch
    {
        AuthorityOutcome.Resolved =>
            $"{AttributeSet}:Resolved:{AuthoritativeSystem}:containment={ContainmentOwner?.ToString() ?? "-"}:strategy={Strategy}:v{ConfigurationVersion}",
        _ => $"{AttributeSet}:{Outcome}:{Explanation}",
    };

    public static AuthorityDecision Missing(AttributeSet set, string explanation) =>
        new(AuthorityOutcome.Missing, set, null, null, null, [], null, explanation);
}
