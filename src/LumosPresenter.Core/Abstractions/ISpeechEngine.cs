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

    /// <summary>Consumes captured audio and yields transcript segments as they become available.</summary>
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> audio,
        CancellationToken cancellationToken = default);
}
