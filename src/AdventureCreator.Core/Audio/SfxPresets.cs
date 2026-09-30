namespace AdventureCreator.Core.Audio;

/// <summary>Starting points for sound effects. Each preset can be varied randomly within its own character.</summary>
public static class SfxPresets
{
    private delegate SoundEffect Maker(Func<double, double, double> v);

    private static readonly (string Name, string Icon, Maker Make)[] All =
    {
        ("Pickup", "🪙", v => new SoundEffect
        {
            Wave = SoundWave.Square, Frequency = v(880, 250), Duty = v(50, 15), JumpSemitones = v(7, 5), JumpAtMs = v(70, 30),
            AttackMs = 0, SustainMs = v(80, 40), DecayMs = v(220, 100), Punch = v(40, 20), Volume = 60,
        }),
        ("Laser", "🔫", v => new SoundEffect
        {
            Wave = SoundWave.Sawtooth, Frequency = v(1400, 500), SlideTo = v(180, 80), Duty = 50,
            AttackMs = 0, SustainMs = v(120, 60), DecayMs = v(120, 60), HighPass = v(10, 10), Volume = 55,
        }),
        ("Explosion", "💥", v => new SoundEffect
        {
            Wave = SoundWave.Noise, Frequency = v(90, 50), SlideTo = v(40, 20),
            AttackMs = 0, SustainMs = v(250, 120), DecayMs = v(900, 400), Punch = v(60, 30), LowPass = v(70, 20), Volume = 80,
        }),
        ("Power-up", "⭐", v => new SoundEffect
        {
            Wave = SoundWave.Square, Frequency = v(300, 100), SlideTo = v(900, 300), Duty = v(35, 15), RepeatMs = v(90, 30),
            AttackMs = 0, SustainMs = v(350, 150), DecayMs = v(250, 100), Volume = 55,
        }),
        ("Hit", "👊", v => new SoundEffect
        {
            Wave = SoundWave.Noise, Frequency = v(700, 300), SlideTo = v(200, 100),
            AttackMs = 0, SustainMs = v(40, 20), DecayMs = v(160, 80), Punch = v(50, 30), LowPass = v(85, 15), Volume = 70,
        }),
        ("Jump", "🦘", v => new SoundEffect
        {
            Wave = SoundWave.Square, Frequency = v(260, 80), SlideTo = v(620, 200), Duty = v(50, 20),
            AttackMs = 0, SustainMs = v(140, 50), DecayMs = v(120, 60), Volume = 55,
        }),
        ("Blip", "🔹", v => new SoundEffect
        {
            Wave = v(0, 1) > 0 ? SoundWave.Square : SoundWave.Sine, Frequency = v(1000, 400), Duty = 50,
            AttackMs = 0, SustainMs = v(40, 20), DecayMs = v(40, 20), Volume = 50,
        }),
        ("Door creak", "🚪", v => new SoundEffect
        {
            Wave = SoundWave.Sawtooth, Frequency = v(140, 40), SlideTo = v(220, 60), VibratoDepth = v(3, 2), VibratoSpeed = v(11, 4),
            AttackMs = v(80, 40), SustainMs = v(500, 200), DecayMs = v(250, 100), LowPass = v(35, 10), Crush = v(20, 15), Volume = 60,
        }),
        ("Footstep", "👣", v => new SoundEffect
        {
            Wave = SoundWave.Noise, Frequency = v(300, 150),
            AttackMs = 0, SustainMs = v(20, 10), DecayMs = v(90, 40), LowPass = v(30, 12), Volume = 75,
        }),
        ("Alarm", "🚨", v => new SoundEffect
        {
            Wave = SoundWave.Square, Frequency = v(700, 150), SlideTo = v(1100, 200), Duty = 50, RepeatMs = v(250, 80),
            AttackMs = 0, SustainMs = v(1200, 400), DecayMs = 60, LowPass = v(80, 15), Volume = 45,
        }),
        ("Chime", "🔔", v => new SoundEffect
        {
            Wave = SoundWave.Sine, Frequency = v(1320, 400), JumpSemitones = v(12, 5), JumpAtMs = v(120, 40),
            AttackMs = 0, SustainMs = v(30, 20), DecayMs = v(1200, 400), EchoMs = v(180, 60), EchoFeedback = v(35, 15), Volume = 55,
        }),
        ("Thunder", "⛈", v => new SoundEffect
        {
            Wave = SoundWave.Noise, Frequency = v(45, 20), SlideTo = v(25, 10),
            AttackMs = v(60, 40), SustainMs = v(700, 300), DecayMs = v(2200, 700), Punch = v(70, 25), LowPass = v(25, 10), EchoMs = v(220, 80), EchoFeedback = 30, Volume = 90,
        }),
        ("Magic", "✨", v => new SoundEffect
        {
            Wave = SoundWave.Triangle, Frequency = v(600, 200), SlideTo = v(1500, 400), VibratoDepth = v(1.5, 1), VibratoSpeed = v(14, 4), RepeatMs = v(160, 50),
            AttackMs = v(30, 20), SustainMs = v(500, 200), DecayMs = v(400, 150), EchoMs = v(120, 40), EchoFeedback = 40, Volume = 50,
        }),
        ("Splash", "💧", v => new SoundEffect
        {
            Wave = SoundWave.Noise, Frequency = v(1800, 600), SlideTo = v(500, 200),
            AttackMs = v(10, 10), SustainMs = v(120, 60), DecayMs = v(600, 200), LowPass = v(55, 15), HighPass = v(15, 10), Volume = 60,
        }),
    };

