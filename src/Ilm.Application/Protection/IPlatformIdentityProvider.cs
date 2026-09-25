using Ilm.Domain.Protection;

namespace Ilm.Application.Protection;

/// <summary>Supplies the portal's own identities, which are permanently protected.</summary>
public interface IPlatformIdentityProvider
{
    Task<PlatformIdentities> GetAsync(CancellationToken cancellationToken);
}
