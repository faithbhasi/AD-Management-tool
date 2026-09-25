using System.Text.Json;
using Ilm.Application.Abstractions;
using Ilm.Application.Approvals;
using Ilm.Application.Audit;
using Ilm.Application.Configuration;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Feasibility;

/// <summary>Runs the harness, stores the report and manages its Security Approver approval.</summary>
public sealed class FeasibilityService(
    IIlmDbContext db,
    OktaAdFeasibilityHarness harness,
    ApprovalService approvals,
    IActiveConfigurationProvider configuration,
    IEnumerable<Okta.IFeasibilityEnvironmentControl> environmentControls,
    IAuditWriter audit,
    TimeProvider time)
{
    public async Task<FeasibilityRun> RunAsync(FeasibilityOptions options, FeasibilityMode mode, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(options);
        if (mode == FeasibilityMode.Pcatest)
        {
            // Evidence labelled PCATEST must come from PCATEST: never from the simulated Okta org or a mock directory.
            if (environmentControls.Any(c => c.IsSimulated))
            {
                throw new DomainException(SafeErrorCategory.ValidationFailed, "A PCATEST run cannot use the simulated Okta org. Configure Ilm:Okta:Mode=Live against the PCATEST org.");
            }

            var connector = (await configuration.GetAsync(cancellationToken)).FindConnector(options.TargetConnectorId);
            if (connector is null || !string.Equals(connector.Implementation, "Ldap", StringComparison.Ordinal))
            {
                throw new DomainException(SafeErrorCategory.ValidationFailed, $"A PCATEST run must target a live LDAP connector; '{options.TargetConnectorId}' is {connector?.Implementation ?? "not configured"}.");
            }
        }

        var run = new FeasibilityRun
        {
            Mode = mode,
            Environment = options.Environment,
            StartedUtc = time.GetUtcNow().UtcDateTime,
            StartedByUserId = actor.AppUserId ?? Guid.Empty,
        };
        var result = await harness.RunAsync(options, mode == FeasibilityMode.Mock, cancellationToken);
        var (recommendation, _) = FeasibilityReportGenerator.Recommend(result);
        run.CompletedUtc = result.CompletedUtc;
        run.ResultsJson = JsonSerializer.Serialize(result.Checks, IlmJson.Compact);
        run.ReportMarkdown = FeasibilityReportGenerator.Generate(result, run.Id);
        run.ReportSha256 = Hashing.Sha256Hex(run.ReportMarkdown);
        run.Recommendation = recommendation;
        run.Status = FeasibilityRunStatus.Draft;
        db.FeasibilityRuns.Add(run);
        audit.Append(new AuditEvent
        {
            Action = "FeasibilityRunCompleted",
            Result = FeasibilityReportGenerator.Label(recommendation),
            OperationId = run.Id,
            TargetStableId = $"feasibility:{mode}:{options.Environment}",
            AppliedValues = new { run.ReportSha256, passed = result.Checks.Count(c => c.Outcome == CheckOutcome.Passed), total = result.Checks.Count },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<FeasibilityRun> SubmitForApprovalAsync(Guid runId, ActorContext actor, CancellationToken cancellationToken)
    {
        var run = await db.FeasibilityRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Feasibility run not found.");
        if (run.Mode == FeasibilityMode.Mock)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "Mock runs cannot be submitted for approval.");
        }

        var active = await configuration.GetAsync(cancellationToken);
        var approval = approvals.Create(ApprovalSubjectType.FeasibilityReport, run.Id.ToString("D"), $"Okta-to-AD feasibility report ({FeasibilityReportGenerator.Label(run.Recommendation)})",
            AppRole.SecurityApprover, actor, run.ReportSha256, TimeSpan.FromDays(7), active.Version);
        run.ApprovalId = approval.Id;
        run.Status = FeasibilityRunStatus.AwaitingApproval;
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<FeasibilityRun> DecideAsync(Guid runId, bool approve, string? comment, ActorContext actor, CancellationToken cancellationToken)
    {
        var run = await db.FeasibilityRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Feasibility run not found.");
        if (run.ApprovalId is null)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "The run has not been submitted for approval.");
        }

        var approval = await approvals.DecideAsync(run.ApprovalId.Value, approve, comment, actor, Hashing.Sha256Hex(run.ReportMarkdown), cancellationToken);
        run.Status = approval.Status == ApprovalStatus.Approved ? FeasibilityRunStatus.Approved : FeasibilityRunStatus.Rejected;
        run.ApprovedByUserId = approval.Status == ApprovalStatus.Approved ? actor.AppUserId : null;
        run.ApprovedUtc = approval.DecidedUtc;
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }
}
