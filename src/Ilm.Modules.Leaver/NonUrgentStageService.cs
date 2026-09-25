using Ilm.Application.Abstractions;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Tasks;
using Ilm.Domain.Common;
using Ilm.Domain.Leaver;
using Ilm.Domain.Security;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Modules.Leaver;

/// <summary>
/// Creates the non-urgent stages after SafelyContained. Nothing here deletes accounts, clears groups wholesale,
/// clears mail or proxyAddresses, changes sourceAnchor, touches SIDHistory, deletes mailbox data or removes licences
/// outside the approved retention policy. Connectors for these systems are disabled, so each stage is a manual task.
/// </summary>
public sealed class NonUrgentStageService(
    IIlmDbContext db,
    IDirectoryConnectorRegistry directories,
    IActiveConfigurationProvider configuration,
    ManualTaskService manualTasks)
{
    public const string RetentionPrefix = "[Retention]";
    public const string OwnershipPrefix = "[Ownership]";

    private const string Prohibited =
        "\n\n**Never:** delete the account, clear all groups, clear mail or proxyAddresses, change sourceAnchor, change immutable links, " +
        "remove SIDHistory, delete mailbox data, or remove licences outside the approved retention policy.";

    public async Task CreateTasksAsync(LeaverRequest request, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await db.ManualTasks.AnyAsync(t => t.LeaverRequestId == request.Id && t.Kind == ManualTaskKind.NonUrgentLeaverStage, cancellationToken))
        {
            return;
        }

        var config = await configuration.GetAsync(cancellationToken);
        var retention = config.Document.LifecyclePolicies.Retention;
        var groupPlan = await BuildGroupRemovalPlanAsync(request, config, cancellationToken);
        var sla = TimeSpan.FromDays(5);
        void Task(string prefix, string title, string body) =>
            manualTasks.Create(ManualTaskKind.NonUrgentLeaverStage, AlertSeverity.Low, $"{prefix} {title}", body + Prohibited,
                new { requestId = request.Id }, sla, nameof(AppRole.LifecycleOperator), false, request.Id, null, actor);

        Task(RetentionPrefix, "Apply AD group-removal policy", groupPlan);
        Task(RetentionPrefix, "Mailbox ownership or delegation", "Assign mailbox ownership or delegation per the retention policy. Convert to a shared mailbox only if the retention policy says so. Do not delete mailbox data.");
        Task(RetentionPrefix, "Licence review", retention.RemoveLicencesAutomatically
            ? "Review licences against the approved retention policy before removal."
            : "Review licences. The approved retention policy does not permit automatic removal; record the decision only.");
        Task(RetentionPrefix, "Reconcile Mimecast policy state", "Confirm the Mimecast profile/policy groups reflect the leaver state (Mimecast connector is disabled).");
        Task(RetentionPrefix, "Reconcile Citrix entitlement state", "Confirm Citrix entitlements are removed or suspended per policy (Citrix lifecycle connector is disabled).");
        Task(OwnershipPrefix, "Data ownership transfer", "Transfer ownership of files, sites and shared data to the named successor.");
        Task(OwnershipPrefix, "Device return", "Track return of devices assigned to the person.");
        Task(OwnershipPrefix, "Application ownership transfer", "Transfer ownership of applications, service accounts and groups the person managed.");
    }

    private async Task<string> BuildGroupRemovalPlanAsync(LeaverRequest request, ActiveConfiguration config, CancellationToken cancellationToken)
    {
        var preserved = config.Document.LifecyclePolicies.GroupRemoval.PreserveGroupGuids
            .Select(g => Guid.TryParse(g, out var guid) ? guid : Guid.Empty).ToHashSet();
        var lines = new List<string> { "Group-removal plan (review; removal is manual because no connector permits group changes):", string.Empty };
        var identities = await db.ExternalIdentities.AsNoTracking()
            .Where(i => i.PersonId == request.PersonId && i.System == SystemKind.ActiveDirectory && i.ConnectorId != null && i.ObjectGuid != null)
            .ToListAsync(cancellationToken);
        foreach (var identity in identities)
        {
            lines.Add($"**{identity.DisplayLabel}** ({identity.ConnectorId}):");
            try
            {
                var reader = directories.GetReader(identity.ConnectorId!);
                var membership = await reader.GetTransitiveGroupsAsync(identity.ObjectGuid!.Value, cancellationToken);
                foreach (var guid in membership.GroupGuids)
                {
                    var group = await reader.GetByGuidAsync(guid, null, cancellationToken);
                    var name = group?.Name ?? guid.ToString("D");
                    var action = preserved.Contains(guid) ? "PRESERVE (legal hold, investigation or migration)"
                        : group?.ObjectSid?.EndsWith("-513", StringComparison.Ordinal) == true ? "KEEP (primary group)"
                        : config.Document.LifecyclePolicies.GroupRemoval.RemoveNonPreservedGroups ? "remove (confirm direct membership first)" : "review";
                    lines.Add($"- {name}: {action}");
                }
            }
            catch (Exception ex) when (ex is IOException or KeyNotFoundException)
            {
                lines.Add($"- Membership could not be read ({ex.GetType().Name}); review manually.");
            }
        }

        return string.Join("\n", lines);
    }
}
