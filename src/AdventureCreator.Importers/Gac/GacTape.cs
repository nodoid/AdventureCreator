namespace AdventureCreator.Importers.Gac;

/// <summary>A CODE (or other) file found on a Spectrum tape image.</summary>
internal sealed record TapeFile(int Type, string Name, int Start, byte[] Data);

/// <summary>Minimal reader for ZX Spectrum .TAP and .TZX tape images (standard, turbo and pure-data blocks).</summary>
internal static class GacTape
{
    public static bool IsTzx(byte[] data) =>
        data.Length >= 10 && data[0] == 'Z' && data[1] == 'X' && data[2] == 'T' && data[3] == 'a' && data[4] == 'p' && data[5] == 'e' && data[6] == '!';

    /// <summary>Raw blocks (flag byte + data + checksum), in tape order.</summary>
    public static List<byte[]> ReadBlocks(byte[] data) => IsTzx(data) ? ReadTzxBlocks(data) : ReadTapBlocks(data);

    private static List<byte[]> ReadTapBlocks(byte[] d)
    {
        var blocks = new List<byte[]>();
        int p = 0;
        while (p + 2 <= d.Length)
        {
            int len = d[p] | (d[p + 1] << 8);
            p += 2;
            if (len == 0 || p + len > d.Length) break;
            blocks.Add(d.AsSpan(p, len).ToArray());
            p += len;
        }
        return blocks;
    }

    private static int W(byte[] d, int p) => p + 1 < d.Length ? d[p] | (d[p + 1] << 8) : 0;
    private static int L3(byte[] d, int p) => p + 2 < d.Length ? d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) : 0;
    private static int L4(byte[] d, int p) => p + 3 < d.Length ? d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24) : 0;

    private static List<byte[]> ReadTzxBlocks(byte[] d)
    {
        var blocks = new List<byte[]>();
        int p = 10;
        while (p < d.Length)
        {
            int id = d[p++];
            int dataStart = -1, dataLen = 0, skip;
            switch (id)
            {
                case 0x10: dataLen = W(d, p + 2); dataStart = p + 4; skip = 4 + dataLen; break;
                case 0x11: dataLen = L3(d, p + 0x0F); dataStart = p + 0x12; skip = 0x12 + dataLen; break;
                case 0x14: dataLen = L3(d, p + 0x07); dataStart = p + 0x0A; skip = 0x0A + dataLen; break;
                case 0x12: skip = 4; break;
                case 0x13: skip = 1 + (p < d.Length ? d[p] * 2 : 0); break;
                case 0x15: skip = 8 + L3(d, p + 5); break;
                case 0x18: case 0x19: skip = 4 + L4(d, p); break;
                case 0x20: case 0x23: case 0x24: skip = 2; break;
                case 0x21: case 0x30: skip = 1 + (p < d.Length ? d[p] : 0); break;
                case 0x22: case 0x25: case 0x27: skip = 0; break;
                case 0x26: skip = 2 + W(d, p) * 2; break;
                case 0x28: case 0x32: skip = 2 + W(d, p); break;
                case 0x2A: skip = 4; break;
                case 0x2B: skip = 5; break;
                case 0x31: skip = 2 + (p + 1 < d.Length ? d[p + 1] : 0); break;
                case 0x33: skip = 1 + (p < d.Length ? d[p] * 3 : 0); break;
                case 0x35: skip = 0x14 + L4(d, p + 0x10); break;
                case 0x5A: skip = 9; break;
                default: return blocks; // unknown block: stop
            }
            if (dataStart >= 0 && dataLen > 0 && dataStart + dataLen <= d.Length)
                blocks.Add(d.AsSpan(dataStart, dataLen).ToArray());
            if (skip < 0) break;
            p += skip;
        }
        return blocks;
    }

    /// <summary>Pairs standard headers with the data blocks that follow them. Headerless blocks get Type = -1.</summary>
    public static List<TapeFile> ReadFiles(byte[] data)
    {
        var files = new List<TapeFile>();
        var blocks = ReadBlocks(data);
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.Length == 19 && b[0] == 0x00 && i + 1 < blocks.Count && blocks[i + 1].Length >= 2 && blocks[i + 1][0] == 0xFF)
            {
                int type = b[1];
                var name = new string(b.Skip(2).Take(10).Select(c => c is >= 32 and < 127 ? (char)c : '?').ToArray()).TrimEnd();
                int start = b[14] | (b[15] << 8);
                var body = blocks[i + 1];
                files.Add(new TapeFile(type, name, start, body.AsSpan(1, body.Length - 2).ToArray()));
                i++;
            }
            else if (b.Length >= 2 && b[0] == 0xFF)
            {
                files.Add(new TapeFile(-1, "", -1, b.AsSpan(1, b.Length - 2).ToArray()));
            }
        }
        return files;
    }
}
