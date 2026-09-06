using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LumosPresenter.Data.Remote;

/// <summary>
/// Deletes expired cached chapters daily. Startup purging alone is not enough: a machine
/// left running for weeks — the normal state for a church presentation box — would never
/// reclaim anything.
/// </summary>
public sealed class CachePurgeService(
    RemoteChapterCache cache, ILogger<CachePurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                cache.PurgeExpired();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed purge is not worth taking the app down for; it retries tomorrow.
                logger.LogWarning(ex, "Scheduled cache purge failed");
            }
        }
    }
}
