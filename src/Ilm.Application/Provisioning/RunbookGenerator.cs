using System.Globalization;
using System.Text;
using Ilm.Domain.Identity;

namespace Ilm.Application.Provisioning;

public sealed record RunbookTarget(
    string System,
    string ConnectorOrTenant,
    string StableId,
    string? ObjectSid,
    string? LastKnownDistinguishedName,
    string? Label);

/// <summary>Generates precise, target-specific manual containment runbooks.</summary>
public static class RunbookGenerator
{
    public static string ManualContainment(
        string personLabel,
        Guid operationId,
        string reason,
        IReadOnlyList<RunbookTarget> targets,
        string operatorTier,
        DateTime? slaDueUtc,
        bool tier0)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Manual containment: {personLabel}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Operation: `{operationId:D}`");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Why this is manual: {reason}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Performed by: {operatorTier}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- SLA due (UTC): {slaDueUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "not set"}");
        sb.AppendLine();
        if (tier0)
        {
            sb.AppendLine("> Tier 0 object. Perform this only from a Tier 0 privileged access workstation. ILM will not act on it.");
            sb.AppendLine();
        }

        sb.AppendLine("## Targets");
        sb.AppendLine();
        sb.AppendLine("| System | Connector / tenant | Stable ID | SID | Last known DN | Label |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var t in targets)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {t.System} | {t.ConnectorOrTenant} | `{t.StableId}` | {t.ObjectSid ?? "-"} | {t.LastKnownDistinguishedName ?? "-"} | {t.Label ?? "-"} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Steps");
        sb.AppendLine();
        sb.AppendLine("1. Locate each target by its **stable ID** (objectGUID or Okta user ID), not by name or DN.");
        sb.AppendLine("2. Directory accounts: set the ACCOUNTDISABLE bit only. Do not delete, move, rename, reset the password, or change groups.");
        sb.AppendLine("3. Okta users: apply the approved containment action (suspend or deactivate) and clear the user's sessions.");
        sb.AppendLine("4. Session systems: end live sessions (Entra sign-in sessions, Citrix sessions, VPN) for each target.");
        sb.AppendLine("5. Record the change ticket as completion evidence in ILM. ILM re-reads each target and closes the task only when it observes the contained state.");
        sb.AppendLine();
        sb.AppendLine("## Do not");
        sb.AppendLine();
        sb.AppendLine("- Delete any account, clear mail or proxyAddresses, change sourceAnchor, remove SIDHistory, or remove licences.");
        sb.AppendLine("- Mark this task complete before the change is actually made.");
        return sb.ToString();
    }

    public static RunbookTarget FromIdentity(ExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new RunbookTarget(
            identity.System.ToString(),
            identity.ConnectorId ?? identity.ForestOrTenantId,
            identity.StableObjectId,
            identity.ObjectSid,
            identity.DistinguishedName,
            identity.DisplayLabel);
    }
}
