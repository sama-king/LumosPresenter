using LumosPresenter.Core.Domain;
using LumosPresenter.Speech;

namespace LumosPresenter.Tests.Speech;

/// <summary>
/// Golden-audio tests: run a real recording ("after early nightfall the yellow lamps…")
/// through each real engine and check the words survive. Seed of the Week-1 bake-off
/// harness. Tests no-op when models are not downloaded (e.g. CI).
/// </summary>
public class SpeechEngineIntegrationTests
{
    private static readonly string ModelsDir = Path.Combine(
        FindRepoRoot(), "src", "LumosPresenter.WebHost", "models");

    private static readonly string GoldenWav = Path.Combine(ModelsDir, "sherpa-onnx", "test_wavs", "0.wav");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent!;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static SpeechOptions CreateOptions() => new()
    {
        Whisper = new WhisperEngineOptions
        {
            ModelPath = Path.Combine(ModelsDir, "whisper", "ggml-base.en.bin"),
        },
        SherpaOnnx = new SherpaOnnxEngineOptions
        {
            EncoderPath = Path.Combine(ModelsDir, "sherpa-onnx", "encoder.onnx"),
            DecoderPath = Path.Combine(ModelsDir, "sherpa-onnx", "decoder.onnx"),
            JoinerPath = Path.Combine(ModelsDir, "sherpa-onnx", "joiner.onnx"),
            TokensPath = Path.Combine(ModelsDir, "sherpa-onnx", "tokens.txt"),
        },
    };

    [Fact]
    public async Task SherpaOnnx_TranscribesGoldenAudio()
    {
        var options = CreateOptions();
        if (!File.Exists(GoldenWav) || !File.Exists(options.SherpaOnnx.EncoderPath))
        {
            return; // models not downloaded locally
        }

        await using var engine = new SherpaOnnxSpeechEngine(
            Microsoft.Extensions.Options.Options.Create(options));

        var lastText = "";
        await foreach (var segment in engine.TranscribeAsync(WavFrames(GoldenWav)))
        {
            lastText = segment.Text;
        }

        Assert.Contains("yellow lamps", lastText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Whisper_TranscribesGoldenAudio()
    {
        var options = CreateOptions();
        if (!File.Exists(GoldenWav) || !File.Exists(options.Whisper.ModelPath))
        {
            return; // models not downloaded locally
        }

        await using var engine = new WhisperSpeechEngine(
            Microsoft.Extensions.Options.Options.Create(options));

        var text = "";
        await foreach (var segment in engine.TranscribeAsync(WavFrames(GoldenWav)))
        {
            text += " " + segment.Text;
        }

        Assert.Contains("yellow lamps", text, StringComparison.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<AudioFrame> WavFrames(string path)
    {
        var (samples, sampleRate) = ReadPcm16Wav(path);
        const int frameSize = 1_600;
        for (var i = 0; i < samples.Length; i += frameSize)
        {
            var length = Math.Min(frameSize, samples.Length - i);
            yield return new AudioFrame(samples.AsMemory(i, length), sampleRate, DateTimeOffset.UtcNow);
        }
        await Task.CompletedTask;
    }

    private static (float[] Samples, int SampleRate) ReadPcm16Wav(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12); // "RIFF" + size + "WAVE"
        var sampleRate = 16_000;
        while (true)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkId == "fmt ")
            {
                var format = reader.ReadBytes(chunkSize);
                sampleRate = BitConverter.ToInt32(format, 4);
            }
            else if (chunkId == "data")
            {
                var bytes = reader.ReadBytes(chunkSize);
                var samples = new float[bytes.Length / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                }
                return (samples, sampleRate);
            }
            else
            {
                reader.ReadBytes(chunkSize + (chunkSize % 2)); // chunks are word-aligned
            }
        }
    }
}
