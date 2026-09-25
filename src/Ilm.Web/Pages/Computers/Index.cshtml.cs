using Ilm.Application.Directory;
using Ilm.Application.Security;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Modules.ReadOnly;
using Ilm.Web.Authorization;

namespace Ilm.Web.Pages.Computers;

[RequirePermission(Permission.ViewDirectory)]
public sealed class IndexModel(DirectoryViewService directory) : IlmPageModel
{
    public string? Q { get; private set; }

    public IReadOnlyList<DirectoryObject> Results { get; private set; } = [];

    public async Task OnGetAsync(string? q, CancellationToken cancellationToken)
    {
        Q = q;
        try
        {
            Results = await directory.SearchAsync(Actor, DirectoryObjectKind.Computer, q, cancellationToken);
        }
        catch (DomainException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
