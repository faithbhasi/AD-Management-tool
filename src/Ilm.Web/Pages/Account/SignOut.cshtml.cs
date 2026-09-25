using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ilm.Web.Pages.Account;

public sealed class SignOutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public IActionResult OnPost() => SignOut(
        new AuthenticationProperties { RedirectUri = "/Account/SignedOut" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
}
