using System.Text.Json;

namespace AdventureCreator.Core.Audio;

/// <summary>One recording in the built-in sound library (<c>assets/sounds/library.json</c>).</summary>
public sealed record SoundLibraryEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    /// <summary>The WAV file's name within the library.</summary>
    public string File { get; init; } = "";
    /// <summary>Made to repeat (ambience); the Studio switches on Repeat when it's used.</summary>
    public bool Loop { get; init; }
    public double Seconds { get; init; }
    public string Author { get; init; } = "";
    public string Source { get; init; } = "";
    public string OriginalFile { get; init; } = "";
    public string Url { get; init; } = "";
    public string Licence { get; init; } = "";
}

/// <summary>
/// The built-in library of CC0 sound recordings. The Studio bundles all of it; the example games embed the few they
/// use (<see cref="Embedded"/>). Credits and licences are in <c>assets/sounds/CREDITS.md</c>.
/// </summary>
public static class SoundLibrary
{
    public static IReadOnlyList<SoundLibraryEntry> Parse(string json) =>
        JsonSerializer.Deserialize<List<SoundLibraryEntry>>(json) ?? new List<SoundLibraryEntry>();

    /// <summary>The categories in library order.</summary>
    public static IReadOnlyList<string> Categories(IEnumerable<SoundLibraryEntry> entries) => entries.Select(e => e.Category).Distinct().ToList();

    /// <summary>A library sound embedded in this assembly (only those used by the example games), or null.</summary>
    public static byte[]? Embedded(string id)
    {
        using var stream = typeof(SoundLibrary).Assembly.GetManifestResourceStream($"sounds.{id}.wav");
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
