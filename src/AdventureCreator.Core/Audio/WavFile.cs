namespace AdventureCreator.Core.Audio;

/// <summary>Reads and writes WAV files (PCM 8/16/24/32-bit and 32-bit float; stereo is mixed down to mono).</summary>
public static class WavFile
{
    public static bool IsWav(byte[] data) =>
        data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';

    /// <summary>Encodes mono samples (−1…1) as a 16-bit PCM WAV file.</summary>
    public static byte[] Encode(float[] samples, int sampleRate)
    {
        using var ms = new MemoryStream(44 + samples.Length * 2);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)1);            // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * 32767));
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Decodes a WAV file to mono samples. Throws <see cref="InvalidDataException"/> for other formats.</summary>
    public static (float[] Samples, int SampleRate) Decode(byte[] data)
    {
        if (!IsWav(data)) throw new InvalidDataException("Not a WAV file.");
        int format = 0, channels = 0, rate = 0, bits = 0;
        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            int size = BitConverter.ToInt32(data, pos + 4);
            int body = pos + 8;
            if (size < 0 || body > data.Length) break;
            if (id == "fmt " && size >= 16)
            {
                format = BitConverter.ToUInt16(data, body);
                channels = BitConverter.ToUInt16(data, body + 2);
                rate = BitConverter.ToInt32(data, body + 4);
                bits = BitConverter.ToUInt16(data, body + 14);
                // WAVE_FORMAT_EXTENSIBLE: the real format is in the sub-format GUID.
                if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(data, body + 24);
            }
            else if (id == "data")
            {
                if (channels <= 0 || rate <= 0) throw new InvalidDataException("The WAV file has no format information.");
                int length = Math.Min(size, data.Length - body);
                return (ReadSamples(data, body, length, format, channels, bits), rate);
            }
            pos = body + size + (size & 1);
        }
        throw new InvalidDataException("The WAV file has no audio data.");
    }

    private static float[] ReadSamples(byte[] data, int start, int length, int format, int channels, int bits)
    {
        int bytes = bits / 8;
        if (bytes == 0 || !(format == 1 && bytes is 1 or 2 or 3 or 4 || format == 3 && bytes == 4))
            throw new InvalidDataException($"This WAV format can't be edited (format {format}, {bits}-bit). Use 8, 16, 24 or 32-bit PCM, or 32-bit float.");
        int frames = length / (bytes * channels);
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int p = start + (f * channels + c) * bytes;
                sum += format == 3 ? BitConverter.ToSingle(data, p) : bytes switch
                {
                    1 => (data[p] - 128) / 128.0,
                    2 => BitConverter.ToInt16(data, p) / 32768.0,
                    3 => ((data[p] | data[p + 1] << 8 | (sbyte)data[p + 2] << 16)) / 8388608.0,
                    _ => BitConverter.ToInt32(data, p) / 2147483648.0,
                };
            }
            samples[f] = (float)(sum / channels);
        }
        return samples;
    }
}
