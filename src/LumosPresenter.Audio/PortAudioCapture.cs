using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using PortAudioSharp;

namespace LumosPresenter.Audio;

/// <summary>
/// Microphone capture via PortAudio (cross-platform; NAudio is deliberately not used).
/// Produces 16 kHz mono float frames — the input format both speech engines expect.
///
/// macOS: the OS prompts for microphone access on first use. A packaged .app must
/// declare NSMicrophoneUsageDescription in Info.plist or capture fails silently (TCC);
/// this is verified in the Week 1 packaged-build spike.
/// </summary>
public sealed class PortAudioCapture : IAudioCapture
{
    public const int SampleRate = 16_000;
    private const uint FramesPerBuffer = 1_600; // 100 ms

    private volatile float _peak;
    private volatile float _rms;

    public int? DeviceId { get; set; }

    public AudioLevel CurrentLevel => new(_peak, _rms);

    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Bounded with DropOldest: if the consumer stalls, we lose old audio instead of growing memory.
        var channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(capacity: 64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        PortAudio.Initialize();
        PortAudioSharp.Stream? stream = null;
        try
        {
            var device = DeviceId ?? PortAudio.DefaultInputDevice;
            if (device == PortAudio.NoDevice)
            {
                throw new InvalidOperationException(
                    "No microphone found. Connect an input device and check OS microphone permissions.");
            }

            var parameters = new StreamParameters
            {
                device = device,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = PortAudio.GetDeviceInfo(device).defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            stream = new PortAudioSharp.Stream(
                inParams: parameters,
                outParams: null,
                sampleRate: SampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamFlags: StreamFlags.ClipOff,
                callback: (nint input, nint _, uint frameCount,
                    ref StreamCallbackTimeInfo _, StreamCallbackFlags _, nint _) =>
                {
                    var samples = new float[frameCount];
                    Marshal.Copy(input, samples, 0, (int)frameCount);
                    UpdateLevel(samples);
                    channel.Writer.TryWrite(new AudioFrame(samples, SampleRate, DateTimeOffset.UtcNow));
                    return StreamCallbackResult.Continue;
                },
                userData: IntPtr.Zero);

            stream.Start();

            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }
        finally
        {
            _peak = 0;
            _rms = 0;
            if (stream is not null)
            {
                stream.Stop();
                stream.Dispose();
            }
            PortAudio.Terminate();
        }
    }

    private void UpdateLevel(float[] samples)
    {
        var peak = 0f;
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }
            sumSquares += (double)sample * sample;
        }
        // Plain writes: single writer (the callback), readers tolerate a slightly stale value.
        _peak = peak;
        _rms = samples.Length > 0 ? (float)Math.Sqrt(sumSquares / samples.Length) : 0f;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
