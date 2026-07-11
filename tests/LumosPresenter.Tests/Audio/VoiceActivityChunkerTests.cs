using LumosPresenter.Core.Audio;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Tests.Audio;

public class VoiceActivityChunkerTests
{
    private const int SampleRate = 16_000;
    private const int FrameMs = 100;

    private static AudioFrame Frame(float amplitude)
    {
        var samples = new float[SampleRate * FrameMs / 1000];
        Array.Fill(samples, amplitude);
        return new AudioFrame(samples, SampleRate, DateTimeOffset.UtcNow);
    }

    private static async IAsyncEnumerable<AudioFrame> Stream(params (float Amplitude, int Frames)[] sections)
    {
        foreach (var (amplitude, frames) in sections)
        {
            for (var i = 0; i < frames; i++)
            {
                yield return Frame(amplitude);
            }
        }
        await Task.CompletedTask;
    }

    private static async Task<List<AudioFrame>> Collect(IAsyncEnumerable<AudioFrame> chunks)
    {
        var result = new List<AudioFrame>();
        await foreach (var chunk in chunks)
        {
            result.Add(chunk);
        }
        return result;
    }

    [Fact]
    public async Task SpeechBetweenSilence_YieldsSingleChunk()
    {
        var audio = Stream((0f, 5), (0.5f, 8), (0f, 8));
        var chunks = await Collect(VoiceActivityChunker.ChunkAsync(audio, new VadOptions()));

        var chunk = Assert.Single(chunks);
        // 800 ms speech + 200 ms pre-roll + trailing silence up to the 600 ms cutoff
        Assert.InRange(chunk.Samples.Length, SampleRate, SampleRate * 2);
    }

    [Fact]
    public async Task SilenceOnly_YieldsNothing()
    {
        var chunks = await Collect(VoiceActivityChunker.ChunkAsync(Stream((0f, 30)), new VadOptions()));
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ShortBlip_BelowMinSpeech_IsDiscarded()
    {
        var audio = Stream((0f, 5), (0.5f, 2), (0f, 10)); // 200 ms < 300 ms MinSpeech
        var chunks = await Collect(VoiceActivityChunker.ChunkAsync(audio, new VadOptions()));
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task TwoUtterances_YieldTwoChunks()
    {
        var audio = Stream((0f, 3), (0.5f, 6), (0f, 8), (0.5f, 6), (0f, 8));
        var chunks = await Collect(VoiceActivityChunker.ChunkAsync(audio, new VadOptions()));
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task SpeechAtEndOfStream_IsFlushed()
    {
        var audio = Stream((0f, 3), (0.5f, 6)); // stream ends mid-utterance
        var chunks = await Collect(VoiceActivityChunker.ChunkAsync(audio, new VadOptions()));
        Assert.Single(chunks);
    }

    [Fact]
    public async Task LongSpeech_IsSplitAtMaxUtterance()
    {
        var options = new VadOptions { MaxUtterance = TimeSpan.FromSeconds(2) };
        var audio = Stream((0.5f, 50)); // 5 s of continuous speech
        var chunks = await Collect(VoiceActivityChunker.ChunkAsync(audio, options));
        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Samples.Length <= SampleRate * 2 + SampleRate / 10));
    }
}
