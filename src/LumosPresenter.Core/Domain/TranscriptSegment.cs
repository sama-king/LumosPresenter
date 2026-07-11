namespace LumosPresenter.Core.Domain;

/// <summary>
/// A piece of transcribed speech. Streaming engines (sherpa-onnx) emit partials with
/// <paramref name="IsFinal"/> = false followed by a final; chunked engines (Whisper) emit finals only.
/// </summary>
public sealed record TranscriptSegment(string Text, bool IsFinal, DateTimeOffset At);
