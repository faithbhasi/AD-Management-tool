using Ilm.Application.Abstractions;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.Application.Configuration;

/// <summary>Loads the single Active configuration version and caches it briefly per process.</summary>
public sealed class ActiveConfigurationProvider(IServiceScopeFactory scopes, TimeProvider time) : IActiveConfigurationProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim gate = new(1, 1);
    private ActiveConfiguration? cached;
    private DateTime cachedUntilUtc;

    public async Task<ActiveConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (cached is not null && now < cachedUntilUtc)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && now < cachedUntilUtc)
            {
                return cached;
            }

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
            var active = await db.ConfigurationVersions.AsNoTracking()
                .Where(v => v.Status == ConfigurationVersionStatus.Active)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DomainException(SafeErrorCategory.ValidationFailed, "No active configuration exists. Run the bootstrap.");

            cached = new ActiveConfiguration(active.Version, ConfigurationSerializer.Deserialize(active.DocumentJson));
            cachedUntilUtc = now.Add(CacheLifetime);
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate()
    {
        cached = null;
        cachedUntilUtc = DateTime.MinValue;
    }
}
