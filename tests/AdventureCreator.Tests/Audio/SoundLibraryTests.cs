using AdventureCreator.Core.Audio;
using AdventureCreator.Core.Samples;

namespace AdventureCreator.Tests.Audio;

public class SoundLibraryTests
{
    private static string LibraryFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "sounds");
            if (File.Exists(Path.Combine(candidate, "library.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("assets/sounds not found");
    }

    private static IReadOnlyList<SoundLibraryEntry> Library() => SoundLibrary.Parse(File.ReadAllText(Path.Combine(LibraryFolder(), "library.json")));

    [Fact]
    public void EverySoundIsAPlayableWavWithACc0Credit()
    {
        var folder = LibraryFolder();
        var library = Library();
        Assert.True(library.Count >= 80);
        Assert.Equal(library.Count, library.Select(e => e.Id).Distinct().Count());
        foreach (var e in library)
        {
            var (samples, rate) = WavFile.Decode(File.ReadAllBytes(Path.Combine(folder, e.File)));
            Assert.Equal(22050, rate);
            Assert.True(samples.Length > 0 && samples.Max(Math.Abs) > 0.5f, $"{e.Id} is too quiet");
            Assert.InRange((double)samples.Length / rate, e.Seconds - 0.05, e.Seconds + 0.05);
            Assert.Equal("CC0 1.0", e.Licence);
            Assert.False(string.IsNullOrWhiteSpace(e.Author), e.Id);
            Assert.StartsWith("https://", e.Url);
        }
        // Nothing in the folder that isn't catalogued.
        var files = Directory.GetFiles(folder, "*.wav").Select(f => Path.GetFileName(f)!).ToHashSet();
        Assert.Equal(files, library.Select(e => e.File).ToHashSet());
    }

    [Fact]
    public void LicencesAndCreditsAreIncluded()
    {
        var folder = LibraryFolder();
        var credits = File.ReadAllText(Path.Combine(folder, "CREDITS.md"));
        foreach (var e in Library())
        {
            Assert.Contains($"`{e.Id}`", credits);
            Assert.Contains(e.Url, credits);
        }
        Assert.Contains("CC0 1.0 Universal", File.ReadAllText(Path.Combine(folder, "CC0-1.0.txt")));
        Assert.Contains("Creative Commons Zero", File.ReadAllText(Path.Combine(folder, "Kenney-License.txt")));
    }

    [Fact]
    public void ExampleGamesUseEmbeddedLibrarySounds()
    {
        var folder = LibraryFolder();
        foreach (var game in new[] { ExampleAdventures.Lighthouse(), ExampleAdventures.Genesis() })
            foreach (var sound in game.Sounds)
            {
                var bytes = game.Assets[sound.AssetName];
                var file = Path.Combine(folder, Path.GetFileName(sound.AssetName));
                Assert.True(File.Exists(file), $"{sound.Id} doesn't come from the library");
                Assert.Equal(File.ReadAllBytes(file), bytes);
            }
    }
}
