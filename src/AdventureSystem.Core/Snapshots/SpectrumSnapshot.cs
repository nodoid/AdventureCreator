namespace AdventureSystem.Core.Snapshots;

/// <summary>
/// A 64K view of ZX Spectrum memory loaded from a .SNA or .Z80 snapshot (48K images; for 128K
/// snapshots the pages that are mapped at 0xC000 on load are used), or a raw memory dump.
/// Importers use it to locate PAWS / Quill / GAC databases.
/// </summary>
public sealed class SpectrumSnapshot
{
    public byte[] Memory { get; } = new byte[65536];
    public string Format { get; private set; } = "raw";
    /// <summary>All 16K RAM banks for 128K snapshots (index = bank number), otherwise null.</summary>
    public byte[][]? Banks128 { get; private set; }

    public byte this[int address] => Memory[address & 0xFFFF];
    public int Word(int address) => Memory[address & 0xFFFF] | (Memory[(address + 1) & 0xFFFF] << 8);

    public static SpectrumSnapshot Load(string path) => Load(File.ReadAllBytes(path), Path.GetExtension(path));

    public static SpectrumSnapshot Load(byte[] data, string? extension = null)
    {
        var ext = (extension ?? "").TrimStart('.').ToLowerInvariant();
        if (ext == "sna" || (ext == "" && (data.Length == 49179 || data.Length == 131103 || data.Length == 147487)))
            return LoadSna(data);
        if (ext == "z80")
            return LoadZ80(data);
        if (ext is "bin" or "mem" or "raw" or "" && data.Length == 49152)
            return FromRam48(data, "raw48");
        if (data.Length == 65536)
        {
            var s = new SpectrumSnapshot { Format = "raw64" };
            data.CopyTo(s.Memory, 0);
            return s;
        }
        throw new InvalidDataException($"Unrecognised Spectrum snapshot ({data.Length} bytes, .{ext}).");
    }

    public static SpectrumSnapshot FromRam48(byte[] ram, string format)
    {
        var s = new SpectrumSnapshot { Format = format };
        Array.Copy(ram, 0, s.Memory, 0x4000, Math.Min(ram.Length, 49152));
        return s;
    }

    private static SpectrumSnapshot LoadSna(byte[] data)
    {
        if (data.Length < 49179) throw new InvalidDataException("SNA file too short.");
        var s = FromRam48(data.AsSpan(27, 49152).ToArray(), "sna48");
        if (data.Length > 49179 + 4)
        {
            // 128K SNA: 48K dump contains banks 5, 2 and the paged bank; remaining banks follow.
            s.Format = "sna128";
            int port = data[49181];
            int paged = port & 7;
            var banks = new byte[8][];
            banks[5] = data.AsSpan(27, 16384).ToArray();
            banks[2] = data.AsSpan(27 + 16384, 16384).ToArray();
            banks[paged] = data.AsSpan(27 + 32768, 16384).ToArray();
            int offset = 49183;
            for (int b = 0; b < 8; b++)
            {
                if (b == 5 || b == 2 || b == paged) continue;
                if (offset + 16384 > data.Length) break;
                banks[b] = data.AsSpan(offset, 16384).ToArray();
                offset += 16384;
            }
            s.Banks128 = banks;
        }
        return s;
    }

    private static SpectrumSnapshot LoadZ80(byte[] data)
    {
        if (data.Length < 30) throw new InvalidDataException("Z80 file too short.");
        int pc = data[6] | (data[7] << 8);
        var s = new SpectrumSnapshot();
        if (pc != 0)
        {
            // Version 1: 48K only.
            s.Format = "z80v1";
            bool compressed = (data[12] == 255 ? 1 : data[12] & 0x20) != 0;
            var ram = compressed ? Decompress(data, 30, data.Length - 30, 49152, true) : data.AsSpan(30).ToArray();
            Array.Copy(ram, 0, s.Memory, 0x4000, Math.Min(ram.Length, 49152));
            return s;
        }

        int extraLen = data[30] | (data[31] << 8);
        int hwMode = data[34];
        bool is128 = extraLen == 23 ? hwMode >= 3 : hwMode >= 4;
        if (hwMode is 7 or 8 or 9 or 12 or 13) is128 = true;
        s.Format = extraLen == 23 ? "z80v2" : "z80v3";
        int pos = 32 + extraLen;
        var banks = new byte[8][];
        while (pos + 3 <= data.Length)
        {
            int len = data[pos] | (data[pos + 1] << 8);
            int page = data[pos + 2];
            pos += 3;
            byte[] block;
            if (len == 0xFFFF)
            {
                block = data.AsSpan(pos, Math.Min(16384, data.Length - pos)).ToArray();
                pos += 16384;
            }
            else
            {
                block = Decompress(data, pos, len, 16384, false);
                pos += len;
            }

            if (is128)
            {
                if (page >= 3 && page <= 10) banks[page - 3] = block;
            }
            else
            {
                int addr = page switch { 8 => 0x4000, 4 => 0x8000, 5 => 0xC000, _ => -1 };
                if (addr >= 0) Array.Copy(block, 0, s.Memory, addr, Math.Min(block.Length, 16384));
            }
        }

        if (is128)
        {
            int paged = data[35] & 7;
            void Map(int bank, int addr) { if (banks[bank] != null) Array.Copy(banks[bank], 0, s.Memory, addr, 16384); }
            Map(5, 0x4000); Map(2, 0x8000); Map(paged, 0xC000);
            s.Banks128 = banks;
        }
        return s;
    }

