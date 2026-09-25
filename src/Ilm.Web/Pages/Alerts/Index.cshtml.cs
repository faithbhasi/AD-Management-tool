using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Domain.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Alerts;

public sealed class IndexModel(IIlmDbContext db, IAuditWriter audit, TimeProvider time) : IlmPageModel
{
    public IReadOnlyList<Alert> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Items = await db.Alerts.AsNoTracking().OrderByDescending(a => a.CreatedUtc).Take(300).ToListAsync(cancellationToken);

    public async Task<IActionResult> OnPostAcknowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is not null && alert.AcknowledgedUtc is null)
        {
            var actor = Actor;
            alert.AcknowledgedUtc = time.GetUtcNow().UtcDateTime;
            alert.AcknowledgedByUserId = actor.AppUserId;
            audit.Append(new AuditEvent { Action = "AlertAcknowledged", Result = "Acknowledged", OperationId = alert.OperationId, TargetStableId = alert.Id.ToString("D") }, actor);
            await db.SaveChangesAsync(cancellationToken);
            Message = "Alert acknowledged. Acknowledging does not resolve the underlying task.";
        }

        return RedirectToPage();
    }
}
