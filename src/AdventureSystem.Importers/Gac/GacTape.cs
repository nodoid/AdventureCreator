namespace AdventureSystem.Importers.Gac;

/// <summary>A CODE (or other) file found on a Spectrum tape image.</summary>
internal sealed record TapeFile(int Type, string Name, int Start, byte[] Data)
{
    /// <summary>Index (in <see cref="GacTape.ReadBlocks"/>) of the block holding the data; a header, if any, is the block before.</summary>
    public int Block { get; init; } = -1;
}

/// <summary>Minimal reader and writer for ZX Spectrum .TAP and .TZX tape images (standard, turbo and pure-data blocks).</summary>
internal static class GacTape
{
    public static bool IsTzx(byte[] data) =>
        data.Length >= 10 && data[0] == 'Z' && data[1] == 'X' && data[2] == 'T' && data[3] == 'a' && data[4] == 'p' && data[5] == 'e' && data[6] == '!';

    /// <summary>Raw blocks (flag byte + data + checksum), in tape order.</summary>
    public static List<byte[]> ReadBlocks(byte[] data) =>
        Locate(data).Select(b => data.AsSpan(b.Start, b.Length).ToArray()).ToList();

    /// <summary>Where a block's bytes are, and where its length is stored (offset and size in bytes).</summary>
    private readonly record struct BlockAt(int Start, int Length, int LengthAt, int LengthSize);

    private static List<BlockAt> Locate(byte[] data) => IsTzx(data) ? LocateTzx(data) : LocateTap(data);

    private static List<BlockAt> LocateTap(byte[] d)
    {
        var blocks = new List<BlockAt>();
        int p = 0;
        while (p + 2 <= d.Length)
        {
            int len = d[p] | (d[p + 1] << 8);
            if (len == 0 || p + 2 + len > d.Length) break;
            blocks.Add(new BlockAt(p + 2, len, p, 2));
            p += 2 + len;
        }
        return blocks;
    }

    private static int W(byte[] d, int p) => p + 1 < d.Length ? d[p] | (d[p + 1] << 8) : 0;
    private static int L3(byte[] d, int p) => p + 2 < d.Length ? d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) : 0;
    private static int L4(byte[] d, int p) => p + 3 < d.Length ? d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24) : 0;

    private static List<BlockAt> LocateTzx(byte[] d)
    {
        var blocks = new List<BlockAt>();
        int p = 10;
        while (p < d.Length)
        {
            int id = d[p++];
            int dataStart = -1, dataLen = 0, lengthAt = 0, lengthSize = 0, skip;
            switch (id)
            {
                case 0x10: dataLen = W(d, p + 2); dataStart = p + 4; lengthAt = p + 2; lengthSize = 2; skip = 4 + dataLen; break;
                case 0x11: dataLen = L3(d, p + 0x0F); dataStart = p + 0x12; lengthAt = p + 0x0F; lengthSize = 3; skip = 0x12 + dataLen; break;
                case 0x14: dataLen = L3(d, p + 0x07); dataStart = p + 0x0A; lengthAt = p + 0x07; lengthSize = 3; skip = 0x0A + dataLen; break;
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
                blocks.Add(new BlockAt(dataStart, dataLen, lengthAt, lengthSize));
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
                files.Add(new TapeFile(type, name, start, body.AsSpan(1, body.Length - 2).ToArray()) { Block = i + 1 });
                i++;
            }
            else if (b.Length >= 2 && b[0] == 0xFF)
            {
                files.Add(new TapeFile(-1, "", -1, b.AsSpan(1, b.Length - 2).ToArray()) { Block = i });
            }
        }
        return files;
    }

    // ---- writing ----------------------------------------------------------------------------------------------

    /// <summary>A standard block: flag byte, contents and the XOR checksum.</summary>
    public static byte[] Block(byte flag, ReadOnlySpan<byte> contents)
    {
        var block = new byte[contents.Length + 2];
        block[0] = flag;
        contents.CopyTo(block.AsSpan(1));
        byte x = 0;
        for (int i = 0; i < block.Length - 1; i++) x ^= block[i];
        block[^1] = x;
        return block;
    }

    /// <summary>A CODE file's data block and its header with the new length (the header's other fields are kept).</summary>
    public static (byte[] Header, byte[] Data) CodeFile(byte[] oldHeader, ReadOnlySpan<byte> contents)
    {
        var fields = oldHeader.AsSpan(1, 17).ToArray();
        fields[11] = (byte)contents.Length;
        fields[12] = (byte)(contents.Length >> 8);
        return (Block(0x00, fields), Block(0xFF, contents));
    }

    /// <summary>The tape with some blocks replaced (key = block index), their stored lengths updated; everything else is kept byte for byte.</summary>
    public static byte[] Rewrite(byte[] data, IReadOnlyDictionary<int, byte[]> replacements)
    {
        var output = new List<byte>(data.Length);
        int copied = 0;
        var blocks = Locate(data);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!replacements.TryGetValue(i, out var block)) continue;
            var b = blocks[i];
            output.AddRange(data.AsSpan(copied, b.LengthAt - copied).ToArray());
            for (int k = 0; k < b.LengthSize; k++) output.Add((byte)(block.Length >> (8 * k)));
            output.AddRange(data.AsSpan(b.LengthAt + b.LengthSize, b.Start - b.LengthAt - b.LengthSize).ToArray());
            output.AddRange(block);
            copied = b.Start + b.Length;
        }
        output.AddRange(data.AsSpan(copied).ToArray());
        return output.ToArray();
    }
}
