using Ilm.Application.Directory;
using Ilm.Application.Security;
using Ilm.Domain.Common;
using Ilm.Modules.ReadOnly;
using Ilm.Web.Authorization;

namespace Ilm.Web.Pages.Directory;

[RequirePermission(Permission.ViewDirectory)]
public sealed class IndexModel(DirectoryViewService directory) : IlmPageModel
{
    public IReadOnlyList<ScopeRoot> Roots { get; private set; } = [];

    public IReadOnlyList<DirectoryObject> Children { get; private set; } = [];

    public string? Connector { get; private set; }

    public Guid? Ou { get; private set; }

    public async Task OnGetAsync(string? connector, Guid? ou, CancellationToken cancellationToken)
    {
        Roots = await directory.GetScopeRootsAsync(Actor, cancellationToken);
        Connector = connector;
        Ou = ou;
        if (connector is not null && ou is not null)
        {
            try
            {
                Children = await directory.ListChildOusAsync(Actor, connector, ou.Value, cancellationToken);
            }
            catch (DomainException ex)
            {
                ErrorMessage = ex.Message;
            }
        }
    }
}
