namespace Ilm.Domain.Leaver;

/// <summary>
/// Decides whether a leaver is SafelyContained. Only verified controls count; attempted or acknowledged
/// authentication and directory controls do not.
/// </summary>
public static class SafelyContainedEvaluator
{
    public static SafelyContainedAssessment Evaluate(
        SafelyContainedPolicy policy,
        IReadOnlyCollection<ContainmentAction> actions,
        int unresolvedIdentityLinks)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(actions);

        var unmet = new List<string>();
        var met = new List<string>();

        var auth = actions.Where(a => a.Step == ContainmentStep.AuthenticationContainment).ToList();
        if (policy.RequireAuthenticationContainmentVerified)
        {
            if (auth.Count == 0)
            {
                unmet.Add("No authentication containment control was planned for the authoritative authentication system.");
            }

            foreach (var a in auth)
            {
                Record(a.Status == ContainmentActionStatus.Verified, $"Authentication containment verified for {a.System} {a.TargetLabel}", unmet, met);
            }
        }

        // A directory account disabled as the authentication authority also satisfies the directory control.
        var directory = actions.Where(a => a.Step == ContainmentStep.DirectoryDisable
            || (a.Step == ContainmentStep.AuthenticationContainment && a.System == Common.SystemKind.ActiveDirectory)).ToList();
        if (policy.RequireDirectoryDisableVerified)
        {
            if (directory.Count == 0)
            {
                unmet.Add("No directory disable control was planned for the authoritative directory account.");
            }

            foreach (var a in directory)
            {
                // Skipped is only planned when the person has no directory account at all.
                Record(a.Status is ContainmentActionStatus.Verified or ContainmentActionStatus.Skipped, $"Directory account disabled and verified: {a.TargetLabel}", unmet, met);
            }
        }

        foreach (var a in actions.Where(a => a.Step == ContainmentStep.SessionRevocation && a.SessionSystem is not null))
        {
            var requirement = policy.RequirementFor(a.SessionSystem!.Value);
            if (requirement != SessionRequirement.Mandatory)
            {
                continue;
            }

            // Skipped is set only by the planner when the person has no identity in that system.
            var ok = a.Status is ContainmentActionStatus.Succeeded or ContainmentActionStatus.Verified or ContainmentActionStatus.Skipped;
            Record(ok, $"Mandatory {a.SessionSystem} session revocation for {a.TargetLabel}", unmet, met);
        }

        foreach (var system in policy.SessionRequirements.Where(kv => kv.Value == SessionRequirement.Mandatory).Select(kv => kv.Key))
        {
            if (!actions.Any(a => a.SessionSystem == system))
            {
                unmet.Add($"Mandatory {system} session control was not planned.");
            }
        }

        if (policy.RequireAllIdentityLinksResolved && unresolvedIdentityLinks > 0)
        {
            unmet.Add($"{unresolvedIdentityLinks} provisional or ambiguous identity link(s) must be confirmed and contained, or rejected.");
        }

        return new SafelyContainedAssessment(unmet.Count == 0, unmet, met);
    }

    private static void Record(bool ok, string text, List<string> unmet, List<string> met)
    {
        if (ok)
        {
            met.Add(text);
        }
        else
        {
            unmet.Add(text);
        }
    }
}
