namespace Ilm.Domain.Configuration;

/// <summary>
/// Every feature flag ILM understands. There is deliberately no flag for Tier 0 management:
/// no configuration can enable it, and unknown flag names are rejected.
/// </summary>
public enum Feature
{
    // Initially enabled
    LegacyContainment = 0,
    OktaContainment,
    SessionRevocation,
    Reconciliation,

    // Initially disabled
    StandardUserCreation,
    AdministrativeUserCreation,
    PasswordReset,
    UserMove,
    ComputerDisableAndMove,
    OktaUserProvisioning,
    OktaToAdProvisioning,
    EntraLifecycle,
    ExchangeLifecycle,
    CitrixLifecycle,
    MimecastLifecycle,
    DirectActiveDirectoryContainment,
    EmergencyDirectoryOverride,
    MigrationTransition,
}

public static class FeatureDefaults
{
    public static readonly IReadOnlySet<Feature> EnabledByDefault = new HashSet<Feature>
    {
        Feature.LegacyContainment,
        Feature.OktaContainment,
        Feature.SessionRevocation,
        Feature.Reconciliation,
    };

    /// <summary>Features that must not be enabled without an approved Okta-to-AD feasibility report.</summary>
    public static readonly IReadOnlySet<Feature> RequireApprovedFeasibility = new HashSet<Feature>
    {
        Feature.OktaUserProvisioning,
        Feature.OktaToAdProvisioning,
    };

    public static bool IsEnabled(IReadOnlyDictionary<string, bool> flags, Feature feature)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return flags.TryGetValue(feature.ToString(), out var enabled) ? enabled : EnabledByDefault.Contains(feature);
    }
}
