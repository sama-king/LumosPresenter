using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// A speech-to-text engine. Implementations (Whisper.net, sherpa-onnx) live in the Speech
/// project and are selected at runtime; the parser downstream consumes text only, so
/// engines are interchangeable without touching Core.
/// </summary>
public interface ISpeechEngine : IAsyncDisposable
{
    /// <summary>Stable identifier used for engine selection ("whisper", "sherpa-onnx").</summary>
    string Name { get; }

    /// <summary>
    /// Why this engine cannot transcribe right now, or null when it is ready. Models load
    /// lazily inside <see cref="TranscribeAsync"/>, which runs on the pipeline's background
    /// task — so without a check up front a missing model surfaces only in the log, long
    /// after the caller was told the start succeeded. Cheap enough to call per request.
    /// </summary>
    string? ReadinessError { get; }

    /// <summary>Consumes captured audio and yields transcript segments as they become available.</summary>
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> audio,
        CancellationToken cancellationToken = default);
}
