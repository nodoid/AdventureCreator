using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.ZMachine;

/// <summary>Wraps Z-code story files (raw .z1–.z8 or Blorb .zblorb/.zlb) as games the engine can run.</summary>
public static class ZStory
{
    public static readonly string[] Extensions = { "z1", "z2", "z3", "z4", "z5", "z6", "z7", "z8", "zblorb", "zlb", "blb", "dat" };

    public static string PictureId(int number) => $"zpic{number}";

    /// <summary>True for a plausible Z-code story file, or a Blorb file containing one.</summary>
    public static bool IsStory(byte[] data)
    {
        if (Blorb.IsBlorb(data))
        {
            try { return Blorb.Parse(data).Story is { } s && IsRawStory(s); }
            catch { return false; }
        }
        return IsRawStory(data);
    }

    private static bool IsRawStory(byte[] d)
    {
        if (d.Length < 64 || d[0] is < 1 or > 8) return false;
        int Word(int a) => (d[a] << 8) | d[a + 1];
        int high = Word(0x04), pc = Word(0x06), dictionary = Word(0x08), objects = Word(0x0A), globals = Word(0x0C), statics = Word(0x0E);
        int scale = d[0] <= 3 ? 2 : d[0] <= 5 ? 4 : 8;
        int length = Word(0x1A) * scale;
        bool inside(int a) => a >= 64 && a < d.Length;
        return inside(dictionary) && inside(objects) && inside(globals) && statics >= 64 && statics <= d.Length && high <= d.Length
               && (d[0] == 6 || inside(pc)) && (length == 0 || length <= d.Length + scale);
    }

    /// <summary>Makes a game that plays the story. Blorb pictures become pictures (the cover art is the intro picture).</summary>
    public static Adventure CreateAdventure(byte[] data, string fileName)
    {
        Blorb? blorb = Blorb.IsBlorb(data) ? Blorb.Parse(data) : null;
        var storyBytes = blorb?.Story ?? data;
        var machine = new ZMachine(storyBytes, 1);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var a = new Adventure
        {
            Title = !string.IsNullOrWhiteSpace(blorb?.Title) ? blorb!.Title! : Titled(name),
            Author = blorb?.Author ?? "",
            Description = $"Z-code story, version {machine.Version}, release {machine.Release}, serial {machine.Serial.Trim('\0', ' ')}.",
            StoryFile = $"story/{Safe(name)}.z{machine.Version}",
            StartRoomId = "story",
        };
        a.Assets[a.StoryFile] = storyBytes;
        a.Rooms.Add(new Room { Id = "story", Name = a.Title, Description = "This game is a Z-code story, played by the built-in Z-machine." });
        a.Settings.Prompt = ">";
        a.Settings.ShowStatusBar = true;
        a.Settings.AutoListExits = false;
        a.Settings.SpellingCorrection = false;

        if (blorb != null)
        {
            foreach (var (number, (format, bytes)) in blorb.Pictures.OrderBy(p => p.Key))
            {
                var (w, h) = Blorb.ImageSize(bytes);
                if (w <= 0 || h <= 0) continue;   // placeholder (Rect) or unknown format
                var asset = $"images/zpic{number}.{(format == "JPEG" ? "jpg" : format.ToLowerInvariant())}";
                a.Assets[asset] = bytes;
                a.Pictures.Add(new Picture
                {
                    Id = PictureId(number), Name = $"Story picture {number}", Width = w, Height = h,
                    RenderMode = PictureRenderMode.Smooth, BitmapAsset = asset, Palette = Palettes.Extended.ToList(),
                });
            }
            if (blorb.Frontispiece is int cover && a.FindPicture(PictureId(cover)) != null) a.IntroPictureId = PictureId(cover);
        }
        return a;
    }

    /// <summary>The Z-machine for a story game, told the sizes of its pictures (for V6).</summary>
    public static ZMachine CreateMachine(Adventure adventure, int? seed = null)
    {
        if (adventure.StoryFile == null || !adventure.Assets.TryGetValue(adventure.StoryFile, out var bytes))
            throw new InvalidDataException("The game's story file is missing.");
        var sizes = new Dictionary<int, (int, int)>();
        foreach (var p in adventure.Pictures)
            if (p.Id.StartsWith("zpic", StringComparison.Ordinal) && int.TryParse(p.Id[4..], out var n)) sizes[n] = (p.Width, p.Height);
        return new ZMachine(bytes, seed, sizes);
    }

    private static string Titled(string name)
    {
        var words = name.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "Story" : string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static string Safe(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "story" : s;
    }
}
