using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Core.Packaging;

/// <summary>
/// Reads and writes adventure packages (<c>.adventure</c>): a zip file containing <c>adventure.json</c>
/// and an <c>assets/</c> folder with images and audio.
/// </summary>
public static class AdventurePackage
{
    public const string Extension = ".adventure";
    public const string JsonEntry = "adventure.json";
    public const string AssetFolder = "assets/";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(Adventure adventure) => JsonSerializer.Serialize(adventure, JsonOptions);

    public static Adventure FromJson(string json) =>
        JsonSerializer.Deserialize<Adventure>(json, JsonOptions) ?? throw new InvalidDataException("Empty adventure file.");

    public static void Save(Adventure adventure, string path)
    {
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) Save(adventure, fs);
        File.Move(tmp, path, overwrite: true);
    }

    public static byte[] SaveToBytes(Adventure adventure)
    {
        using var ms = new MemoryStream();
        Save(adventure, ms);
        return ms.ToArray();
    }

    public static void Save(Adventure adventure, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var entry = zip.CreateEntry(JsonEntry, CompressionLevel.Optimal);
        using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            w.Write(ToJson(adventure));
        foreach (var (name, data) in adventure.Assets)
        {
            // Already-compressed media is stored, everything else deflated.
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var level = ext is ".png" or ".jpg" or ".jpeg" or ".mp3" or ".ogg" or ".m4a" or ".aac" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            var e = zip.CreateEntry(AssetFolder + name.Replace('\\', '/'), level);
            using var s = e.Open();
            s.Write(data);
        }
    }

    public static Adventure Load(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            return FromJson(File.ReadAllText(path));
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>
    /// Opens any game file: an .adventure package, a JSON game, or a Z-code story (.z1–.z8, .zblorb), which becomes a
    /// game played by the built-in Z-machine.
    /// </summary>
    public static Adventure LoadAny(byte[] data, string fileName)
    {
        if (ZMachine.ZStory.IsStory(data)) return ZMachine.ZStory.CreateAdventure(data, fileName);
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return FromJson(System.Text.Encoding.UTF8.GetString(data));
        return Load(data);
    }

    public static Adventure Load(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Load(ms);
    }

    public static Adventure Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var jsonEntry = zip.GetEntry(JsonEntry) ?? throw new InvalidDataException("The package has no adventure.json.");
        Adventure adventure;
        using (var r = new StreamReader(jsonEntry.Open(), Encoding.UTF8))
            adventure = FromJson(r.ReadToEnd());
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith(AssetFolder, StringComparison.Ordinal) || e.FullName.EndsWith('/')) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            adventure.Assets[e.FullName[AssetFolder.Length..]] = ms.ToArray();
        }
        return adventure;
    }

    /// <summary>Deep copy (used by the Studio's test player so edits don't affect a running game).</summary>
    public static Adventure Clone(Adventure adventure)
    {
        var copy = FromJson(ToJson(adventure));
        foreach (var (k, v) in adventure.Assets) copy.Assets[k] = v;
        return copy;
    }
}
