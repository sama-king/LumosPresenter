namespace LumosPresenter.Core.Audio;

/// <summary>Reads a 16-bit PCM mono/stereo WAV file into normalized float samples.</summary>
public static class WavReader
{
    /// <summary>Reads the file, downmixing to mono. Returns samples in [-1, 1] and the sample rate.</summary>
    public static (float[] Samples, int SampleRate) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            throw new InvalidDataException("Not a RIFF/WAV file.");
        }
        reader.ReadInt32();                       // file size
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Not a WAVE file.");
        }

        int sampleRate = 16_000;
        short channels = 1;
        short bitsPerSample = 16;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkId == "fmt ")
            {
                reader.ReadInt16();               // audio format
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();               // byte rate
                reader.ReadInt16();               // block align
                bitsPerSample = reader.ReadInt16();
                var consumed = 16;
                if (chunkSize > consumed)
                {
                    reader.ReadBytes(chunkSize - consumed);
                }
            }
            else if (chunkId == "data")
            {
                if (bitsPerSample != 16)
                {
                    throw new InvalidDataException($"Only 16-bit PCM is supported (got {bitsPerSample}-bit).");
                }
                var bytes = reader.ReadBytes(chunkSize);
                var totalSamples = bytes.Length / 2;
                var frameCount = channels > 0 ? totalSamples / channels : totalSamples;
                var mono = new float[frameCount];
                for (var frame = 0; frame < frameCount; frame++)
                {
                    float sum = 0;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        sum += BitConverter.ToInt16(bytes, (frame * channels + ch) * 2) / 32768f;
                    }
                    mono[frame] = sum / channels;
                }
                return (mono, sampleRate);
            }
            else
            {
                reader.ReadBytes(chunkSize + (chunkSize % 2)); // chunks are word-aligned
            }
        }
        throw new InvalidDataException("WAV file has no data chunk.");
    }
}
