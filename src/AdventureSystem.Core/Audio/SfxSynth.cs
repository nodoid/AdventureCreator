namespace AdventureSystem.Core.Audio;

/// <summary>Renders <see cref="SoundEffect"/>s to samples (mono, −1…1).</summary>
public static class SfxSynth
{
    public const int SampleRate = 22050;
    private const double MaxSeconds = 10;

    public static float[] Render(SoundEffect fx, int sampleRate = SampleRate)
    {
        double attack = Math.Max(0, fx.AttackMs) / 1000, sustain = Math.Max(0, fx.SustainMs) / 1000, decay = Math.Max(0, fx.DecayMs) / 1000;
        double length = Math.Clamp(attack + sustain + decay, 0.01, MaxSeconds);
        double feedback = Math.Clamp(fx.EchoFeedback, 0, 90) / 100;
        double echo = Math.Max(0, fx.EchoMs) / 1000;
        // Let echoes ring out until they are about 1 % of the original.
        double tail = echo > 0 && feedback > 0 ? Math.Min(3, echo * Math.Ceiling(Math.Log(0.01) / Math.Log(feedback))) : 0;
        int n = (int)((length + tail) * sampleRate);
        int body = (int)(length * sampleRate);
        var output = new float[n];

        var random = new Random(fx.Seed);
        double noise = random.NextDouble() * 2 - 1;
        double phase = 0;
        double lowPass = 0, highPassState = 0;
        double lowCoefficient = Math.Pow(Math.Clamp(fx.LowPass, 1, 100) / 100, 2);
        double highCoefficient = Math.Pow(Math.Clamp(fx.HighPass, 0, 100) / 100, 2) * 0.25;
        double duty = Math.Clamp(fx.Duty, 5, 95) / 100;
        double f0 = Math.Clamp(fx.Frequency, 20, 12000);
        double f1 = fx.SlideTo > 0 ? Math.Clamp(fx.SlideTo, 20, 12000) : f0;
        double repeat = fx.RepeatMs > 0 ? fx.RepeatMs / 1000 : 0;
        double jumpAt = fx.JumpAtMs > 0 ? fx.JumpAtMs / 1000 : double.MaxValue;
        double jump = Math.Pow(2, fx.JumpSemitones / 12);
        double punch = Math.Clamp(fx.Punch, 0, 100) / 100;

        for (int i = 0; i < body; i++)
        {
            double t = (double)i / sampleRate;
            double tp = repeat > 0 ? t % repeat : t;                       // time for the pitch movement
            double slideLength = repeat > 0 ? repeat : length;
            double f = f0 * Math.Pow(f1 / f0, Math.Min(1, tp / slideLength)); // exponential slide sounds even
            if (tp >= jumpAt) f *= jump;
            if (fx.VibratoDepth > 0) f *= Math.Pow(2, fx.VibratoDepth * Math.Sin(2 * Math.PI * fx.VibratoSpeed * t) / 12);

            double previous = phase;
            phase += f / sampleRate;
            if (phase >= 1) phase -= Math.Floor(phase);
            // Noise changes value twice per cycle, so its "pitch" follows the frequency.
            if (fx.Wave == SoundWave.Noise && (phase < previous || (previous < 0.5 && phase >= 0.5))) noise = random.NextDouble() * 2 - 1;

            double v = fx.Wave switch
            {
                SoundWave.Square => phase < duty ? 1 : -1,
                SoundWave.Sawtooth => 2 * phase - 1,
                SoundWave.Triangle => 1 - 4 * Math.Abs(phase - 0.5),
                SoundWave.Sine => Math.Sin(2 * Math.PI * phase),
                _ => noise,
            };

            // Envelope
            double env;
            if (t < attack) env = t / attack;
            else if (t < attack + sustain) env = 1 + punch * (1 - (t - attack) / Math.Max(sustain, 1e-6)) * 1.5;
            else env = decay > 0 ? Math.Pow(Math.Max(0, 1 - (t - attack - sustain) / decay), 2) : 0;
            v *= env;

            // Filters
            lowPass += (v - lowPass) * lowCoefficient;
            v = lowPass;
            if (highCoefficient > 0)
            {
                highPassState += (v - highPassState) * highCoefficient;
                v -= highPassState;
            }
            output[i] = (float)v;
        }

        if (fx.Crush > 0) Crush(output, body, fx.Crush / 100);
        if (echo > 0 && feedback > 0)
        {
            int delay = Math.Max(1, (int)(echo * sampleRate));
            for (int i = delay; i < n; i++) output[i] += (float)(output[i - delay] * feedback);
        }

        float volume = (float)(Math.Clamp(fx.Volume, 0, 100) / 100 * 0.9);
        for (int i = 0; i < n; i++) output[i] = Math.Clamp(output[i] * volume, -1f, 1f);
        SoundEditing.FadeOut(output, sampleRate, 0.004);   // no click at the end
        return output;
    }

    /// <summary>Renders straight to a 16-bit WAV file.</summary>
    public static byte[] RenderWav(SoundEffect fx) => WavFile.Encode(Render(fx), SampleRate);

    private static void Crush(float[] s, int count, double amount)
    {
        int hold = 1 + (int)(amount * 11);                // sample-and-hold: lower effective sample rate
        double levels = Math.Pow(2, 16 - amount * 12);    // fewer bits
        float held = 0;
        for (int i = 0; i < count; i++)
        {
            if (i % hold == 0) held = (float)(Math.Round(s[i] * levels) / levels);
            s[i] = held;
        }
    }
}
