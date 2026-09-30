namespace AdventureSystem.Core.Audio;

public enum SoundWave { Square, Sawtooth, Triangle, Sine, Noise }

/// <summary>
/// Settings for a generated sound effect, in the spirit of sfxr: one oscillator with pitch movement, an envelope,
/// filters and a few effects. <see cref="SfxSynth"/> turns it into samples.
/// </summary>
public sealed class SoundEffect
{
    /// <summary>The preset it started from (for display only).</summary>
    public string Preset { get; set; } = "";

    // ---- tone
    public SoundWave Wave { get; set; } = SoundWave.Square;
    /// <summary>Starting pitch in Hz.</summary>
    public double Frequency { get; set; } = 440;
    /// <summary>Pitch at the end of the sound in Hz (0 = no slide).</summary>
    public double SlideTo { get; set; }
    /// <summary>Square wave duty cycle, 5–95 %.</summary>
    public double Duty { get; set; } = 50;
    /// <summary>Vibrato depth in semitones.</summary>
    public double VibratoDepth { get; set; }
    /// <summary>Vibrato speed in Hz.</summary>
    public double VibratoSpeed { get; set; } = 6;
    /// <summary>Pitch jump in semitones (positive = up), made at <see cref="JumpAtMs"/>.</summary>
    public double JumpSemitones { get; set; }
    /// <summary>When the pitch jump happens, in ms (0 = no jump).</summary>
    public double JumpAtMs { get; set; }
    /// <summary>Restart the pitch movement (slide, jump) every so many ms (0 = off). Good for alarms and trills.</summary>
    public double RepeatMs { get; set; }

    // ---- envelope
    public double AttackMs { get; set; } = 5;
    public double SustainMs { get; set; } = 150;
    public double DecayMs { get; set; } = 200;
    /// <summary>Extra loudness at the start of the sustain that fades away, 0–100 %.</summary>
    public double Punch { get; set; }

    // ---- filters and effects
    /// <summary>Low-pass filter, 0–100 % (100 = off; lower is more muffled).</summary>
    public double LowPass { get; set; } = 100;
    /// <summary>High-pass filter, 0–100 % (0 = off; higher is thinner).</summary>
    public double HighPass { get; set; }
    /// <summary>Retro bit-crush, 0–100 % (fewer bits and a lower sample rate).</summary>
    public double Crush { get; set; }
    /// <summary>Echo delay in ms (0 = off).</summary>
    public double EchoMs { get; set; }
    /// <summary>How much of each echo comes back, 0–90 %.</summary>
    public double EchoFeedback { get; set; } = 40;

    /// <summary>Output volume, 0–100 %.</summary>
    public double Volume { get; set; } = 70;
    /// <summary>Seed for the noise generator, so a sound always renders the same.</summary>
    public int Seed { get; set; } = 1;

    public double LengthMs => AttackMs + SustainMs + DecayMs;

    public SoundEffect Clone() => (SoundEffect)MemberwiseClone();
}
