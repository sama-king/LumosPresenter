using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>The most recent input signal level, for a meter and clip detection.</summary>
/// <param name="Peak">Peak absolute sample in the last frame (0..1+; >=1 means clipping).</param>
/// <param name="Rms">RMS level of the last frame (0..1).</param>
public readonly record struct AudioLevel(float Peak, float Rms);

/// <summary>
/// Microphone capture. The PortAudio implementation lives in the Audio project;
/// tests substitute a fake that replays golden audio files.
/// </summary>
public interface IAudioCapture : IAsyncDisposable
{
    /// <summary>
    /// Input device id to capture from, or null for the OS default. Applied on the next
    /// <see cref="CaptureAsync"/>; changing it mid-capture has no effect until restart.
    /// </summary>
    int? DeviceId { get; set; }

    /// <summary>The most recent input level, updated continuously while capturing.</summary>
    AudioLevel CurrentLevel { get; }

    /// <summary>Starts capture and yields frames until cancelled.</summary>
    IAsyncEnumerable<AudioFrame> CaptureAsync(CancellationToken cancellationToken = default);
}