    public static IReadOnlyList<(string Name, string Icon)> Names => All.Select(p => (p.Name, p.Icon)).ToList();

    /// <summary>The preset's standard version, or a random variation of it (<paramref name="seed"/> ≠ null).</summary>
    public static SoundEffect Make(string preset, int? seed = null)
    {
        var entry = All.FirstOrDefault(p => string.Equals(p.Name, preset, StringComparison.OrdinalIgnoreCase));
        if (entry.Make == null) entry = All[0];
        var random = seed is int s ? new Random(s) : null;
        double V(double centre, double spread) => random == null ? centre : centre + spread * (random.NextDouble() * 2 - 1);
        var fx = entry.Make(V);
        fx.Preset = entry.Name;
        fx.Seed = seed ?? 1;
        Clamp(fx);
        return fx;
    }

    /// <summary>A small random change to every setting, keeping the sound's character.</summary>
    public static SoundEffect Mutate(SoundEffect source, int seed)
    {
        var r = new Random(seed);
        var fx = source.Clone();
        double M(double value, double scale) => value + scale * (r.NextDouble() * 2 - 1);
        fx.Frequency *= Math.Pow(2, M(0, 0.25));
        if (fx.SlideTo > 0) fx.SlideTo *= Math.Pow(2, M(0, 0.25));
        fx.Duty = M(fx.Duty, 8);
        if (fx.VibratoDepth > 0) fx.VibratoDepth = M(fx.VibratoDepth, 0.5);
        if (fx.JumpAtMs > 0) { fx.JumpAtMs = M(fx.JumpAtMs, 15); fx.JumpSemitones = Math.Round(M(fx.JumpSemitones, 2)); }
        if (fx.RepeatMs > 0) fx.RepeatMs = M(fx.RepeatMs, 20);
        fx.SustainMs *= Math.Pow(2, M(0, 0.2));
        fx.DecayMs *= Math.Pow(2, M(0, 0.2));
        if (fx.Punch > 0) fx.Punch = M(fx.Punch, 10);
        if (fx.LowPass < 100) fx.LowPass = M(fx.LowPass, 8);
        fx.Seed = seed;
        Clamp(fx);
        return fx;
    }

    private static void Clamp(SoundEffect fx)
    {
        fx.Frequency = Math.Round(Math.Clamp(fx.Frequency, 20, 12000));
        fx.SlideTo = fx.SlideTo <= 0 ? 0 : Math.Round(Math.Clamp(fx.SlideTo, 20, 12000));
        fx.Duty = Math.Round(Math.Clamp(fx.Duty, 5, 95));
        fx.VibratoDepth = Math.Round(Math.Clamp(fx.VibratoDepth, 0, 12), 1);
        fx.VibratoSpeed = Math.Round(Math.Clamp(fx.VibratoSpeed, 0.5, 40), 1);
        fx.JumpSemitones = Math.Round(Math.Clamp(fx.JumpSemitones, -24, 24));
        fx.JumpAtMs = Math.Round(Math.Max(0, fx.JumpAtMs));
        fx.RepeatMs = fx.RepeatMs <= 0 ? 0 : Math.Round(Math.Max(30, fx.RepeatMs));
        fx.AttackMs = Math.Round(Math.Clamp(fx.AttackMs, 0, 2000));
        fx.SustainMs = Math.Round(Math.Clamp(fx.SustainMs, 0, 4000));
        fx.DecayMs = Math.Round(Math.Clamp(fx.DecayMs, 0, 4000));
        fx.Punch = Math.Round(Math.Clamp(fx.Punch, 0, 100));
        fx.LowPass = Math.Round(Math.Clamp(fx.LowPass, 1, 100));
        fx.HighPass = Math.Round(Math.Clamp(fx.HighPass, 0, 100));
        fx.Crush = Math.Round(Math.Clamp(fx.Crush, 0, 100));
        fx.EchoMs = Math.Round(Math.Clamp(fx.EchoMs, 0, 1000));
        fx.EchoFeedback = Math.Round(Math.Clamp(fx.EchoFeedback, 0, 90));
        fx.Volume = Math.Round(Math.Clamp(fx.Volume, 0, 100));
    }
}
