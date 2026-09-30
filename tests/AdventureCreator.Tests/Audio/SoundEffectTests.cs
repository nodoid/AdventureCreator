using AdventureCreator.Core.Audio;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;

namespace AdventureCreator.Tests.Audio;

public class SoundEffectTests
{
    private static float Peak(float[] s) => s.Length == 0 ? 0 : s.Max(Math.Abs);

    [Fact]
    public void EveryPresetRendersAudibleClippedSound()
    {
        foreach (var (name, _) in SfxPresets.Names)
            foreach (var seed in new int?[] { null, 1, 2, 3 })
            {
                var fx = SfxPresets.Make(name, seed);
                var s = SfxSynth.Render(fx);
                Assert.True(s.Length >= fx.LengthMs / 1000 * SfxSynth.SampleRate * 0.99, $"{name} too short");
                Assert.True(Peak(s) > 0.05f, $"{name} (seed {seed}) is silent");
                Assert.True(Peak(s) <= 1f, $"{name} clips");
                Assert.True(Math.Abs(s[^1]) < 0.01f, $"{name} ends with a click");
            }
    }

    [Fact]
    public void RenderingIsDeterministicAndVariationsDiffer()
    {
        var a = SfxSynth.Render(SfxPresets.Make("Explosion", 5));
        var b = SfxSynth.Render(SfxPresets.Make("Explosion", 5));
        var c = SfxSynth.Render(SfxPresets.Make("Explosion", 6));
        Assert.Equal(a, b);
        Assert.False(a.SequenceEqual(c));
        var mutated = SfxPresets.Mutate(SfxPresets.Make("Laser"), 9);
        Assert.Equal("Laser", mutated.Preset);
        Assert.NotEqual(SfxPresets.Make("Laser").Frequency, mutated.Frequency);
    }

    [Fact]
    public void PitchFollowsFrequencyAndEchoLengthens()
    {
        // A 1 kHz square wave crosses zero about 2000 times a second.
        var fx = new SoundEffect { Wave = SoundWave.Square, Frequency = 1000, AttackMs = 0, SustainMs = 1000, DecayMs = 0, Volume = 100 };
        var s = SfxSynth.Render(fx);
        int crossings = 0;
        for (int i = 1; i < s.Length; i++) if (Math.Sign(s[i]) != Math.Sign(s[i - 1]) && s[i] != 0) crossings++;
        Assert.InRange(crossings, 1900, 2100);

        fx.EchoMs = 200; fx.EchoFeedback = 50;
        Assert.True(SfxSynth.Render(fx).Length > s.Length + SfxSynth.SampleRate / 2);
    }

    [Fact]
    public void WavRoundTripsAndReadsOtherFormats()
    {
        var samples = new float[] { 0, 0.5f, -0.5f, 1, -1, 0.25f };
        var (decoded, rate) = WavFile.Decode(WavFile.Encode(samples, 8000));
        Assert.Equal(8000, rate);
        for (int i = 0; i < samples.Length; i++) Assert.InRange(decoded[i], samples[i] - 0.001f, samples[i] + 0.001f);

        // 8-bit stereo: left full, right silent → mono half.
        var wav8 = new List<byte>();
        void Str(string x) => wav8.AddRange(System.Text.Encoding.ASCII.GetBytes(x));
        void I32(int x) => wav8.AddRange(BitConverter.GetBytes(x));
        void I16(short x) => wav8.AddRange(BitConverter.GetBytes(x));
        Str("RIFF"); I32(36 + 4); Str("WAVE"); Str("fmt "); I32(16); I16(1); I16(2); I32(11025); I32(22050); I16(2); I16(8);
        Str("LIST"); I32(2); wav8.AddRange(new byte[] { 0, 0 });            // an extra chunk to skip
        Str("data"); I32(4); wav8.AddRange(new byte[] { 255, 128, 0, 128 });
        var (stereo, r2) = WavFile.Decode(wav8.ToArray());
        Assert.Equal(11025, r2);
        Assert.Equal(2, stereo.Length);
        Assert.InRange(stereo[0], 0.49f, 0.5f);
        Assert.InRange(stereo[1], -0.5f, -0.49f);

        Assert.Throws<InvalidDataException>(() => WavFile.Decode(new byte[] { 0x49, 0x44, 0x33, 3, 0, 0, 0, 0, 0, 0, 0, 0 }));   // an MP3
    }

    [Fact]
    public void EditsWorkOnARangeOrTheWholeSound()
    {
        float[] S() => Enumerable.Range(0, 10).Select(i => i / 20f).ToArray();
        Assert.Equal(new[] { 0.1f, 0.15f, 0.2f }, SoundEditing.Trim(S(), 2, 5));
        Assert.Equal(7, SoundEditing.Delete(S(), 2, 5).Length);

        var s = S();
        SoundEditing.Normalize(s, 0, 0);            // empty range = whole sound
        Assert.Equal(0.95f, s.Max(), 3);

        s = S();
        SoundEditing.Reverse(s, 0, 10);
        Assert.Equal(0.45f, s[0], 3);

        s = Enumerable.Repeat(0.5f, 10).ToArray();
        SoundEditing.FadeIn(s, 0, 5);
        Assert.Equal(0f, s[0]);
        Assert.Equal(0.5f, s[9]);
        SoundEditing.Gain(s, 5, 10, 4);
        Assert.Equal(1f, s[9]);                      // clipped

        Assert.True(SoundEditing.Echo(new float[100], 100, 0.1, 0.5).Length > 100);
    }

    [Fact]
    public void GeneratedEffectsAreSavedWithTheGame()
    {
        var a = new Adventure();
        var fx = SfxPresets.Make("Chime", 4);
        a.Assets["sounds/snd1.wav"] = SfxSynth.RenderWav(fx);
        a.Sounds.Add(new SoundAsset { Id = "snd1", Name = "Chime", AssetName = "sounds/snd1.wav", Effect = fx, Volume = 0.65, Repeat = true, CutOffSeconds = 1.5 });
        var loaded = AdventurePackage.Load(AdventurePackage.SaveToBytes(a));
        var s = loaded.Sounds.Single();
        Assert.NotNull(s.Effect);
        Assert.Equal(fx.Frequency, s.Effect!.Frequency);
        Assert.Equal(SoundWave.Sine, s.Effect.Wave);
        Assert.True(WavFile.IsWav(loaded.Assets["sounds/snd1.wav"]));
        Assert.Equal((0.65, true, 1.5), (s.Volume, s.Repeat, s.CutOffSeconds));
    }
}
