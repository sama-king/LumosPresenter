using System.Runtime.CompilerServices;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace LumosPresenter.Speech;

/// <summary>
/// sherpa-onnx streaming Zipformer transducer. Emits word-level partial segments as
/// audio arrives and a final segment at each detected endpoint. Expected end-to-end
/// latency under 2 s.
/// </summary>
public sealed class SherpaOnnxSpeechEngine(IOptions<SpeechOptions> options) : ISpeechEngine
{
    private readonly SherpaOnnxEngineOptions _options = options.Value.SherpaOnnx;
    private OnlineRecognizer? _recognizer;

    public string Name => SpeechEngineNames.SherpaOnnx;

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> audio,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureLoaded();

        var stream = _recognizer!.CreateStream();
        try
        {
            var lastPartial = "";
            await foreach (var frame in audio.WithCancellation(cancellationToken))
            {
                stream.AcceptWaveform(frame.SampleRate, frame.Samples.ToArray());
                while (_recognizer.IsReady(stream))
                {
                    _recognizer.Decode(stream);
                }

                var text = _recognizer.GetResult(stream).Text.Trim();
                if (_recognizer.IsEndpoint(stream))
                {
                    if (text.Length > 0)
                    {
                        yield return new TranscriptSegment(text, IsFinal: true, DateTimeOffset.UtcNow);
                    }
                    _recognizer.Reset(stream);
                    lastPartial = "";
                }
                else if (text.Length > 0 && text != lastPartial)
                {
                    lastPartial = text;
                    yield return new TranscriptSegment(text, IsFinal: false, DateTimeOffset.UtcNow);
                }
            }

            // Audio source completed (file playback / benchmark): flush the pending utterance.
            stream.InputFinished();
            while (_recognizer.IsReady(stream))
            {
                _recognizer.Decode(stream);
            }
            var remaining = _recognizer.GetResult(stream).Text.Trim();
            if (remaining.Length > 0)
            {
                yield return new TranscriptSegment(remaining, IsFinal: true, DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            stream.Dispose();
        }
    }

    private void EnsureLoaded()
    {
        if (_recognizer is not null)
        {
            return;
        }

        foreach (var path in new[] { _options.EncoderPath, _options.DecoderPath, _options.JoinerPath, _options.TokensPath })
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"sherpa-onnx model file not found at '{path}'. Download a streaming Zipformer " +
                    "(e.g. sherpa-onnx-streaming-zipformer-en-2023-06-26 from " +
                    "github.com/k2-fsa/sherpa-onnx releases) and set the Speech:SherpaOnnx paths.", path);
            }
        }

        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16_000;
        config.ModelConfig.Transducer.Encoder = _options.EncoderPath;
        config.ModelConfig.Transducer.Decoder = _options.DecoderPath;
        config.ModelConfig.Transducer.Joiner = _options.JoinerPath;
        config.ModelConfig.Tokens = _options.TokensPath;
        config.ModelConfig.NumThreads = _options.NumThreads;
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 1;
        _recognizer = new OnlineRecognizer(config);
    }

    public ValueTask DisposeAsync()
    {
        _recognizer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
