using System.IO.Compression;

namespace AdventureCreator.Core.Graphics;

/// <summary>A simple 32-bit ARGB bitmap with PNG encoding/decoding (no platform dependencies).</summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public RasterImage(int width, int height, uint fill = 0xFF000000)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new uint[Width * Height];
        Array.Fill(Pixels, fill);
    }

    public uint this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Returns a nearest-neighbour scaled copy (used for crisp retro pixels).</summary>
    public RasterImage Scale(int factor)
    {
        if (factor <= 1) return this;
        var r = new RasterImage(Width * factor, Height * factor);
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
                r.Pixels[y * r.Width + x] = Pixels[(y / factor) * Width + x / factor];
        return r;
    }

    public RasterImage Resize(int width, int height)
    {
        var r = new RasterImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                r.Pixels[y * width + x] = Pixels[(int)((long)y * Height / height) * Width + (int)((long)x * Width / width)];
        return r;
    }

    // ------------------------------------------------------------------ PNG encoding

    public byte[] ToPng()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)Width);
        WriteBE(ihdr, 4, (uint)Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        WriteChunk(ms, "IHDR", ihdr);

        var raw = new byte[Height * (Width * 4 + 1)];
        int p = 0;
        for (int y = 0; y < Height; y++)
        {
            raw[p++] = 0;
            for (int x = 0; x < Width; x++)
            {
                uint c = Pixels[y * Width + x];
                raw[p++] = (byte)(c >> 16);
                raw[p++] = (byte)(c >> 8);
                raw[p++] = (byte)c;
                raw[p++] = (byte)(c >> 24);
            }
        }
        using (var zms = new MemoryStream())
        {
            using (var z = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            WriteChunk(ms, "IDAT", zms.ToArray());
        }
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, 0xFFFFFFFF);
        crc = Crc32(data, crc) ^ 0xFFFFFFFF;
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, crc);
        s.Write(crcBytes);
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static uint ReadBE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    private static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data, uint crc)
    {
        foreach (var b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    // ------------------------------------------------------------------ PNG decoding

    public static bool IsPng(byte[] data) => data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    /// <summary>Decodes non-interlaced PNGs with 8-bit channels (grey, RGB, palette, grey+alpha, RGBA) and 1/2/4-bit palettes/greys.</summary>
    public static RasterImage DecodePng(byte[] data)
    {
        if (!IsPng(data)) throw new InvalidDataException("Not a PNG image.");
        int pos = 8, width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        uint[]? palette = null;
        var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = (int)ReadBE(data, pos);
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int start = pos + 8;
            switch (type)
            {
                case "IHDR":
                    width = (int)ReadBE(data, start);
                    height = (int)ReadBE(data, start + 4);
                    depth = data[start + 8];
                    colorType = data[start + 9];
                    interlace = data[start + 12];
                    break;
                case "PLTE":
                    palette = new uint[len / 3];
                    for (int i = 0; i < palette.Length; i++)
                        palette[i] = 0xFF000000 | (uint)data[start + i * 3] << 16 | (uint)data[start + i * 3 + 1] << 8 | data[start + i * 3 + 2];
                    break;
                case "tRNS":
                    if (palette != null)
                        for (int i = 0; i < len && i < palette.Length; i++)
                            palette[i] = (palette[i] & 0x00FFFFFF) | (uint)data[start + i] << 24;
                    break;
                case "IDAT":
                    idat.Write(data, start, len);
                    break;
            }
            pos = start + len + 4;
            if (type == "IEND") break;
        }
        if (interlace != 0) throw new NotSupportedException("Interlaced PNGs are not supported; please re-save the image without interlacing.");
        if (depth == 16) throw new NotSupportedException("16-bit PNGs are not supported.");

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("PNG colour type " + colorType) };
        int bitsPerPixel = channels * depth;
        int stride = (width * bitsPerPixel + 7) / 8;
        int bpp = Math.Max(1, bitsPerPixel / 8);

        idat.Position = 0;
        byte[] raw;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            z.CopyTo(outMs);
            raw = outMs.ToArray();
        }

        var img = new RasterImage(width, height);
        var prev = new byte[stride];
        var cur = new byte[stride];
        int rp = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = raw[rp++];
            Array.Copy(raw, rp, cur, 0, stride);
            rp += stride;
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0;
                int b = prev[i];
                int c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = filter switch
                {
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + ((a + b) >> 1)),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => cur[i],
                };
            }
            for (int x = 0; x < width; x++)
            {
                uint argb;
                if (depth < 8)
                {
                    int bitIndex = x * depth;
                    int v = (cur[bitIndex / 8] >> (8 - depth - bitIndex % 8)) & ((1 << depth) - 1);
                    if (colorType == 3) argb = palette != null && v < palette.Length ? palette[v] : 0xFF000000;
                    else { int g = v * 255 / ((1 << depth) - 1); argb = 0xFF000000 | (uint)(g << 16 | g << 8 | g); }
                }
                else
                {
                    int o = x * channels;
                    argb = colorType switch
                    {
                        0 => 0xFF000000 | (uint)(cur[o] << 16 | cur[o] << 8 | cur[o]),
                        2 => 0xFF000000 | (uint)(cur[o] << 16 | cur[o + 1] << 8 | cur[o + 2]),
                        3 => palette != null && cur[o] < palette.Length ? palette[cur[o]] : 0xFF000000,
                        4 => (uint)(cur[o + 1] << 24 | cur[o] << 16 | cur[o] << 8 | cur[o]),
                        _ => (uint)(cur[o + 3] << 24 | cur[o] << 16 | cur[o + 1] << 8 | cur[o + 2]),
                    };
                }
                img.Pixels[y * width + x] = argb;
            }
            (prev, cur) = (cur, prev);
        }
        return img;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
