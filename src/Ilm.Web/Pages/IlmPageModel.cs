using Ilm.Application.Abstractions;
using Ilm.Application.Security;
using Ilm.Domain.Common;
using Ilm.Web.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ilm.Web.Pages;

/// <summary>Base page: actor access, safe error handling and flash messages.</summary>
public abstract class IlmPageModel : PageModel
{
    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected ActorContext Actor => HttpContext.RequestServices.GetRequiredService<CurrentActorAccessor>().Current();

    protected Task<ActorContext> FreshActorAsync() =>
        HttpContext.RequestServices.GetRequiredService<CurrentActorAccessor>().FreshAsync(HttpContext.RequestAborted);

    public bool Can(Permission permission) => Permissions.Has(Actor, permission);

    /// <summary>Runs a command and converts rule violations into a safe message; never shows exception text for other errors.</summary>
    protected async Task<IActionResult> RunAsync(Func<Task<string>> command, Func<IActionResult> onDone)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(onDone);
        try
        {
            Message = await command();
        }
        catch (DomainException ex)
        {
            ErrorMessage = $"{ex.Category}: {ex.Message}";
        }

        return onDone();
    }
}
