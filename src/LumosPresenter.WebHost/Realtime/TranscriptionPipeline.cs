using System.Runtime.CompilerServices;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Audio;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;

namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// The live pipeline: audio source → active speech engine → reference parser → SSE events.
/// The source is normally the microphone, but a media file can be run through the exact same
/// path (<see cref="StartFileAsync"/>) so transcript and reference events fire identically.
/// Started and stopped from the operator console; the parser instance is shared with
/// <see cref="SimulateUtterance"/> so typed test input uses the same chapter/verse context.
/// </summary>
public sealed class TranscriptionPipeline(
    IAudioCapture capture,
    ISpeechEngineProvider engines,
    EventBroadcaster broadcaster,
    SessionRecorder recorder,
    IVerseRepository verses,
    TranslationState translation,
    LiveState live,
    ILogger<TranscriptionPipeline> logger)
{
    private readonly ReferenceParser _parser = new();
    private readonly TranslationDetector _translationDetector = new();
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _running;

    /// <summary>
    /// Detections at or above this confidence push to the displays automatically.
    /// Lives here — not in the operator console — so auto-live works no matter which
    /// page (or whether any console tab) is open.
    /// </summary>
    public double AutoLiveConfidence { get; set; } = 0.75;

    /// <summary>
    /// How many utterances a detected book/chapter stays in context before it decays.
    /// Bridges the gap when a speaker names the book, chapter, and verse in separate
    /// breaths with filler between. See <see cref="ReferenceParser.UtteranceWindow"/>.
    /// </summary>
    public int UtteranceWindow
    {
        get => _parser.UtteranceWindow;
        set => _parser.UtteranceWindow = Math.Clamp(value, 1, 200);
    }

    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _running is { IsCompleted: false };
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                return;
            }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _running = Task.Run(() => RunAsync(
                recorder.Tap(capture.CaptureAsync(token), token), engines.Current.Name, token),
                CancellationToken.None);
        }
        PublishStatus();
    }

    /// <summary>
    /// Transcribes a decoded 16 kHz mono WAV through the same engine/parser/SSE path as the
    /// mic. When <paramref name="paced"/> is true, frames are fed at real time so the browser's
    /// audio playback stays roughly in sync; when false, frames feed as fast as the engine can
    /// consume them ("fast" mode — quick file testing with no playback). Stops any active source
    /// first; the parser context is reset so the file starts fresh.
    /// </summary>
    public async Task StartFileAsync(string wavPath, bool paced = true)
    {
        await StopAsync();
        _parser.Reset();
        lock (_gate)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _running = Task.Run(
                () => RunAsync(ReadFileFrames(wavPath, paced, token), "file", token),
                CancellationToken.None);
        }
        PublishStatus();
    }

    /// <summary>
    /// Reads a WAV into 100 ms frames. When <paramref name="paced"/>, each frame is held until
    /// its real-time position so playback tracks; otherwise frames flow as fast as possible.
    /// </summary>
    private static async IAsyncEnumerable<AudioFrame> ReadFileFrames(
        string wavPath, bool paced, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (samples, sampleRate) = WavReader.Read(wavPath);
        var frameSize = sampleRate / 10; // 100 ms
        var start = DateTimeOffset.UtcNow;
        for (var offset = 0; offset < samples.Length; offset += frameSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(frameSize, samples.Length - offset);
            if (paced)
            {
                var elapsedAudio = TimeSpan.FromSeconds((double)offset / sampleRate);
                var wallClock = DateTimeOffset.UtcNow - start;
                if (elapsedAudio > wallClock)
                {
                    await Task.Delay(elapsedAudio - wallClock, cancellationToken);
                }
            }
            yield return new AudioFrame(samples.AsMemory(offset, length), sampleRate, DateTimeOffset.UtcNow);
        }
    }

    public async Task StopAsync()
    {
        Task? running;
        lock (_gate)
        {
            _cts?.Cancel();
            running = _running;
        }
        if (running is not null)
        {
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }
        }
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
            _running = null;
        }
        PublishStatus();
    }

    /// <summary>Switches engine, restarting the pipeline if it was listening.</summary>
    public async Task SwitchEngineAsync(string name)
    {
        var wasListening = IsListening;
        await StopAsync();
        engines.Select(name);
        PublishStatus();
        if (wasListening)
        {
            Start();
        }
    }

    /// <summary>Feeds typed text through the parser as if it were a final transcript.</summary>
    public Task SimulateUtteranceAsync(string text) =>
        HandleSegmentAsync(new TranscriptSegment(text, IsFinal: true, DateTimeOffset.Now), "simulated", CancellationToken.None);

    private async Task RunAsync(
        IAsyncEnumerable<AudioFrame> frames, string source, CancellationToken cancellationToken)
    {
        var engine = engines.Current;
        logger.LogInformation("Pipeline starting: source={Source} engine={Engine}", source, engine.Name);
        try
        {
            await foreach (var segment in engine.TranscribeAsync(frames, cancellationToken))
            {
                await HandleSegmentAsync(segment, engine.Name, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transcription pipeline failed");
            broadcaster.Publish(new PipelineEvent("pipelineerror", new { message = ex.Message }));
        }
        finally
        {
            logger.LogInformation("Pipeline stopped");
            PublishStatus();
        }
    }

    private async Task HandleSegmentAsync(TranscriptSegment segment, string source, CancellationToken cancellationToken)
    {
        broadcaster.Publish(new PipelineEvent("transcript", new
        {
            text = segment.Text,
            isFinal = segment.IsFinal,
            source,
            at = segment.At,
        }));

        if (!segment.IsFinal)
        {
            return;
        }

        // Spoken translation switches ("reading from the Berean Standard") run before
        // reference parsing so a combined utterance ("John 3:16 in the King James")
        // resolves its verse in the new translation. SelectAsync re-pushes whatever is
        // live; a detection failure must never take down the pipeline.
        try
        {
            if (!_translationDetector.IsConfigured)
            {
                _translationDetector.Configure(await verses.GetTranslationsAsync(cancellationToken));
            }
            if (_translationDetector.Detect(segment.Text) is { } detectedCode
                && !detectedCode.Equals(translation.Current, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Detected translation switch to {Translation}", detectedCode);
                await translation.SelectAsync(detectedCode, verses, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Translation detection failed for utterance: {Utterance}", segment.Text);
        }

        foreach (var reference in _parser.Parse(segment.Text))
        {
            logger.LogInformation("Detected {Reference} (confidence {Confidence:0.00})", reference, reference.Confidence);

            // Resolve verse text in the active translation. Chapter-only references skip the
            // fetch (the verse arrives moments later via the continuation logic); failures
            // must never take down the pipeline — the reference still displays without text.
            var verseText = "";
            if (reference.VerseStart is not null)
            {
                try
                {
                    var found = await verses.GetVersesAsync(translation.Current, reference, cancellationToken);
                    verseText = string.Join(" ", found.Select(v => v.Text));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Verse lookup failed for {Reference} ({Translation})",
                        reference, translation.Current);
                }
            }

            broadcaster.Publish(new PipelineEvent("reference", new
            {
                display = reference.ToString(),
                book = reference.Book,
                chapter = reference.Chapter,
                verseStart = reference.VerseStart,
                verseEnd = reference.VerseEnd,
                confidence = reference.Confidence,
                utterance = segment.Text,
                translation = translation.Current,
                text = verseText,
            }));

            // Confidence-gated auto-live: the detection drives the displays directly.
            // LiveState suppresses re-detections of what is already showing.
            if (reference.VerseStart is not null
                && verseText.Length > 0
                && reference.Confidence >= AutoLiveConfidence)
            {
                live.Show(new LiveItem(
                    Guid.NewGuid().ToString("N"),
                    reference.ToString(),
                    verseText,
                    translation.Current,
                    "auto",
                    DateTimeOffset.UtcNow,
                    reference.Book,
                    reference.Chapter,
                    reference.VerseStart,
                    reference.VerseEnd ?? reference.VerseStart));
            }
        }
    }

    private void PublishStatus()
    {
        broadcaster.Publish(new PipelineEvent("status", new
        {
            listening = IsListening,
            engine = engines.Current.Name,
            engineError = engines.Current.ReadinessError,
        }));
    }
}
