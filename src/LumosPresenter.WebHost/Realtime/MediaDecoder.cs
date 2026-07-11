using System.Diagnostics;

namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// Decodes an arbitrary media file (mp3, m4a, mp4, aac, wav, …) to a 16 kHz mono 16-bit
/// PCM WAV — the format both speech engines expect — using macOS's built-in `afconvert`
/// (Core Audio). No third-party dependency and fully offline.
///
/// Windows has no afconvert; a cross-platform decoder (e.g. bundled ffmpeg) is a follow-up.
/// </summary>
public sealed class MediaDecoder(ILogger<MediaDecoder> logger)
{
    public const int TargetSampleRate = 16_000;

    /// <summary>Converts <paramref name="inputPath"/> to a mono 16 kHz WAV at <paramref name="outputWavPath"/>.</summary>
    public async Task DecodeToWavAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("afconvert")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // -f WAVE -d LEI16 (16-bit little-endian PCM), mono, 16 kHz.
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("WAVE");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add("LEI16@16000");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add(outputWavPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start afconvert. Is this macOS?");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            logger.LogError("afconvert failed ({Code}): {Error}", process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"Could not decode media file (afconvert exit {process.ExitCode}). " +
                "Supported: wav, mp3, m4a, mp4, aac, aiff, caf.");
        }
    }
}
