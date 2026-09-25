using Ilm.Application.Configuration;
using Ilm.Application.Security;
using Ilm.Domain.Configuration;
using Ilm.Domain.Protection;
using Ilm.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ilm.Web.Pages.Admin;

/// <summary>Imports protected objects from an approved attack-path analysis as a configuration proposal (additions only).</summary>
[RequirePermission(Permission.ProposeConfiguration)]
public sealed class ProtectionModel(IActiveConfigurationProvider active, ConfigurationService service) : IlmPageModel
{
    public ActiveConfiguration Config { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) => Config = await active.GetAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(string csv, string analysisReference, CancellationToken cancellationToken) =>
        await RunAsync(async () =>
        {
            var additions = Parse(csv, analysisReference);
            var config = await active.GetAsync(cancellationToken);
            var doc = ConfigurationSerializer.Deserialize(ConfigurationSerializer.Serialize(config.Document));
            foreach (var entry in additions.Where(a => !doc.ProtectionAdditions.Any(p => p.MatchOn == a.MatchOn && string.Equals(p.Value, a.Value, StringComparison.OrdinalIgnoreCase))))
            {
                doc.ProtectionAdditions.Add(entry);
            }

            var version = await service.ProposeAsync(ConfigurationSerializer.Serialize(doc), $"Attack-path import {analysisReference}: {additions.Count} entr(ies)", await FreshActorAsync(), cancellationToken);
            return version.Status == ConfigurationVersionStatus.ValidationFailed
                ? $"Rejected by validation: {version.ValidationIssuesJson}"
                : $"Proposed as version {version.Version} ({additions.Count} additions); awaiting Security Approver approval.";
        }, () => RedirectToPage());

    /// <summary>CSV lines: matchOn,value,category. Lines that cannot be parsed are rejected as a whole.</summary>
    public static List<ProtectedObjectDefinition> Parse(string csv, string reference)
    {
        var result = new List<ProtectedObjectDefinition>();
        foreach (var raw in (csv ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('#'))
            {
                continue;
            }

            var parts = raw.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !Enum.TryParse<ProtectedObjectMatch>(parts[0], true, out var match) || !Enum.TryParse<ProtectionCategory>(parts[2], true, out var category) || string.IsNullOrWhiteSpace(parts[1]))
            {
                throw new Domain.Common.DomainException(Domain.Common.SafeErrorCategory.ValidationFailed, $"Unparseable line: '{raw}'. Expected matchOn,value,category.");
            }

            result.Add(new ProtectedObjectDefinition { MatchOn = match, Value = parts[1], Category = category, Source = ProtectionSource.ImportedAttackPath, SourceReference = reference });
        }

        return result;
    }
}
