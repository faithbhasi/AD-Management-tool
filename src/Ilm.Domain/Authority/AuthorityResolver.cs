namespace Ilm.Domain.Authority;

/// <summary>
/// Resolves the owning system for one attribute set. The most specific matching rule wins;
/// incomparable rules with different outcomes are a conflict, never a silent pick.
/// </summary>
public static class AuthorityResolver
{
    public static AuthorityDecision Resolve(IEnumerable<AuthorityRule> rules, AuthorityContext context)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(context);

        var candidates = rules.Where(r => r.Matches(context)).ToList();
        if (candidates.Count == 0)
        {
            return AuthorityDecision.Missing(
                context.AttributeSet,
                $"No effective authority rule for population '{context.Population}', action {context.LifecycleAction}, set {context.AttributeSet}.");
        }

        var mostSpecific = candidates
            .Where(c => !candidates.Any(other => !ReferenceEquals(other, c) && other.IsStrictlyMoreSpecificThan(c)))
            .ToList();

        var first = mostSpecific[0];
        if (mostSpecific.Skip(1).Any(r => !r.SameOutcome(first)))
        {
            return new AuthorityDecision(
                AuthorityOutcome.Conflicting,
                context.AttributeSet,
                null,
                null,
                null,
                mostSpecific.Select(r => r.Id).ToList(),
                null,
                "Multiple equally specific rules assign different owners: " + string.Join("; ", mostSpecific.Select(r => r.Describe())));
        }

        return new AuthorityDecision(
            AuthorityOutcome.Resolved,
            context.AttributeSet,
            first.AuthoritativeSystem,
            first.ContainmentOwner,
            first.ProvisioningStrategy,
            mostSpecific.Select(r => r.Id).ToList(),
            first.ApprovedConfigurationVersion,
            first.Describe());
    }
}
