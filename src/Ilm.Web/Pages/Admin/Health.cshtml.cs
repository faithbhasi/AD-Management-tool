using Ilm.Application.Health;
using Ilm.Application.Security;
using Ilm.Web.Authorization;

namespace Ilm.Web.Pages.Admin;

[RequirePermission(Permission.ViewAdministration)]
public sealed class HealthModel(IConnectorHealthSource health) : IlmPageModel
{
    public IReadOnlyList<ComponentHealth> Components { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => Components = await health.CheckAsync(cancellationToken);
}
