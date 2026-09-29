namespace AdventureCreator.Core.Samples;

/// <summary>Tiny procedural audio generator used by the example game (so it ships with sound but no binary files).</summary>
public static class SoundSynth
{
    public const int SampleRate = 22050;

    /// <summary>Rolling surf: low-passed noise with a slow swell.</summary>
    public static byte[] Waves(double seconds = 6)
    {
        var rnd = new Random(7);
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        double lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double swell = 0.35 + 0.65 * Math.Pow(Math.Sin(Math.PI * t / 3.0), 2);
            double noise = rnd.NextDouble() * 2 - 1;
            lp += (noise - lp) * 0.06;
            lp2 += (lp - lp2) * 0.2;
            s[i] = (short)(Math.Clamp(lp2 * 3.2 * swell, -1, 1) * 12000);
        }
        return Fade(s, 0.4);
    }

    /// <summary>Two-tone foghorn.</summary>
    public static byte[] Foghorn(double seconds = 2.5)
    {
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double f = t < seconds / 2 ? 110 : 98;
            double v = 0.6 * Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(2 * Math.PI * f * 2 * t) + 0.1 * Math.Sin(2 * Math.PI * f * 3 * t);
            s[i] = (short)(v * 14000);
        }
        return Fade(s, 0.25);
    }

    /// <summary>Rising arpeggio for success.</summary>
    public static byte[] Fanfare()
    {
        double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
        const double noteLen = 0.18;
        int per = (int)(noteLen * SampleRate);
        var s = new short[per * notes.Length + SampleRate / 2];
        for (int k = 0; k < notes.Length; k++)
            for (int i = 0; i < per * (k == notes.Length - 1 ? 3 : 1) && k * per + i < s.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Exp(-t * 4);
                s[k * per + i] += (short)(Math.Sin(2 * Math.PI * notes[k] * t) * env * 9000);
            }
        return ToWav(s);
    }

    /// <summary>Creaking wood: a slowly bending, rough tone.</summary>
    public static byte[] Creak()
    {
        int n = (int)(0.8 * SampleRate);
        var s = new short[n];
        var rnd = new Random(3);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double f = 180 + 120 * Math.Sin(t * 5);
            phase += 2 * Math.PI * f / SampleRate;
            double v = Math.Sign(Math.Sin(phase)) * 0.5 + (rnd.NextDouble() - 0.5) * 0.3;
            s[i] = (short)(v * 6000 * Math.Sin(Math.PI * t / 0.8));
        }
        return ToWav(s);
    }

    private static byte[] Fade(short[] s, double seconds)
    {
        int f = (int)(seconds * SampleRate);
        for (int i = 0; i < f && i < s.Length; i++)
        {
            double g = (double)i / f;
            s[i] = (short)(s[i] * g);
            s[^(i + 1)] = (short)(s[^(i + 1)] * g);
        }
        return ToWav(s);
    }

    public static byte[] ToWav(short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SampleRate);
        w.Write(SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var v in samples) w.Write(v);
        w.Flush();
        return ms.ToArray();
    }
}
