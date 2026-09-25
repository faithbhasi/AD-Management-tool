using Ilm.Application.Abstractions;
using Ilm.Application.Security;
using Ilm.Domain.Approvals;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Tasks;
using Ilm.Modules.Leaver;
using Ilm.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Leavers;

public sealed class DetailsModel(IIlmDbContext db, LeaverWorkflowService workflow) : IlmPageModel
{
    public LeaverRequest Leaver { get; private set; } = null!;

    public Person? Person { get; private set; }

    public LeaverPlan? Plan { get; private set; }

    public Approval? Approval { get; private set; }

    public IReadOnlyList<LeaverTransition> Transitions { get; private set; } = [];

    public IReadOnlyList<ManualTask> Tasks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.LeaverRequests.AsNoTracking().Include(r => r.Actions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        Leaver = request;
        Person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PersonId, cancellationToken);
        Plan = request.PlanJson is null ? null : LeaverPlan.FromJson(request.PlanJson);
        Approval = request.ApprovalId is null ? null : await db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.ApprovalId, cancellationToken);
        Transitions = await db.LeaverTransitions.AsNoTracking().Where(t => t.LeaverRequestId == id).OrderBy(t => t.Ordinal).ToListAsync(cancellationToken);
        Tasks = await db.ManualTasks.AsNoTracking().Where(t => t.LeaverRequestId == id).OrderBy(t => t.CreatedUtc).ToListAsync(cancellationToken);
        return Page();
    }

    public Task<IActionResult> OnPostApproveAsync(Guid id, string? comment, CancellationToken ct) => Do(id, async a => (await workflow.DecideAsync(id, true, comment, a, ct)).Message);

    public Task<IActionResult> OnPostRejectAsync(Guid id, string? comment, CancellationToken ct) => Do(id, async a => (await workflow.DecideAsync(id, false, comment, a, ct)).Message);

    public Task<IActionResult> OnPostStartAsync(Guid id, CancellationToken ct) => Do(id, async a => (await workflow.StartContainmentAsync(id, a, ct)).Message);

    public Task<IActionResult> OnPostReverifyAsync(Guid id, CancellationToken ct) => Do(id, async a => (await workflow.ReverifyAsync(id, a, ct)).Message);

    public Task<IActionResult> OnPostAdvanceAsync(Guid id, CancellationToken ct) => Do(id, async a => (await workflow.AdvanceAsync(id, a, ct)).Message);

    public Task<IActionResult> OnPostCancelAsync(Guid id, string? reason, CancellationToken ct) => Do(id, async a => (await workflow.CancelAsync(id, reason ?? "Cancelled by operator.", a, ct)).Message);

    public Task<IActionResult> OnPostRollbackAsync(Guid id, string? reason, CancellationToken ct) => Do(id, async a => (await workflow.RequestRollbackAsync(id, reason ?? "Rollback requested.", a, ct)).Message);

    private async Task<IActionResult> Do(Guid id, Func<ActorContext, Task<string>> action) =>
        await RunAsync(async () =>
        {
            var result = await action(await FreshActorAsync());
            IlmTelemetry.LeaverOperations.Add(1);
            return result;
        }, () => RedirectToPage(new { id }));

    public bool CanDecide => Approval?.Status == ApprovalStatus.Pending && Leaver.State == LeaverState.AwaitingApproval && Can(Permission.ApproveLeaver);
}
