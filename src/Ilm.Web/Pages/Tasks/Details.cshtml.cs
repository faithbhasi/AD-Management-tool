using Ilm.Application.Abstractions;
using Ilm.Domain.Tasks;
using Ilm.Modules.Leaver;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Tasks;

public sealed class DetailsModel(IIlmDbContext db, LeaverWorkflowService workflow) : IlmPageModel
{
    public ManualTask Item { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await db.ManualTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        Item = task;
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteAsync(Guid id, string evidence, CancellationToken cancellationToken) =>
        await RunAsync(async () =>
        {
            var result = await workflow.RecordManualTaskAsync(id, evidence, await FreshActorAsync(), cancellationToken);
            return result.Message;
        }, () => RedirectToPage(new { id }));
}
