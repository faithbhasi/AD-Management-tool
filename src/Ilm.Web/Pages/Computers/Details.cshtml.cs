using Ilm.Application.Security;
using Ilm.Domain.Common;
using Ilm.Modules.ReadOnly;
using Ilm.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ilm.Web.Pages.Computers;

[RequirePermission(Permission.ViewDirectory)]
public sealed class DetailsModel(DirectoryViewService directory) : IlmPageModel
{
    public DirectoryObjectView View { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string connector, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            View = await directory.GetObjectAsync(Actor, connector, id, cancellationToken);
            return Page();
        }
        catch (DomainException ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToPage("/Computers/Index");
        }
    }
}
