using Ilm.Domain.Common;

namespace Ilm.Domain.Authority;

/// <summary>
/// Declares which system owns one attribute set for one population and lifecycle action.
/// Null <see cref="BusinessEntity"/>, <see cref="MigrationWave"/> and <see cref="AccountType"/> mean "any".
/// </summary>
public sealed class AuthorityRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Population { get; set; } = string.Empty;

    public string? BusinessEntity { get; set; }

    public string? MigrationWave { get; set; }

    public AccountType? AccountType { get; set; }

    public LifecycleAction LifecycleAction { get; set; }

    public AttributeSet AttributeSet { get; set; }

    public SystemKind AuthoritativeSystem { get; set; }

    /// <summary>
    /// The system that performs containment for this set. Mandatory for <see cref="AttributeSet.AccountEnabledState"/>.
    /// Containment by this owner is not treated as a second writer.
    /// </summary>
    public SystemKind? ContainmentOwner { get; set; }

    public StrategyKind ProvisioningStrategy { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveUntil { get; set; }

    public long ApprovedConfigurationVersion { get; set; }

    public bool IsEffective(DateTime atUtc) =>
        EffectiveFrom <= atUtc && (EffectiveUntil is null || EffectiveUntil > atUtc);

    public bool Matches(AuthorityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return string.Equals(Population, context.Population, StringComparison.OrdinalIgnoreCase)
            && LifecycleAction == context.LifecycleAction
            && AttributeSet == context.AttributeSet
            && (BusinessEntity is null || string.Equals(BusinessEntity, context.BusinessEntity, StringComparison.OrdinalIgnoreCase))
            && (MigrationWave is null || string.Equals(MigrationWave, context.MigrationWave, StringComparison.OrdinalIgnoreCase))
            && (AccountType is null || AccountType == context.AccountType)
            && IsEffective(context.AtUtc);
    }

    internal int Specificity =>
        (BusinessEntity is null ? 0 : 1) + (MigrationWave is null ? 0 : 1) + (AccountType is null ? 0 : 1);

    /// <summary>True when this rule is a strict specialisation of <paramref name="other"/>.</summary>
    internal bool IsStrictlyMoreSpecificThan(AuthorityRule other)
    {
        static bool Covers(string? general, string? specific) =>
            general is null || (specific is not null && string.Equals(general, specific, StringComparison.OrdinalIgnoreCase));

        var coversAll = Covers(other.BusinessEntity, BusinessEntity)
            && Covers(other.MigrationWave, MigrationWave)
            && (other.AccountType is null || (AccountType is not null && other.AccountType == AccountType));

        return coversAll && Specificity > other.Specificity;
    }

    internal bool SameOutcome(AuthorityRule other) =>
        AuthoritativeSystem == other.AuthoritativeSystem
        && ContainmentOwner == other.ContainmentOwner
        && ProvisioningStrategy == other.ProvisioningStrategy;

    public string Describe() =>
        $"{Population}/{BusinessEntity ?? "*"}/{MigrationWave ?? "*"}/{AccountType?.ToString() ?? "*"} {LifecycleAction} {AttributeSet} -> {AuthoritativeSystem}"
        + (ContainmentOwner is null ? string.Empty : $" (containment: {ContainmentOwner})")
        + $" via {ProvisioningStrategy}";
}

public sealed record AuthorityContext(
    string Population,
    string? BusinessEntity,
    string? MigrationWave,
    AccountType AccountType,
    LifecycleAction LifecycleAction,
    AttributeSet AttributeSet,
    DateTime AtUtc);
