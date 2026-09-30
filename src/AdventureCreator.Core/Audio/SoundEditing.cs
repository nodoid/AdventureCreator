namespace AdventureCreator.Core.Audio;

/// <summary>Simple edits on mono samples (−1…1). Operations return a new array or change the one given.</summary>
public static class SoundEditing
{
    /// <summary>Keeps only samples [from, to).</summary>
    public static float[] Trim(float[] s, int from, int to)
    {
        (from, to) = Range(s, from, to);
        return s[from..to];
    }

    /// <summary>Removes samples [from, to).</summary>
    public static float[] Delete(float[] s, int from, int to)
    {
        (from, to) = Range(s, from, to);
        return s[..from].Concat(s[to..]).ToArray();
    }

    /// <summary>Fades in over samples [from, to).</summary>
    public static void FadeIn(float[] s, int from, int to)
    {
        (from, to) = Range(s, from, to);
        int n = Math.Max(1, to - from);
        for (int i = from; i < to; i++) s[i] *= (float)(i - from) / n;
    }

    /// <summary>Fades out over samples [from, to).</summary>
    public static void FadeOut(float[] s, int from, int to)
    {
        (from, to) = Range(s, from, to);
        int n = Math.Max(1, to - from);
        for (int i = from; i < to; i++) s[i] *= (float)(to - i) / n;
    }

    /// <summary>Fades out the last <paramref name="seconds"/>.</summary>
    public static void FadeOut(float[] s, int sampleRate, double seconds) => FadeOut(s, s.Length - (int)(seconds * sampleRate), s.Length);

    /// <summary>Scales samples [from, to) so the loudest reaches <paramref name="peak"/>.</summary>
    public static void Normalize(float[] s, int from, int to, float peak = 0.95f)
    {
        (from, to) = Range(s, from, to);
        float max = 0;
        for (int i = from; i < to; i++) max = Math.Max(max, Math.Abs(s[i]));
        if (max < 1e-6f) return;
        Gain(s, from, to, peak / max);
    }

    /// <summary>Multiplies samples [from, to) by <paramref name="factor"/>, clipping at ±1.</summary>
    public static void Gain(float[] s, int from, int to, float factor)
    {
        (from, to) = Range(s, from, to);
        for (int i = from; i < to; i++) s[i] = Math.Clamp(s[i] * factor, -1f, 1f);
    }

    /// <summary>Reverses samples [from, to).</summary>
    public static void Reverse(float[] s, int from, int to)
    {
        (from, to) = Range(s, from, to);
        Array.Reverse(s, from, to - from);
    }

    /// <summary>Adds an echo to the whole sound, lengthening it so the echoes can ring out.</summary>
    public static float[] Echo(float[] s, int sampleRate, double delaySeconds, double feedback)
    {
        feedback = Math.Clamp(feedback, 0, 0.9);
        int delay = Math.Max(1, (int)(delaySeconds * sampleRate));
        int extra = feedback > 0 ? (int)Math.Min(3 * sampleRate, delay * Math.Ceiling(Math.Log(0.01) / Math.Log(feedback))) : 0;
        var o = new float[s.Length + extra];
        Array.Copy(s, o, s.Length);
        for (int i = delay; i < o.Length; i++) o[i] = Math.Clamp(o[i] + (float)(o[i - delay] * feedback), -1f, 1f);
        return o;
    }

    /// <summary>Clamps a range to the sound; an empty range means the whole sound.</summary>
    public static (int From, int To) Range(float[] s, int from, int to)
    {
        from = Math.Clamp(from, 0, s.Length);
        to = Math.Clamp(to, 0, s.Length);
        if (to < from) (from, to) = (to, from);
        return to - from <= 0 ? (0, s.Length) : (from, to);
    }
}
