namespace Ilm.Application.Abstractions;

/// <summary>A lease-based application lock that works across ILM instances.</summary>
public interface IDistributedLock
{
    /// <summary>Returns a handle, or null when another owner holds an unexpired lease.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan leaseDuration, CancellationToken cancellationToken);
}
