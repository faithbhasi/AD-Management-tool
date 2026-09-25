using Ilm.Application.Abstractions;
using Ilm.Application.Configuration;
using Ilm.Application.Security;
using Ilm.Domain.Configuration;
using Ilm.Domain.Protection;

namespace Ilm.Modules.ReadOnly;

/// <summary>Explains, for each lifecycle action, whether it is available to this operator now and why not.</summary>
public static class PermittedActionsEvaluator
{
    public static IReadOnlyList<PermittedAction> ForPerson(ActorContext actor, ActiveConfiguration config, bool hasActiveLeaver, bool anyIdentityInScope)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(config);
        var actions = new List<PermittedAction>
        {
            Leaver(actor, hasActiveLeaver, anyIdentityInScope),
            Disabled("Create standard user", config, Feature.StandardUserCreation),
            Disabled("Create administrative user", config, Feature.AdministrativeUserCreation),
            Disabled("Reset password", config, Feature.PasswordReset),
            Disabled("Move user", config, Feature.UserMove),
            Disabled("Provision in Okta", config, Feature.OktaUserProvisioning),
            Disabled("Entra lifecycle actions", config, Feature.EntraLifecycle),
            Disabled("Exchange lifecycle actions", config, Feature.ExchangeLifecycle),
            Disabled("Citrix lifecycle actions", config, Feature.CitrixLifecycle),
            Disabled("Mimecast lifecycle actions", config, Feature.MimecastLifecycle),
        };
        return actions;
    }

    public static IReadOnlyList<PermittedAction> ForDirectoryUser(ActorContext actor, ActiveConfiguration config, ProtectionDecision protection, bool inScope, bool linkedToPerson, bool hasActiveLeaver)
    {
        ArgumentNullException.ThrowIfNull(protection);
        var list = new List<PermittedAction>();
        if (protection.Status == ProtectionStatus.Protected)
        {
            list.Add(new("Any automated lifecycle action", false, "Protected Tier 0 or Tier 0-adjacent object. ILM never manages it; Tier 0 work is manual on a PAW."));
        }
        else if (protection.Status == ProtectionStatus.Unknown)
        {
            list.Add(new("Any automated lifecycle action", false, "Protection could not be conclusively evaluated; automation is denied and containment becomes a manual task."));
        }

        list.Add(!linkedToPerson
            ? new("Initiate containment", false, "The account is not linked to a person. Link it first.")
            : Leaver(actor, hasActiveLeaver, inScope));
        list.Add(Disabled("Reset password", config, Feature.PasswordReset));
        list.Add(Disabled("Move user", config, Feature.UserMove));
        return list;
    }

    public static IReadOnlyList<PermittedAction> ForComputer(ActorContext actor, ActiveConfiguration config, ProtectionDecision protection)
    {
        ArgumentNullException.ThrowIfNull(protection);
        var list = new List<PermittedAction>();
        if (protection.Status != ProtectionStatus.Clear)
        {
            list.Add(new("Any automated computer action", false, protection.Status == ProtectionStatus.Protected
                ? "Protected server or Tier 0 computer (for example a DC, CA, Okta agent host or the portal host)."
                : "Protection could not be conclusively evaluated."));
        }

        list.Add(Disabled("Disable computer", config, Feature.ComputerDisableAndMove));
        list.Add(Disabled("Move computer", config, Feature.ComputerDisableAndMove));
        _ = actor;
        return list;
    }

    private static PermittedAction Leaver(ActorContext actor, bool hasActiveLeaver, bool inScope)
    {
        if (!Permissions.Has(actor, Permission.RequestLeaver))
        {
            return new("Initiate containment", false, "Requires the Lifecycle Operator role.");
        }

        if (hasActiveLeaver)
        {
            return new("Initiate containment", false, "An active leaver request already exists for this person.");
        }

        return inScope
            ? new("Initiate containment", true, "Available.")
            : new("Initiate containment", false, "None of the person's accounts are within your OU scopes.");
    }

    private static PermittedAction Disabled(string name, ActiveConfiguration config, Feature feature) =>
        config.IsEnabled(feature)
            ? new(name, false, $"{feature} is enabled but its workflow module is not yet implemented in this release.")
            : new(name, false, $"Disabled by feature flag {feature} (initially disabled; enabling requires an approved configuration version).");
}
