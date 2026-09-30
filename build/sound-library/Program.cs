using System.Text.Json;
using AdventureSystem.Core.Audio;

// args: manifest.tsv convDir outDir
var sources = new Dictionary<string, (string Pack, string Author, string Url)>
{
    ["K-RPG"] = ("RPG Audio", "Kenney (kenney.nl)", "https://kenney.nl/assets/rpg-audio"),
    ["K-IMP"] = ("Impact Sounds", "Kenney (kenney.nl)", "https://kenney.nl/assets/impact-sounds"),
    ["K-SCI"] = ("Sci-fi Sounds", "Kenney (kenney.nl)", "https://kenney.nl/assets/sci-fi-sounds"),
    ["K-UI"] = ("Interface Sounds", "Kenney (kenney.nl)", "https://kenney.nl/assets/interface-sounds"),
    ["K-DIG"] = ("Digital Audio", "Kenney (kenney.nl)", "https://kenney.nl/assets/digital-audio"),
    ["RD100"] = ("100 CC0 SFX", "rubberduck", "https://opengameart.org/content/100-cc0-sfx"),
    ["RD100v2"] = ("100 CC0 SFX #2", "rubberduck", "https://opengameart.org/content/100-cc0-sfx-2"),
    ["RDLOOP"] = ("30 CC0 SFX loops", "rubberduck", "https://opengameart.org/content/30-cc0-sfx-loops"),
    ["RDWATER"] = ("40 CC0 water / splash / slime SFX", "rubberduck", "https://opengameart.org/content/40-cc0-water-splash-slime-sfx"),
    ["RDRPG"] = ("80 CC0 RPG SFX", "rubberduck", "https://opengameart.org/content/80-cc0-rpg-sfx"),
    ["RDBANG"] = ("25 CC0 bang / firework SFX", "rubberduck", "https://opengameart.org/content/25-cc0-bang-firework-sfx"),
    ["WUXIA"] = ("Rain + Long Thunder", "WuxiaScrub", "https://opengameart.org/content/rain-long-thunder"),
    ["QUBO"] = ("Beach Ocean Waves", "jasinski (Freesound #18363), shared by qubodup", "https://opengameart.org/content/beach-ocean-waves"),
    ["FVC"] = ("Classic fanfare lick", "fvcalderan", "https://opengameart.org/content/classic-fanfare-lick"),
    ["MUSH-F"] = ("Foghorn Kinda Gross", "Musheran", "https://opengameart.org/content/foghorn-kinda-gross"),
    ["MUSH-R"] = ("Low Rumbling", "Musheran", "https://opengameart.org/content/low-rumbling"),
    ["SKETCH"] = ("wind whoosh loop", "SketchMan3", "https://opengameart.org/content/wind-whoosh-loop"),
    ["JAGGED"] = ("Loopable Dungeon Ambience", "JaggedStone", "https://opengameart.org/content/loopable-dungeon-ambience"),
    ["PYRANO"] = ("Air whoosh", "pyranostudios", "https://opengameart.org/content/air-whoosh"),
};
var entries = new List<object>();
Directory.CreateDirectory(args[2]);
long total = 0;
foreach (var line in File.ReadAllLines(args[0]))
{
    if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
    var f = line.Split('\t');
    var (pack, file, id, name, category, loop, max) = (f[0], f[1], f[2], f[3], f[4], f[5] == "1", double.Parse(f[6]));
    double start = f.Length > 7 ? double.Parse(f[7]) : 0;
    var (s, rate) = WavFile.Decode(File.ReadAllBytes(Path.Combine(args[1], id + ".wav")));
    if (start > 0) { s = SoundEditing.Trim(s, (int)(start * rate), s.Length); SoundEditing.FadeIn(s, 0, rate / 2); }
    if (max > 0 && s.Length > max * rate)
    {
        s = SoundEditing.Trim(s, 0, (int)(max * rate));
        if (!loop) SoundEditing.FadeOut(s, rate, 0.8);
    }
    if (loop)
    {
        // Short fades so a loop restarts without a click.
        SoundEditing.FadeIn(s, 0, rate / 40);
        SoundEditing.FadeOut(s, rate, 0.025);
    }
    // Even levels across the library: one-shots peak at 90 %, loops (background sounds) at 75 %.
    float peak = s.Max(Math.Abs);
    float target = loop ? 0.75f : 0.9f;
    if (peak > 0) SoundEditing.Gain(s, 0, s.Length, Math.Min(100f, target / peak));
    var bytes = WavFile.Encode(s, rate);
    File.WriteAllBytes(Path.Combine(args[2], id + ".wav"), bytes);
    total += bytes.Length;
    var src = sources[pack];
    entries.Add(new { Id = id, Name = name, Category = category, File = id + ".wav", Loop = loop, Seconds = Math.Round((double)s.Length / rate, 2),
        Author = src.Author, Source = src.Pack, OriginalFile = file, Url = src.Url, Licence = "CC0 1.0" });
    Console.WriteLine($"{id,-18} {(double)s.Length / rate,6:0.00}s  was {peak:0.00}  {bytes.Length / 1024,5} KB");
}
File.WriteAllText(Path.Combine(args[2], "library.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{entries.Count} sounds, {total / 1024 / 1024.0:0.0} MB");
