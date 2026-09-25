using Ilm.Application.Security;
using Ilm.Domain.Identity;
using Ilm.Modules.ReadOnly;
using Ilm.Web.Authorization;

namespace Ilm.Web.Pages.People;

[RequirePermission(Permission.ViewPeople)]
public sealed class IndexModel(PersonViewService people) : IlmPageModel
{
    public string? Q { get; private set; }

    public IReadOnlyList<Person> Results { get; private set; } = [];

    public async Task OnGetAsync(string? q, CancellationToken cancellationToken)
    {
        Q = q;
        Results = await people.SearchAsync(Actor, q, cancellationToken);
    }
}
