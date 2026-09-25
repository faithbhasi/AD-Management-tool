using Ilm.Application.Abstractions;
using Ilm.Application.Security;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Modules.Leaver;
using Ilm.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Leavers;

[RequirePermission(Permission.RequestLeaver)]
public sealed class NewModel(IIlmDbContext db, LeaverWorkflowService workflow, ContainmentPlanner planner) : IlmPageModel
{
    public Person Person { get; private set; } = null!;

    public LeaverPlan? Preview { get; private set; }

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    [BindProperty]
    public string TicketReference { get; set; } = string.Empty;

    [BindProperty]
    public LeaverUrgency Urgency { get; set; } = LeaverUrgency.Planned;

    [BindProperty]
    public DateTime? EffectiveUtc { get; set; }

    [BindProperty]
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    public async Task<IActionResult> OnGetAsync(Guid personId, CancellationToken cancellationToken)
    {
        var person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }

        Person = person;
        Preview = await planner.BuildAsync(personId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid personId, CancellationToken cancellationToken)
    {
        Guid? created = null;
        return await RunAsync(async () =>
        {
            var effective = EffectiveUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : (DateTime?)null;
            var result = await workflow.CreateAsync(new CreateLeaverCommand(personId, Reason, TicketReference, Urgency, effective, IdempotencyKey), await FreshActorAsync(), cancellationToken);
            created = result.Request.Id;
            return result.Message;
        }, () => created is null ? RedirectToPage(new { personId }) : RedirectToPage("/Leavers/Details", new { id = created }));
    }
}
