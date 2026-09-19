namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// Heartbeat for the live channel: every few seconds it republishes the current revision and
/// video transport to whoever is connected.
///
/// A projection surface that misses one 'live' event shows the wrong thing until someone
/// notices — and events do get missed: the SSE fan-out drops the oldest event for a client
/// whose buffer is full, and a reconnecting EventSource silently skips whatever happened while
/// it was away. Neither is recoverable from the event stream alone, because there is nothing
/// to re-deliver. The heartbeat closes that hole from the other side: a display compares the
/// revision it applied against the one in the beat and re-reads /api/live when it is behind,
/// so a stale picture lasts seconds rather than the rest of the service.
///
/// It doubles as the drift keeper for video: the beat carries the transport clock, so a
/// display that has slipped ahead or behind the console re-seeks on the next tick.
/// </summary>
public sealed class LiveSyncPublisher(EventBroadcaster broadcaster, LiveState live) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (broadcaster.SubscriberCount == 0)
            {
                continue;
            }
            var snapshot = live.Snapshot();
            broadcaster.Publish(new PipelineEvent("livesync", new
            {
                revision = snapshot.Revision,
                itemId = snapshot.Item?.Id,
                transport = snapshot.Transport,
            }));
        }
    }
}
