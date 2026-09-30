using System.Text;

namespace AdventureCreator.Core.ZMachine;

/// <summary>
/// Reads Blorb resource files (IFF "IFRS", the standard container for Z-code stories: .zblorb / .blb). Gives the
/// story (ZCOD), pictures (PNG / JPEG, by resource number), the frontispiece (cover art) and sounds.
/// </summary>
public sealed class Blorb
{
    public byte[]? Story { get; private set; }
    public Dictionary<int, (string Format, byte[] Data)> Pictures { get; } = new();
    public Dictionary<int, (string Format, byte[] Data)> Sounds { get; } = new();
    /// <summary>The picture number used as cover art, if the file names one.</summary>
    public int? Frontispiece { get; private set; }
    /// <summary>Title and author from the iFiction metadata chunk, when present.</summary>
    public string? Title { get; private set; }
    public string? Author { get; private set; }

    public static bool IsBlorb(byte[] data) =>
        data.Length >= 12 && data[0] == 'F' && data[1] == 'O' && data[2] == 'R' && data[3] == 'M' && Encoding.ASCII.GetString(data, 8, 4) == "IFRS";

    public static Blorb Parse(byte[] data)
    {
        if (!IsBlorb(data)) throw new InvalidDataException("Not a Blorb file.");
        var b = new Blorb();
        var chunksAt = new Dictionary<int, (string Type, int Start, int Length)>();
        int end = Math.Min(data.Length, 8 + BigEndian(data, 4));
        var index = new List<(string Usage, int Number, int Start)>();
        for (int p = 12; p + 8 <= end;)
        {
            string type = Encoding.ASCII.GetString(data, p, 4);
            int length = BigEndian(data, p + 4);
            int body = p + 8;
            if (body + length > data.Length) break;
            chunksAt[p] = (type, body, length);
            switch (type)
            {
                case "RIdx":
                {
                    int count = BigEndian(data, body);
                    for (int i = 0; i < count; i++)
                    {
                        int e = body + 4 + i * 12;
                        index.Add((Encoding.ASCII.GetString(data, e, 4), BigEndian(data, e + 4), BigEndian(data, e + 8)));
                    }
                    break;
                }
                case "Fspc": b.Frontispiece = BigEndian(data, body); break;
                case "IFmd":
                {
                    var xml = Encoding.UTF8.GetString(data, body, length);
                    b.Title = Tag(xml, "title");
                    b.Author = Tag(xml, "author");
                    break;
                }
            }
            p = body + length + (length & 1);
        }
        foreach (var (usage, number, start) in index)
        {
            if (!chunksAt.TryGetValue(start, out var chunk)) continue;
            var bytes = data.AsSpan(chunk.Start, chunk.Length).ToArray();
            switch (usage)
            {
                case "Exec" when chunk.Type == "ZCOD": b.Story = bytes; break;
                case "Pict": b.Pictures[number] = (chunk.Type.Trim(), bytes); break;
                case "Snd ": b.Sounds[number] = (chunk.Type.Trim(), bytes); break;
            }
        }
        return b;
    }

    private static int BigEndian(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

    private static string? Tag(string xml, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(xml, $"<{name}>(.*?)</{name}>", System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : null;
    }

    /// <summary>Width and height of a PNG, JPEG or GIF image (0, 0 if unknown).</summary>
    public static (int Width, int Height) ImageSize(byte[] d)
    {
        if (d.Length >= 24 && d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G')
            return (BigEndian(d, 16), BigEndian(d, 20));
        if (d.Length >= 10 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F')
            return (d[6] | d[7] << 8, d[8] | d[9] << 8);
        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
        {
            for (int p = 2; p + 9 < d.Length;)
            {
                if (d[p] != 0xFF) { p++; continue; }
                int marker = d[p + 1];
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { p += 2; continue; }
                int length = (d[p + 2] << 8) | d[p + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return ((d[p + 7] << 8) | d[p + 8], (d[p + 5] << 8) | d[p + 6]);
                p += 2 + length;
            }
        }
        return (0, 0);
    }
}
