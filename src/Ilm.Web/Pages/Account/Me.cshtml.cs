using System.Security.Claims;
using Ilm.Web.Authentication;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ilm.Web.Pages.Account;

public sealed class MeModel : PageModel
{
    public string Issuer { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string? RoleSource { get; private set; }

    public string? RoleFailure { get; private set; }

    public IReadOnlyDictionary<Ilm.Domain.Security.AppRole, IReadOnlyCollection<string>> Roles { get; private set; } = new Dictionary<Ilm.Domain.Security.AppRole, IReadOnlyCollection<string>>();

    public void OnGet()
    {
        Issuer = User.FindFirstValue(IlmClaimTypes.Issuer) ?? string.Empty;
        Subject = User.FindFirstValue(IlmClaimTypes.Subject) ?? string.Empty;
        RoleSource = User.FindFirstValue(IlmClaimTypes.RoleSource);
        RoleFailure = User.FindFirstValue(IlmClaimTypes.RoleResolutionFailed);
        Roles = CurrentActorAccessor.RolesFromClaims(User);
    }
}
