using System.Text;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.ZMachine;
using AdventureSystem.Importers.Common;

namespace AdventureSystem.Importers.ZCode;

/// <summary>Z-code stories are kept byte for byte, so they are written back unchanged (as a Blorb when they have pictures).</summary>
public sealed class ZCodeExporter : IAdventureExporter
{
    public string Id => "zcode";
    public string Name => "Z-code story";

    public string Extension(Adventure a) => HasPictures(a) ? "zblorb" : $"z{Story(a)?[0] ?? 5}";

    public string? CannotExport(Adventure a) =>
        !a.IsStory ? "Only Z-code stories (imported .z3/.z5/.z8/.zblorb files) can be exported as Z-code: Z-code is compiled from Inform or ZIL source." : null;

    private static byte[]? Story(Adventure a) => a.StoryFile != null && a.Assets.TryGetValue(a.StoryFile, out var s) ? s : null;

    private static bool HasPictures(Adventure a) => a.Pictures.Any(p => p.Id.StartsWith("zpic", StringComparison.Ordinal));

    public ExportResult Export(Adventure a)
    {
        var story = Story(a) ?? throw new InvalidOperationException(CannotExport(a) ?? "The story file is missing.");
        if (!HasPictures(a)) return new ExportResult(story) { Summary = $"Z-code version {story[0]}, {story.Length / 1024} KB" };

        // A Blorb: the story, its pictures and the cover art.
        var chunks = new List<(string Usage, int Number, string Type, byte[] Data)> { ("Exec", 0, "ZCOD", story) };
        foreach (var p in a.Pictures.Where(p => p.Id.StartsWith("zpic", StringComparison.Ordinal)))
        {
            if (!int.TryParse(p.Id[4..], out var n) || p.BitmapAsset == null || !a.Assets.TryGetValue(p.BitmapAsset, out var img)) continue;
            chunks.Add(("Pict", n, img.Length > 1 && img[0] == 0xFF && img[1] == 0xD8 ? "JPEG" : "PNG ", img));
        }
        int? cover = a.IntroPictureId is { } intro && intro.StartsWith("zpic", StringComparison.Ordinal) && int.TryParse(intro[4..], out var c) ? c : null;
        var result = new ExportResult(WriteBlorb(chunks, cover, a.Title, a.Author)) { Summary = $"Z-code version {story[0]} in a Blorb with {chunks.Count - 1} picture(s)" };
        return result;
    }

    internal static byte[] WriteBlorb(List<(string Usage, int Number, string Type, byte[] Data)> chunks, int? frontispiece, string title, string author)
    {
        static void BE(List<byte> b, int v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }
        static void Str(List<byte> b, string s) => b.AddRange(Encoding.ASCII.GetBytes(s));
        var meta = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ifindex version=\"1.0\" xmlns=\"http://babel.ifarchive.org/protocol/iFiction/\"><story><bibliographic><title>{System.Net.WebUtility.HtmlEncode(title)}</title><author>{System.Net.WebUtility.HtmlEncode(author)}</author></bibliographic></story></ifindex>");
        int indexLength = 4 + chunks.Count * 12;
        int offset = 12 + 8 + indexLength + (frontispiece.HasValue ? 12 : 0) + 8 + meta.Length + (meta.Length & 1);
        var index = new List<byte>();
        var body = new List<byte>();
        BE(index, chunks.Count);
        foreach (var (usage, number, type, data) in chunks)
        {
            Str(index, usage); BE(index, number); BE(index, offset + body.Count);
            Str(body, type); BE(body, data.Length); body.AddRange(data);
            if ((data.Length & 1) != 0) body.Add(0);
        }
        var file = new List<byte>();
        Str(file, "FORM");
        BE(file, 0);   // patched below
        Str(file, "IFRS");
        Str(file, "RIdx"); BE(file, indexLength); file.AddRange(index);
        if (frontispiece.HasValue) { Str(file, "Fspc"); BE(file, 4); BE(file, frontispiece.Value); }
        Str(file, "IFmd"); BE(file, meta.Length); file.AddRange(meta); if ((meta.Length & 1) != 0) file.Add(0);
        file.AddRange(body);
        var bytes = file.ToArray();
        int length = bytes.Length - 8;
        bytes[4] = (byte)(length >> 24); bytes[5] = (byte)(length >> 16); bytes[6] = (byte)(length >> 8); bytes[7] = (byte)length;
        return bytes;
    }
}
