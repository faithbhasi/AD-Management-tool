using Ilm.Application.Abstractions;
using Ilm.Application.Feasibility;
using Ilm.Application.Security;
using Ilm.Domain.Feasibility;
using Ilm.Web.Authorization;
using Ilm.Web.Cli;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Admin;

[RequirePermission(Permission.ViewAdministration)]
public sealed class FeasibilityModel(IIlmDbContext db, FeasibilityService feasibility) : IlmPageModel
{
    public IReadOnlyList<FeasibilityRun> Runs { get; private set; } = [];

    public FeasibilityRun? Selected { get; private set; }

    public async Task OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        Runs = await db.FeasibilityRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).Take(20).ToListAsync(cancellationToken);
        Selected = id is null ? Runs.FirstOrDefault() : Runs.FirstOrDefault(r => r.Id == id);
    }

    public async Task<IActionResult> OnPostRunMockAsync(CancellationToken cancellationToken)
    {
        if (!Can(Permission.RunFeasibility))
        {
            return Forbid();
        }

        Guid? id = null;
        return await RunAsync(async () =>
        {
            var run = await feasibility.RunAsync(CommandRunner.MockFeasibilityOptions(), FeasibilityMode.Mock, await FreshActorAsync(), cancellationToken);
            id = run.Id;
            return $"Mock run complete: {FeasibilityReportGenerator.Label(run.Recommendation)} (mock evidence can never support activation).";
        }, () => RedirectToPage(new { id }));
    }

    public Task<IActionResult> OnPostSubmitAsync(Guid id, CancellationToken ct) =>
        RunAsync(async () => $"Submitted: {(await feasibility.SubmitForApprovalAsync(id, await FreshActorAsync(), ct)).Status}.", () => RedirectToPage(new { id }));

    public Task<IActionResult> OnPostDecideAsync(Guid id, bool approve, CancellationToken ct) =>
        RunAsync(async () => $"Report {(await feasibility.DecideAsync(id, approve, null, await FreshActorAsync(), ct)).Status}.", () => RedirectToPage(new { id }));
}
