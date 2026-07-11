namespace LumosPresenter.Core.Domain;

/// <summary>A block of mono PCM samples (normalized floats) from the capture device.</summary>
public sealed record AudioFrame(ReadOnlyMemory<float> Samples, int SampleRate, DateTimeOffset CapturedAt);
