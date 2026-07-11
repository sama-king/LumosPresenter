using System.Runtime.CompilerServices;
using LumosPresenter.Core.Audio;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// Debug aid: taps the raw microphone frame stream and, when enabled, writes the whole
/// session to a WAV file so a human can play back exactly what the engine heard. This is
/// the fastest way to tell an audio-capture problem (quiet, clipped, reverberant, wrong
/// device) apart from a model-accuracy problem.
/// </summary>
public sealed class SessionRecorder(ILogger<SessionRecorder> logger)
{
    private readonly string _outputDir = Path.Combine(AppContext.BaseDirectory, "debug-recordings");

    public bool Enabled { get; set; }

    /// <summary>Path of the most recently written recording, for surfacing in the UI.</summary>
    public string? LastRecordingPath { get; private set; }

    /// <summary>Pass-through over the frame stream; accumulates samples when <see cref="Enabled"/>.</summary>
    public async IAsyncEnumerable<AudioFrame> Tap(
        IAsyncEnumerable<AudioFrame> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var recording = Enabled;
        var buffer = recording ? new List<float>() : null;
        var sampleRate = 16_000;
        try
        {
            await foreach (var frame in frames.WithCancellation(cancellationToken))
            {
                if (buffer is not null)
                {
                    sampleRate = frame.SampleRate;
                    buffer.AddRange(frame.Samples.Span);
                }
                yield return frame;
            }
        }
        finally
        {
            if (buffer is { Count: > 0 })
            {
                Flush(buffer, sampleRate);
            }
        }
    }

    private void Flush(List<float> buffer, int sampleRate)
    {
        var path = Path.Combine(_outputDir, $"session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.wav");
        try
        {
            WavWriter.Write(path, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer), sampleRate);
            LastRecordingPath = path;
            logger.LogInformation("Wrote debug recording ({Seconds:0.0}s) to {Path}",
                (double)buffer.Count / sampleRate, path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write debug recording to {Path}", path);
        }
    }
}
