using System.Text.Json;
using Ilm.Application.Abstractions;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Security;

namespace Ilm.Modules.Leaver;

public sealed record CreateLeaverCommand(
    Guid PersonId,
    string Reason,
    string TicketReference,
    LeaverUrgency Urgency,
    DateTime? EffectiveUtc,
    string IdempotencyKey);

/// <summary>One planned containment control. The plan is hashed; approval binds to that hash.</summary>
public sealed record PlannedContainmentAction(
    string Key,
    ContainmentStep Step,
    ContainmentMethod Method,
    SystemKind System,
    SessionSystem? SessionSystem,
    Guid? TargetIdentityId,
    string TargetStableId,
    string TargetLabel,
    string? ConnectorId,
    string? ForestId,
    bool Mandatory,
    string AuthorityDecision,
    string ProtectionDecision,
    string? ManualReason,
    bool Skipped,
    bool Tier0);

public sealed record UnresolvedLink(Guid LinkId, Guid IdentityId, string Label, LinkConfidence Confidence, string Evidence);

public sealed record LeaverPlan(
    Guid PersonId,
    string PersonLabel,
    long ConfigurationVersion,
    IReadOnlyList<PlannedContainmentAction> Actions,
    IReadOnlyList<UnresolvedLink> UnresolvedLinks,
    AppRole RequiredApprovalRole,
    IReadOnlyList<string> Notes)
{
    /// <summary>Hash over everything material: targets, methods, mandatory flags, manual reasons, unresolved links, approver role.</summary>
    public string Hash => Hashing.Sha256Hex(string.Join("\n",
        Actions.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a =>
            $"{a.Key}|{a.Step}|{a.Method}|{a.System}|{a.SessionSystem}|{a.TargetStableId}|{a.Mandatory}|{a.Skipped}|{a.Tier0}|{a.ManualReason}"))
        + "\nunresolved:" + string.Join(",", UnresolvedLinks.Select(u => u.LinkId).OrderBy(g => g))
        + "\nrole:" + RequiredApprovalRole);

    public string ToJson() => JsonSerializer.Serialize(this, IlmJson.Compact);

    public static LeaverPlan FromJson(string json) => JsonSerializer.Deserialize<LeaverPlan>(json, IlmJson.Compact)!;
}

public sealed record LeaverOperationResult(LeaverRequest Request, string Message, bool Changed);
