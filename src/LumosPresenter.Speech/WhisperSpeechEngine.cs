using System.Runtime.CompilerServices;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Audio;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LumosPresenter.Speech;

/// <summary>
/// Whisper.net engine (whisper.cpp with CoreML/Metal on Apple Silicon). Non-streaming:
/// audio is VAD-chunked into utterances and each chunk transcribed whole, so segments
/// are always final. Expected end-to-end latency ~3–4 s.
/// </summary>
public sealed class WhisperSpeechEngine(IOptions<SpeechOptions> options) : ISpeechEngine
{
    private readonly SpeechOptions _options = options.Value;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _loadedModelPath;

    public string Name => SpeechEngineNames.Whisper;

    /// <summary>
    /// Which native runtime Whisper.net actually loaded ("Vulkan", "Cpu", "CoreML"…), or
    /// null before the first model load — the native library is resolved lazily, so this
    /// only becomes known once an engine has run. Reported to the console so an operator
    /// can see whether GPU acceleration is in play rather than inferring it from speed.
    /// </summary>
    public static string? LoadedRuntime => RuntimeOptions.LoadedLibrary?.ToString();

    public string? ReadinessError =>
        File.Exists(_options.Whisper.ModelPath) ? null : MissingModelMessage(_options.Whisper.ModelPath);

    private static string MissingModelMessage(string modelPath) =>
        $"Whisper model not found at '{modelPath}'. Download a ggml model " +
        "(e.g. ggml-base.en.bin from huggingface.co/ggerganov/whisper.cpp) " +
        "and set Speech:Whisper:ModelPath.";

    /// <summary>Absolute path of the ggml model to load. Setting a new value reloads on next use.</summary>
    public string ModelPath
    {
        get => _options.Whisper.ModelPath;
        set => _options.Whisper.ModelPath = value;
    }

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> audio,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync();

        await foreach (var chunk in VoiceActivityChunker.ChunkAsync(audio, _options.Vad, cancellationToken))
        {
            await foreach (var segment in _processor!.ProcessAsync(chunk.Samples.ToArray(), cancellationToken))
            {
                var text = segment.Text.Trim();
                if (text.Length > 0)
                {
                    yield return new TranscriptSegment(text, IsFinal: true, DateTimeOffset.UtcNow);
                }
            }
        }
    }

    private async ValueTask EnsureLoadedAsync()
    {
        var modelPath = _options.Whisper.ModelPath;
        if (_processor is not null && _loadedModelPath == modelPath)
        {
            return;
        }
        // Whisper.net refuses a synchronous Dispose while a ProcessAsync is still unwinding
        // ("Cannot dispose while processing, please use DisposeAsync instead") and throws,
        // which killed the pipeline outright. Switching model from the console hits this
        // every time: it cancels the pipeline and restarts it in the same breath, so the
        // previous processor is often still draining when the new model loads.
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
        _factory?.Dispose();
        _processor = null;
        _factory = null;
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(MissingModelMessage(modelPath), modelPath);
        }
        _factory = WhisperFactory.FromPath(modelPath);
        var builder = _factory.CreateBuilder()
            .WithLanguage(_options.Whisper.Language);
        if (_options.Whisper.BiasPrompt)
        {
            // Conditions the decoder on scripture vocabulary so book names survive
            // transcription of accented speech ("Filipians" → "Philippians").
            builder = builder.WithPrompt(BiasVocabulary.WhisperInitialPrompt);
        }
        _processor = builder.Build();
        _loadedModelPath = modelPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
        _factory?.Dispose();
    }
}
