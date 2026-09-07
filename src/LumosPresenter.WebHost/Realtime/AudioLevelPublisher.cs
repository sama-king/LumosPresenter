using LumosPresenter.Core.Abstractions;

namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// Pushes the input level to the console over SSE instead of letting the browser ask for it.
///
/// The meter needs ~10 updates a second to look smooth, which as polling meant 10 HTTP
/// requests a second for as long as a console tab was open — ~36,000 an hour, every one of
/// them a request the server logged and a connection the browser had to find a slot for.
/// Worse, the poll had no backoff: while the server was unreachable the requests piled up
/// unresolved until the browser's connection pool ran dry (ERR_INSUFFICIENT_RESOURCES) and
/// the page could no longer load anything, even after the server came back.
///
/// Pushing over the existing /events stream costs one connection that is already open, and
/// a server that goes quiet simply sends nothing. Updates are suppressed while the value is
/// unchanged, so an idle console — no capture running, level pinned at zero — settles to one
/// keepalive a second rather than ten deltas.
/// </summary>
public sealed class AudioLevelPublisher(
    EventBroadcaster broadcaster,
    IAudioCapture capture,
    ILogger<AudioLevelPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>Resend an unchanged level this often, so a console that connects during
    /// silence still gets a reading without waiting for the mic to move.</summary>
    private static readonly TimeSpan Keepalive = TimeSpan.FromSeconds(1);

    /// <summary>Below this the change is invisible on a meter and not worth an event.</summary>
    private const float Epsilon = 0.002f;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        AudioLevel lastSent = default;
        var lastSentAt = DateTimeOffset.MinValue;
        var everSent = false;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (broadcaster.SubscriberCount == 0)
            {
                // Nothing is listening; make a connecting console send immediately.
                everSent = false;
                continue;
            }

            AudioLevel level;
            try
            {
                level = capture.CurrentLevel;
            }
            catch (Exception ex)
            {
                // A capture device can disappear mid-session (unplugged USB mic). That must
                // not take the meter loop down with it — the pipeline reports device trouble.
                logger.LogDebug(ex, "Could not read the input level");
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var moved = !everSent
                || Math.Abs(level.Peak - lastSent.Peak) >= Epsilon
                || Math.Abs(level.Rms - lastSent.Rms) >= Epsilon;
            if (!moved && now - lastSentAt < Keepalive)
            {
                continue;
            }

            broadcaster.Publish(new PipelineEvent("level", new
            {
                peak = level.Peak,
                rms = level.Rms,
                clipping = level.Peak >= 0.99f,
            }));
            lastSent = level;
            lastSentAt = now;
            everSent = true;
        }
    }
}
