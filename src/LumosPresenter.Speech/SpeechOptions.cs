using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Audio;

namespace LumosPresenter.Speech;

/// <summary>Bound from the "Speech" configuration section.</summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Engine selected at startup; switchable at runtime via <see cref="ISpeechEngineProvider"/>.</summary>
    public string Engine { get; set; } = SpeechEngineNames.Whisper;

    public WhisperEngineOptions Whisper { get; set; } = new();

    public SherpaOnnxEngineOptions SherpaOnnx { get; set; } = new();

    public VadOptions Vad { get; set; } = new();
}

/// <summary>Whisper.net (whisper.cpp) — non-streaming, VAD-chunked.</summary>
public sealed class WhisperEngineOptions
{
    /// <summary>Path to a ggml model, e.g. ggml-base.en.bin from huggingface.co/ggerganov/whisper.cpp.</summary>
    public string ModelPath { get; set; } = "models/whisper/ggml-small.en.bin";

    public string Language { get; set; } = "en";

    /// <summary>Prime the decoder with scripture vocabulary (book names, "chapter", "verse").</summary>
    public bool BiasPrompt { get; set; } = true;
}

/// <summary>sherpa-onnx streaming Zipformer transducer model files.</summary>
public sealed class SherpaOnnxEngineOptions
{
    public string EncoderPath { get; set; } = "models/sherpa-onnx/encoder.onnx";

    public string DecoderPath { get; set; } = "models/sherpa-onnx/decoder.onnx";

    public string JoinerPath { get; set; } = "models/sherpa-onnx/joiner.onnx";

    public string TokensPath { get; set; } = "models/sherpa-onnx/tokens.txt";

    public int NumThreads { get; set; } = 2;
}
