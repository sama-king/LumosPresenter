namespace LumosPresenter.Core.Abstractions;

/// <summary>Stable engine identifiers used in configuration and the operator console.</summary>
public static class SpeechEngineNames
{
    public const string Whisper = "whisper";
    public const string SherpaOnnx = "sherpa-onnx";
}

/// <summary>
/// Runtime engine selection: the operator console switches the active engine
/// without a restart; the pipeline always transcribes through <see cref="Current"/>.
/// </summary>
public interface ISpeechEngineProvider
{
    ISpeechEngine Current { get; }

    IReadOnlyList<string> AvailableEngines { get; }

    /// <summary>Makes the named engine current. Throws <see cref="ArgumentException"/> if unknown.</summary>
    void Select(string name);
}
