using Ilm.Application.Abstractions;
using Ilm.Application.Configuration;
using Ilm.Application.Security;
using Ilm.Domain.Configuration;
using Ilm.Web.Authorization;
using Ilm.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Admin;

[RequirePermission(Permission.ViewAdministration)]
public sealed class ConfigurationModel(IIlmDbContext db, ConfigurationService service, IActiveConfigurationProvider active) : IlmPageModel
{
    public IReadOnlyList<ConfigurationVersion> Versions { get; private set; } = [];

    [BindProperty]
    public string DocumentJson { get; set; } = string.Empty;

    [BindProperty]
    public string Summary { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Versions = await db.ConfigurationVersions.AsNoTracking().OrderByDescending(v => v.Version).Take(50).ToListAsync(cancellationToken);
        DocumentJson = ConfigurationSerializer.Serialize((await active.GetAsync(cancellationToken)).Document);
    }

    public Task<IActionResult> OnPostProposeAsync(CancellationToken ct) => Do(async a =>
    {
        var v = await service.ProposeAsync(DocumentJson, Summary, a, ct);
        return v.Status == ConfigurationVersionStatus.ValidationFailed
            ? $"Version {v.Version} rejected by validation: {v.ValidationIssuesJson}"
            : $"Version {v.Version} proposed; a Security Approver must approve it.";
    });

    public Task<IActionResult> OnPostDecideAsync(long version, bool approve, string? comment, CancellationToken ct) => Do(async a =>
        $"Version {version}: {(await service.ApproveAsync(version, approve, comment, a, ct)).Status}.");

    public Task<IActionResult> OnPostActivateAsync(long version, CancellationToken ct) => Do(async a =>
        $"Version {(await service.ActivateAsync(version, a, ct)).Version} is now active.");

    public Task<IActionResult> OnPostRollbackAsync(long version, CancellationToken ct) => Do(async a =>
        $"Rollback proposed as version {(await service.ProposeRollbackAsync(version, a, ct)).Version}; it needs approval.");

    private async Task<IActionResult> Do(Func<ActorContext, Task<string>> action) =>
        await RunAsync(async () =>
        {
            var result = await action(await FreshActorAsync());
            IlmTelemetry.ConfigurationOperations.Add(1);
            return result;
        }, () => RedirectToPage());
}
