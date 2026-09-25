using Ilm.Application.Abstractions;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Leavers;

public sealed class IndexModel(IIlmDbContext db) : IlmPageModel
{
    public IReadOnlyList<(LeaverRequest Request, Person? Person)> Requests { get; private set; } = [];

    public bool ActiveOnly { get; private set; }

    public async Task OnGetAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        ActiveOnly = activeOnly;
        var query = db.LeaverRequests.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(r => r.IsActive);
        }

        var requests = await query.OrderByDescending(r => r.UpdatedUtc).Take(200).ToListAsync(cancellationToken);
        var ids = requests.Select(r => r.PersonId).Distinct().ToList();
        var people = await db.Persons.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        Requests = requests.Select(r => (r, people.GetValueOrDefault(r.PersonId))).ToList();
    }
}
