namespace Dispatch.Core.Audio;

/// <summary>Mono 16-bit PCM audio: the samples and their rate.</summary>
/// <param name="Samples">Signed 16-bit samples, one per frame.</param>
/// <param name="SampleRate">Frames per second.</param>
public sealed record PcmAudio(short[] Samples, int SampleRate);

/// <summary>
/// Reads and writes 16-bit PCM WAV, the one audio container the app needs.
/// </summary>
/// <remarks>
/// A deliberately small reader: it understands a canonical PCM WAV — the format
/// SAPI renders and <c>winmm</c> plays — and no more. Multi-channel input is
/// down-mixed to mono, because everything here is a voice line and the radio
/// effect works one channel at a time.
/// </remarks>
public static class WavAudio
{
    /// <summary>Parses a PCM WAV file into mono samples, or throws on a shape it does not handle.</summary>
    public static PcmAudio Read(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);

        if (wav.Length < 44 || wav[0] != (byte)'R' || wav[1] != (byte)'I' || wav[2] != (byte)'F' || wav[3] != (byte)'F')
        {
            throw new FormatException("Not a RIFF/WAVE file.");
        }

        int channels = 1, sampleRate = 22050, bitsPerSample = 16;
        var dataOffset = -1;
        var dataLength = 0;

        // Walk the chunks: fmt carries the format, data carries the samples.
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            var body = pos + 8;

            if (id == "fmt " && body + 16 <= wav.Length)
            {
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bitsPerSample = BitConverter.ToInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = Math.Min(size, wav.Length - body);
            }

            pos = body + size + (size & 1); // chunks are word-aligned
        }

        if (dataOffset < 0 || bitsPerSample != 16)
        {
            throw new FormatException("Unsupported WAV: expected 16-bit PCM with a data chunk.");
        }

        var frames = dataLength / 2 / Math.Max(1, channels);
        var samples = new short[frames];

        for (var i = 0; i < frames; i++)
        {
            if (channels == 1)
            {
                samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2);
            }
            else
            {
                // Average the channels down to mono.
                var sum = 0;
                for (var c = 0; c < channels; c++)
                {
                    sum += BitConverter.ToInt16(wav, dataOffset + (i * channels + c) * 2);
                }

                samples[i] = (short)(sum / channels);
            }
        }

        return new PcmAudio(samples, sampleRate);
    }

    /// <summary>Builds a canonical mono 16-bit PCM WAV from samples.</summary>
    public static byte[] Write(PcmAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var dataBytes = audio.Samples.Length * 2;
        var buffer = new byte[44 + dataBytes];

        void Ascii(int at, string s) => System.Text.Encoding.ASCII.GetBytes(s).CopyTo(buffer, at);
        void I32(int at, int v) => BitConverter.GetBytes(v).CopyTo(buffer, at);
        void I16(int at, short v) => BitConverter.GetBytes(v).CopyTo(buffer, at);

        Ascii(0, "RIFF");
        I32(4, 36 + dataBytes);
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        I32(16, 16);
        I16(20, 1);                                  // PCM
        I16(22, 1);                                  // mono
        I32(24, audio.SampleRate);
        I32(28, audio.SampleRate * 2);               // byte rate
        I16(32, 2);                                   // block align
        I16(34, 16);                                  // bits per sample
        Ascii(36, "data");
        I32(40, dataBytes);

        Buffer.BlockCopy(audio.Samples, 0, buffer, 44, dataBytes);
        return buffer;
    }
}
