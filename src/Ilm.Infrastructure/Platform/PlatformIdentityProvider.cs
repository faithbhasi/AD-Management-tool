using Ilm.Application.Protection;
using Ilm.Domain.Protection;
using Microsoft.Extensions.Options;

namespace Ilm.Infrastructure.Platform;

/// <summary>
/// Deployment-time description of the portal's own identities. Lives in host configuration (not the
/// approved configuration document) so that no UI or configuration version can remove them.
/// </summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Ilm:Platform";

    public List<string> RuntimeIdentitySids { get; set; } = [];

    public List<string> HostComputerSids { get; set; } = [];

    public List<string> HostDnsNames { get; set; } = [];

    public List<string> DatabaseServiceIdentitySids { get; set; } = [];

    public List<string> BreakGlassSids { get; set; } = [];

    /// <summary>Principals in the portal gMSA's PrincipalsAllowedToRetrieveManagedPassword.</summary>
    public List<string> ManagedPasswordRetrieverSids { get; set; } = [];

    /// <summary>Set only after the retriever list has been verified against the directory for this deployment.</summary>
    public bool ManagedPasswordRetrieversVerified { get; set; }
}

public sealed class PlatformIdentityProvider(IOptions<PlatformOptions> options) : IPlatformIdentityProvider
{
    public Task<PlatformIdentities> GetAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var runtime = new List<string>(o.RuntimeIdentitySids);
        if (OperatingSystem.IsWindows())
        {
            using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (current.User?.Value is { } sid)
            {
                runtime.Add(sid);
            }
        }

        return Task.FromResult(new PlatformIdentities
        {
            RuntimeIdentitySids = runtime.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            HostComputerSids = o.HostComputerSids,
            HostDnsNames = o.HostDnsNames,
            DatabaseServiceIdentitySids = o.DatabaseServiceIdentitySids,
            BreakGlassSids = o.BreakGlassSids,
            ManagedPasswordRetrieverSids = o.ManagedPasswordRetrieverSids,
            Complete = o.ManagedPasswordRetrieversVerified && runtime.Count > 0,
        });
    }
}
