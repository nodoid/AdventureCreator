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

    /// <summary>Howling wind over a wasteland: band-limited noise with slow gusts.</summary>
    public static byte[] Wind(double seconds = 6)
    {
        var rnd = new Random(11);
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        double lp = 0, bp = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double gust = 0.4 + 0.6 * Math.Pow(Math.Sin(Math.PI * t / 2.0 + Math.Sin(t * 0.7)), 2);
            double noise = rnd.NextDouble() * 2 - 1;
            lp += (noise - lp) * 0.03;
            bp += (lp - bp) * (0.08 + 0.05 * Math.Sin(t * 1.3));
            s[i] = (short)(Math.Clamp((lp - bp) * 9 * gust, -1, 1) * 11000);
        }
        return Fade(s, 0.5);
    }

    /// <summary>Two-tone alarm siren.</summary>
    public static byte[] Siren(double seconds = 2.4)
    {
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double f = ((int)(t * 2.5) % 2 == 0) ? 880 : 660;
            phase += 2 * Math.PI * f / SampleRate;
            s[i] = (short)(Math.Sign(Math.Sin(phase)) * 5000 + Math.Sin(phase) * 4000);
        }
        return Fade(s, 0.05);
    }

    /// <summary>Low electrical hum of a bunker's machinery.</summary>
    public static byte[] Hum(double seconds = 4)
    {
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        var rnd = new Random(5);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double v = 0.5 * Math.Sin(2 * Math.PI * 50 * t) + 0.3 * Math.Sin(2 * Math.PI * 100 * t) + 0.15 * Math.Sin(2 * Math.PI * 150 * t)
                       + 0.05 * (rnd.NextDouble() - 0.5);
            s[i] = (short)(v * (0.8 + 0.2 * Math.Sin(t * 3)) * 7000);
        }
        return Fade(s, 0.3);
    }

    /// <summary>A deep explosion: noise burst with a fast attack and long rumbling decay.</summary>
    public static byte[] Explosion(double seconds = 3)
    {
        var rnd = new Random(9);
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        double lp = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 1.6) * Math.Min(1, t * 200);
            lp += (rnd.NextDouble() * 2 - 1 - lp) * (0.02 + 0.3 * Math.Exp(-t * 8));
            s[i] = (short)(Math.Clamp(lp * 4 * env, -1, 1) * 30000);
        }
        return ToWav(s);
    }

    /// <summary>Wheezing, groaning dematerialisation effect: filtered noise swept by a slow oscillation.</summary>
    public static byte[] Dematerialise(double seconds = 5)
    {
        var rnd = new Random(13);
        int n = (int)(seconds * SampleRate);
        var s = new short[n];
        double lp = 0, phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double sweep = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.55 * t - Math.PI / 2);
            double f = 70 + 240 * sweep;
            phase += 2 * Math.PI * f / SampleRate;
            lp += (rnd.NextDouble() * 2 - 1 - lp) * (0.01 + 0.08 * sweep);
            double env = Math.Min(1, t) * Math.Min(1, (seconds - t) * 1.5);
            s[i] = (short)(Math.Clamp((Math.Sin(phase) * 0.5 + lp * 2.5) * sweep * env, -1, 1) * 15000);
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
