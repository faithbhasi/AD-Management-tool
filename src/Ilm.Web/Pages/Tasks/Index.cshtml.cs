using Ilm.Application.Abstractions;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Tasks;

public sealed class IndexModel(IIlmDbContext db) : IlmPageModel
{
    public bool OpenOnly { get; private set; }

    public IReadOnlyList<ManualTask> Items { get; private set; } = [];

    public async Task OnGetAsync(bool openOnly = true, CancellationToken cancellationToken = default)
    {
        OpenOnly = openOnly;
        var query = db.ManualTasks.AsNoTracking();
        if (openOnly)
        {
            query = query.Where(t => t.Status != ManualTaskStatus.Completed && t.Status != ManualTaskStatus.Cancelled);
        }

        Items = await query.OrderByDescending(t => t.Severity).ThenBy(t => t.SlaDueUtc).Take(300).ToListAsync(cancellationToken);
    }
}
