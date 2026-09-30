using AdventureCreator.Core.Model;
using Plugin.Maui.Audio;

namespace AdventureCreator.Maui;

/// <summary>
/// Plays an adventure's sound assets: one looping ambient track plus any number of one-shot effects. Honours each
/// sound's Repeat and cut-off settings.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly IAudioManager manager;
    private IAudioPlayer? ambient;
    private string? ambientId;
    private readonly List<IAudioPlayer> effects = new();

    public AudioService(IAudioManager? manager = null) => this.manager = manager ?? AudioManager.Current;

    public bool Muted { get; set; }
    public double MasterVolume { get; set; } = 1.0;

    public void Play(Adventure adventure, string? soundId, bool loop)
    {
        if (Muted || soundId == null) return;
        var sound = adventure.FindSound(soundId);
        if (sound == null || !adventure.Assets.TryGetValue(sound.AssetName, out var data)) return;
        loop |= sound.Repeat;
        try
        {
            if (loop)
            {
                if (ambientId == soundId && ambient?.IsPlaying == true) return;
                StopAmbient();
                ambient = manager.CreatePlayer(new MemoryStream(data));
                ambient.Loop = true;
                ambient.Volume = Math.Clamp(sound.Volume * MasterVolume, 0, 1);
                ambient.Play();
                ambientId = soundId;
            }
            else
            {
                effects.RemoveAll(p =>
                {
                    if (p.IsPlaying) return false;
                    p.Dispose();
                    return true;
                });
                var player = manager.CreatePlayer(new MemoryStream(data));
                player.Volume = Math.Clamp(sound.Volume * MasterVolume, 0, 1);
                player.Play();
                effects.Add(player);
                CutOff(player, sound.CutOffSeconds);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio playback failed for {soundId}: {ex.Message}");
        }
    }

    /// <summary>Plays raw audio bytes (used by the Studio to preview sounds), optionally looping or cut off early.</summary>
    public void Preview(byte[] data, double volume = 1, bool loop = false, double cutOffSeconds = 0)
    {
        StopAll();
        try
        {
            var p = manager.CreatePlayer(new MemoryStream(data));
            p.Volume = Math.Clamp(volume, 0, 1);
            p.Loop = loop;
            p.Play();
            effects.Add(p);
            if (!loop) CutOff(p, cutOffSeconds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Audio preview failed: " + ex.Message);
        }
    }

    /// <summary>Stops a one-shot sound after <paramref name="seconds"/> (0 = let it finish).</summary>
    private void CutOff(IAudioPlayer player, double seconds)
    {
        if (seconds <= 0) return;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds)).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!effects.Contains(player)) return;   // already stopped and disposed
            try { player.Stop(); } catch { }
        }));
    }

    public void StopAmbient()
    {
        ambient?.Stop();
        ambient?.Dispose();
        ambient = null;
        ambientId = null;
    }

    public void StopAll()
    {
        StopAmbient();
        foreach (var p in effects)
        {
            p.Stop();
            p.Dispose();
        }
        effects.Clear();
    }

    public void Dispose() => StopAll();
}
