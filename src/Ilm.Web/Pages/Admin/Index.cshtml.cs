using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Provisioning;
using Ilm.Application.Security;
using Ilm.Domain.Authority;
using Ilm.Domain.Protection;
using Ilm.Web.Authorization;

namespace Ilm.Web.Pages.Admin;

[RequirePermission(Permission.ViewAdministration)]
public sealed class IndexModel(IActiveConfigurationProvider configuration, ScopeEvaluator scopes, StrategyRegistry strategies) : IlmPageModel
{
    public ActiveConfiguration Config { get; private set; } = null!;

    public IReadOnlyList<ResolvedScope> Scopes { get; private set; } = [];

    public IReadOnlyDictionary<StrategyKind, bool> Strategies { get; private set; } = new Dictionary<StrategyKind, bool>();

    public static IReadOnlyList<string> Floor { get; } =
        ProtectionFloor.ProtectedBuiltinSids.OrderBy(s => s, StringComparer.Ordinal)
            .Concat(ProtectionFloor.ProtectedDomainRids.OrderBy(r => r).Select(r => $"<domain SID>-{r}"))
            .Concat(["adminCount=1", "Domain controllers (SERVER_TRUST / PARTIAL_SECRETS / primary group 516, 521)", "Unconstrained delegation", "Managed service accounts (gMSA/sMSA/dMSA)", "Directory-sync and krbtgt name prefixes", "Portal runtime identity, host, database identity, break-glass identities, gMSA password retrievers"])
            .ToList();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Config = await configuration.GetAsync(cancellationToken);
        Scopes = await scopes.ResolveScopesAsync(Config.Document.Scopes.Select(s => s.Id), cancellationToken);
        Strategies = await strategies.GetEnabledStatesAsync(cancellationToken);
    }
}
