using LumosPresenter.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Speech;

/// <summary>Holds the active engine; the operator console swaps it without a restart.</summary>
internal sealed class SpeechEngineProvider : ISpeechEngineProvider
{
    private readonly Dictionary<string, ISpeechEngine> _engines;
    private volatile ISpeechEngine _current;

    public SpeechEngineProvider(
        WhisperSpeechEngine whisper,
        SherpaOnnxSpeechEngine sherpaOnnx,
        IOptions<SpeechOptions> options)
    {
        _engines = new Dictionary<string, ISpeechEngine>(StringComparer.OrdinalIgnoreCase)
        {
            [whisper.Name] = whisper,
            [sherpaOnnx.Name] = sherpaOnnx,
        };
        _current = _engines.TryGetValue(options.Value.Engine, out var configured) ? configured : whisper;
    }

    public ISpeechEngine Current => _current;

    public IReadOnlyList<string> AvailableEngines => [.. _engines.Keys];

    public void Select(string name)
    {
        if (!_engines.TryGetValue(name, out var engine))
        {
            throw new ArgumentException(
                $"Unknown speech engine '{name}'. Available: {string.Join(", ", _engines.Keys)}.", nameof(name));
        }
        _current = engine;
    }
}
