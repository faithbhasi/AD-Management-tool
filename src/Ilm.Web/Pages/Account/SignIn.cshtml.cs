using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ilm.Web.Pages.Account;

/// <summary>Starts Okta OIDC sign-in. There is no password form anywhere in ILM.</summary>
public sealed class SignInModel : PageModel
{
    public IActionResult OnGet(string? returnUrl = null)
    {
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        return Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