    // ================================================================ writing

    /// <summary>
    /// Writes this snapshot back in the format of <paramref name="original"/> (the file it was loaded from): the
    /// registers and hardware state come from the original, the RAM from <see cref="Memory"/> (48K) or
    /// <see cref="Banks128"/> (128K).
    /// </summary>
    public byte[] Save(byte[] original)
    {
        switch (Format)
        {
            case "sna48":
            {
                var data = (byte[])original.Clone();
                Array.Copy(Memory, 0x4000, data, 27, 49152);
                return data;
            }
            case "sna128":
            {
                var data = (byte[])original.Clone();
                int paged = data[49181] & 7;
                Array.Copy(Banks128![5], 0, data, 27, 16384);
                Array.Copy(Banks128[2], 0, data, 27 + 16384, 16384);
                Array.Copy(Banks128[paged], 0, data, 27 + 32768, 16384);
                int offset = 49183;
                for (int b = 0; b < 8; b++)
                {
                    if (b == 5 || b == 2 || b == paged) continue;
                    if (offset + 16384 > data.Length) break;
                    Array.Copy(Banks128[b], 0, data, offset, 16384);
                    offset += 16384;
                }
                return data;
            }
            case "z80v1":
            {
                var output = new List<byte>(original.AsSpan(0, 30).ToArray());
                output[12] = (byte)((original[12] == 255 ? 1 : original[12]) | 0x20);   // compressed
                output.AddRange(Compress(Memory.AsSpan(0x4000, 49152)));
                output.AddRange(new byte[] { 0, 0xED, 0xED, 0 });
                return output.ToArray();
            }
            case "z80v2":
            case "z80v3":
            {
                int extraLen = original[30] | (original[31] << 8);
                var output = new List<byte>(original.AsSpan(0, 32 + extraLen).ToArray());
                void Block(int page, ReadOnlySpan<byte> ram)
                {
                    var packed = Compress(ram);
                    bool raw = packed.Length >= 16384 && Format == "z80v3";
                    var body = raw ? ram.ToArray() : packed;
                    int len = raw ? 0xFFFF : body.Length;
                    output.Add((byte)len); output.Add((byte)(len >> 8)); output.Add((byte)page);
                    output.AddRange(body);
                }
                if (Banks128 != null)
                {
                    for (int b = 0; b < 8; b++)
                        if (Banks128[b] != null) Block(b + 3, Banks128[b]);
                }
                else
                {
                    Block(8, Memory.AsSpan(0x4000, 16384));
                    Block(4, Memory.AsSpan(0x8000, 16384));
                    Block(5, Memory.AsSpan(0xC000, 16384));
                }
                return output.ToArray();
            }
            case "raw48": return Memory.AsSpan(0x4000, 49152).ToArray();
            default: return (byte[])Memory.Clone();
        }
    }

    /// <summary>Z80 RLE: runs of five or more (two or more for ED) become ED ED count byte; a byte after a lone ED is never packed.</summary>
    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length);
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            int run = 1;
            while (i + run < data.Length && data[i + run] == b && run < 255) run++;
            if (run >= 5 || b == 0xED && run >= 2)
            {
                output.Add(0xED); output.Add(0xED); output.Add((byte)run); output.Add(b);
                i += run;
                continue;
            }
            output.Add(b);
            i++;
            if (b == 0xED && i < data.Length) output.Add(data[i++]);
        }
        return output.ToArray();
    }

    /// <summary>Z80 snapshot RLE: ED ED nn bb = nn copies of bb. v1 blocks end with 00 ED ED 00.</summary>
    private static byte[] Decompress(byte[] data, int start, int length, int expected, bool v1)
    {
        var output = new List<byte>(expected);
        int end = Math.Min(data.Length, start + length);
        int i = start;
        while (i < end)
        {
            if (v1 && i + 3 < end + 1 && i + 3 < data.Length && data[i] == 0 && data[i + 1] == 0xED && data[i + 2] == 0xED && data[i + 3] == 0)
                break;
            if (i + 3 < data.Length && data[i] == 0xED && data[i + 1] == 0xED)
            {
                int count = data[i + 2];
                byte value = data[i + 3];
                for (int k = 0; k < count; k++) output.Add(value);
                i += 4;
            }
            else
            {
                output.Add(data[i]);
                i++;
            }
        }
        return output.ToArray();
    }

    /// <summary>Find the first occurrence of a byte pattern (-1 in the pattern = wildcard).</summary>
    public int Find(ReadOnlySpan<int> pattern, int start = 0x4000, int end = 0x10000)
    {
        for (int a = start; a <= end - pattern.Length; a++)
        {
            bool ok = true;
            for (int k = 0; k < pattern.Length && ok; k++)
                if (pattern[k] >= 0 && Memory[a + k] != pattern[k]) ok = false;
            if (ok) return a;
        }
        return -1;
    }
}
