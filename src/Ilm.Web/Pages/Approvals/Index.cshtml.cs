using Ilm.Application.Abstractions;
using Ilm.Application.Security;
using Ilm.Domain.Approvals;
using Ilm.Web.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Approvals;

[RequirePermission(Permission.ViewApprovals)]
public sealed class IndexModel(IIlmDbContext db) : IlmPageModel
{
    public ApprovalStatus Status { get; private set; }

    public IReadOnlyList<Approval> Items { get; private set; } = [];

    public async Task OnGetAsync(ApprovalStatus status = ApprovalStatus.Pending, Guid? id = null, CancellationToken cancellationToken = default)
    {
        Status = status;
        var query = db.Approvals.AsNoTracking();
        query = id is null ? query.Where(a => a.Status == status) : query.Where(a => a.Id == id);
        Items = await query.OrderByDescending(a => a.CreatedUtc).Take(200).ToListAsync(cancellationToken);
    }

    public static string Link(Approval a) => a.SubjectType switch
    {
        ApprovalSubjectType.LeaverRequest => "/Leavers/Details/" + a.SubjectId.Split(':')[0],
        ApprovalSubjectType.ConfigurationVersion => "/Admin/Configuration",
        ApprovalSubjectType.FeasibilityReport => "/Admin/Feasibility",
        _ => "/Approvals",
    };
}
