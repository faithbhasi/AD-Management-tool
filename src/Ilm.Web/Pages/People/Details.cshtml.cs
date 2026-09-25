using Ilm.Application.Identity;
using Ilm.Application.Security;
using Ilm.Domain.Identity;
using Ilm.Modules.ReadOnly;
using Ilm.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ilm.Web.Pages.People;

[RequirePermission(Permission.ViewPeople)]
public sealed class DetailsModel(PersonViewService people, IdentityLinkService links) : IlmPageModel
{
    public PersonView View { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            View = await people.GetAsync(Actor, id, cancellationToken);
            return Page();
        }
        catch (Domain.Common.DomainException ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToPage("/People/Index");
        }
    }

    public async Task<IActionResult> OnPostApproveLinkAsync(Guid id, Guid linkId, CancellationToken cancellationToken) =>
        await RunAsync(async () =>
        {
            var link = await links.ApproveAsync(linkId, await FreshActorAsync(), cancellationToken);
            return $"Link approved ({link.Confidence}).";
        }, () => RedirectToPage(new { id }));

    public async Task<IActionResult> OnPostRejectLinkAsync(Guid id, Guid linkId, string reason, CancellationToken cancellationToken) =>
        await RunAsync(async () =>
        {
            await links.RejectAsync(linkId, string.IsNullOrWhiteSpace(reason) ? "Rejected by approver." : reason, await FreshActorAsync(), cancellationToken);
            return "Link rejected.";
        }, () => RedirectToPage(new { id }));

    public static string LinkBadge(LinkConfidence c) => c switch
    {
        LinkConfidence.Authoritative or LinkConfidence.HumanApproved => "ok",
        LinkConfidence.Rejected => "muted",
        _ => "warn",
    };
}
