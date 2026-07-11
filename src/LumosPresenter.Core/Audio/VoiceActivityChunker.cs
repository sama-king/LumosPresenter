using System.Runtime.CompilerServices;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Audio;

/// <summary>Tuning for energy-based voice-activity chunking.</summary>
public sealed record VadOptions
{
    /// <summary>RMS energy above which a frame counts as speech.</summary>
    public double EnergyThreshold { get; init; } = 0.01;

    /// <summary>Minimum accumulated speech for a chunk to be worth transcribing.</summary>
    public TimeSpan MinSpeech { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Trailing silence that ends an utterance.</summary>
    public TimeSpan EndSilence { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Hard cap per chunk so a long sentence still transcribes promptly.</summary>
    public TimeSpan MaxUtterance { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Audio kept from just before speech onset so word starts are not clipped.</summary>
    public TimeSpan PreRoll { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// Groups a continuous microphone stream into utterance-sized chunks for non-streaming
/// engines (Whisper). Pure logic: energy-based, no native dependencies, unit-testable.
/// Streaming engines (sherpa-onnx) consume raw frames and do not need this.
/// </summary>
public static class VoiceActivityChunker
{
    public static async IAsyncEnumerable<AudioFrame> ChunkAsync(
        IAsyncEnumerable<AudioFrame> frames,
        VadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new List<float>();
        var preRoll = new Queue<AudioFrame>();
        var preRollSamples = 0;
        var inSpeech = false;
        var speechSamples = 0;
        var silenceSamples = 0;
        var sampleRate = 0;
        DateTimeOffset chunkStart = default;

        await foreach (var frame in frames.WithCancellation(cancellationToken))
        {
            sampleRate = frame.SampleRate;
            var isSpeech = Rms(frame.Samples.Span) >= options.EnergyThreshold;

            if (!inSpeech)
            {
                if (!isSpeech)
                {
                    preRoll.Enqueue(frame);
                    preRollSamples += frame.Samples.Length;
                    var maxPreRoll = (int)(options.PreRoll.TotalSeconds * sampleRate);
                    while (preRollSamples > maxPreRoll && preRoll.Count > 1)
                    {
                        preRollSamples -= preRoll.Dequeue().Samples.Length;
                    }
                    continue;
                }

                inSpeech = true;
                chunkStart = preRoll.Count > 0 ? preRoll.Peek().CapturedAt : frame.CapturedAt;
                while (preRoll.Count > 0)
                {
                    Append(buffer, preRoll.Dequeue());
                }
                preRollSamples = 0;
            }

            Append(buffer, frame);
            if (isSpeech)
            {
                speechSamples += frame.Samples.Length;
                silenceSamples = 0;
            }
            else
            {
                silenceSamples += frame.Samples.Length;
            }

            var utteranceEnded = silenceSamples >= (int)(options.EndSilence.TotalSeconds * sampleRate);
            var utteranceFull = buffer.Count >= (int)(options.MaxUtterance.TotalSeconds * sampleRate);
            if (!utteranceEnded && !utteranceFull)
            {
                continue;
            }

            if (speechSamples >= (int)(options.MinSpeech.TotalSeconds * sampleRate))
            {
                yield return new AudioFrame(buffer.ToArray(), sampleRate, chunkStart);
            }
            buffer.Clear();
            inSpeech = false;
            speechSamples = 0;
            silenceSamples = 0;
        }

        // End of stream: flush whatever speech is pending.
        if (sampleRate > 0 && speechSamples >= (int)(options.MinSpeech.TotalSeconds * sampleRate))
        {
            yield return new AudioFrame(buffer.ToArray(), sampleRate, chunkStart);
        }
    }

    private static void Append(List<float> buffer, AudioFrame frame)
    {
        buffer.AddRange(frame.Samples.Span);
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }
        double sum = 0;
        foreach (var sample in samples)
        {
            sum += (double)sample * sample;
        }
        return Math.Sqrt(sum / samples.Length);
    }
}
