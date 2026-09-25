namespace Ilm.Domain.Authority;

/// <summary>
/// Detects rule sets that would give one population, action and attribute set more than one active writer.
/// A strictly more specific rule is an intentional override, not a conflict. Identical keys are always rejected.
/// </summary>
public static class WriterConflictDetector
{
    public static IReadOnlyList<string> FindConflicts(IReadOnlyList<AuthorityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var conflicts = new List<string>();

        for (var i = 0; i < rules.Count; i++)
        {
            for (var j = i + 1; j < rules.Count; j++)
            {
                var a = rules[i];
                var b = rules[j];
                if (!CanOverlap(a, b))
                {
                    continue;
                }

                var identicalKey = SameKey(a, b);
                if (identicalKey)
                {
                    conflicts.Add($"Duplicate writer definition: [{a.Describe()}] and [{b.Describe()}].");
                    continue;
                }

                if (a.IsStrictlyMoreSpecificThan(b) || b.IsStrictlyMoreSpecificThan(a))
                {
                    continue;
                }

                if (!a.SameOutcome(b))
                {
                    conflicts.Add($"Overlapping writers: [{a.Describe()}] and [{b.Describe()}] can both apply to the same identity.");
                }
            }
        }

        return conflicts;
    }

    private static bool CanOverlap(AuthorityRule a, AuthorityRule b)
    {
        static bool DimensionOverlaps(string? x, string? y) =>
            x is null || y is null || string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        return string.Equals(a.Population, b.Population, StringComparison.OrdinalIgnoreCase)
            && a.LifecycleAction == b.LifecycleAction
            && a.AttributeSet == b.AttributeSet
            && DimensionOverlaps(a.BusinessEntity, b.BusinessEntity)
            && DimensionOverlaps(a.MigrationWave, b.MigrationWave)
            && (a.AccountType is null || b.AccountType is null || a.AccountType == b.AccountType)
            && WindowsOverlap(a, b);
    }

    private static bool SameKey(AuthorityRule a, AuthorityRule b) =>
        string.Equals(a.BusinessEntity, b.BusinessEntity, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.MigrationWave, b.MigrationWave, StringComparison.OrdinalIgnoreCase)
        && a.AccountType == b.AccountType;

    private static bool WindowsOverlap(AuthorityRule a, AuthorityRule b)
    {
        var aEnd = a.EffectiveUntil ?? DateTime.MaxValue;
        var bEnd = b.EffectiveUntil ?? DateTime.MaxValue;
        return a.EffectiveFrom < bEnd && b.EffectiveFrom < aEnd;
    }
}
