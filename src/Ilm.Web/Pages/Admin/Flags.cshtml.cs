using Ilm.Application.Configuration;
using Ilm.Application.Security;
using Ilm.Domain.Configuration;
using Ilm.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ilm.Web.Pages.Admin;

[RequirePermission(Permission.ProposeConfiguration)]
public sealed class FlagsModel(IActiveConfigurationProvider active, ConfigurationService service) : IlmPageModel
{
    public ActiveConfiguration Config { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) => Config = await active.GetAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(string[] enabled, string summary, CancellationToken cancellationToken) =>
        await RunAsync(async () =>
        {
            var config = await active.GetAsync(cancellationToken);
            var doc = ConfigurationSerializer.Deserialize(ConfigurationSerializer.Serialize(config.Document));
            doc.FeatureFlags = Enum.GetValues<Feature>().ToDictionary(f => f.ToString(), f => enabled.Contains(f.ToString(), StringComparer.Ordinal), StringComparer.Ordinal);
            var version = await service.ProposeAsync(ConfigurationSerializer.Serialize(doc), string.IsNullOrWhiteSpace(summary) ? "Feature flag change" : summary, await FreshActorAsync(), cancellationToken);
            return version.Status == ConfigurationVersionStatus.ValidationFailed
                ? $"Rejected by validation: {version.ValidationIssuesJson}"
                : $"Proposed as version {version.Version}; awaiting Security Approver approval.";
        }, () => RedirectToPage());
}
