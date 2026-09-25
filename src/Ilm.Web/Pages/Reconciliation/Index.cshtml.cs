using Ilm.Application.Abstractions;
using Ilm.Application.Reconciliation;
using Ilm.Application.Security;
using Ilm.Domain.Reconciliation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Reconciliation;

public sealed class IndexModel(IIlmDbContext db, ReconciliationService reconciliation) : IlmPageModel
{
    public IReadOnlyList<ReconciliationRun> Runs { get; private set; } = [];

    public IReadOnlyList<ReconciliationFinding> Findings { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Runs = await db.ReconciliationRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).Take(20).ToListAsync(cancellationToken);
        var latest = Runs.FirstOrDefault()?.Id;
        Findings = latest is null ? [] : await db.ReconciliationFindings.AsNoTracking().Where(f => f.RunId == latest).OrderByDescending(f => f.Severity).ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        if (!Can(Permission.RunReconciliation))
        {
            return Forbid();
        }

        return await RunAsync(async () =>
        {
            var run = await reconciliation.RunAsync("manual", await FreshActorAsync(), cancellationToken);
            return $"Reconciliation checked {run.IdentitiesChecked} identities; {run.FindingsCount} finding(s).";
        }, () => RedirectToPage());
    }
}
