using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Graphics;

/// <summary>
/// Software rasteriser for <see cref="Picture"/>s. Produces identical output on every platform, supports
/// full-colour drawing and authentic ZX Spectrum attribute rendering (for PAWS/Quill/Illustrator/GAC imports).
/// </summary>
public sealed class PictureRenderer
{
    private readonly Adventure? adventure;
    private readonly Picture picture;
    private readonly bool spectrum;
    private readonly RasterImage image;

    // Spectrum mode state
    private readonly bool[] bits;
    private readonly byte[] cellInk, cellPaper;
    private readonly bool[] cellBright;
    private readonly int cols, rows;

    private int ink, paper;
    private bool bright;
    private int depth;
    private RasterImage? cachedBackground;

    // Fill tracing for Smooth mode: the picture is rasterised on a grid 'supersample' times finer, with strokes as
    // wide as the smooth ones, and every flood fill's region is recorded.
    private readonly int supersample = 1;
    private readonly double strokeScale = 1;
    private List<FillRegion>? regions;

    private PictureRenderer(Picture picture, Adventure? adventure, int supersample = 1)
    {
        this.picture = picture;
        this.adventure = adventure;
        this.supersample = supersample;
        if (supersample > 1) strokeScale = supersample * Math.Max(0.25, picture.LineWidth);
        spectrum = picture.RenderMode == PictureRenderMode.SpectrumAttributes && supersample == 1;
        image = new RasterImage(picture.Width * supersample, picture.Height * supersample, Colour(picture.InitialPaper));
        ink = picture.InitialInk;
        paper = picture.InitialPaper;
        cols = (image.Width + 7) / 8;
        rows = (image.Height + 7) / 8;
        bits = spectrum ? new bool[picture.Width * picture.Height] : Array.Empty<bool>();
        cellInk = new byte[cols * rows];
        cellPaper = new byte[cols * rows];
        cellBright = new bool[cols * rows];
        Array.Fill(cellInk, (byte)(ink & 7));
        Array.Fill(cellPaper, (byte)(paper & 7));
    }

    /// <summary>Renders a picture. <paramref name="commandLimit"/> renders only the first N commands (editor preview/undo).</summary>
    public static RasterImage Render(Picture picture, Adventure? adventure = null, int? commandLimit = null)
    {
        var r = new PictureRenderer(picture, adventure);
        r.DrawBackground();
        var commands = commandLimit.HasValue ? picture.Commands.Take(commandLimit.Value) : picture.Commands;
        r.Run(commands, 0, 0, 8);
        return r.Compose();
    }

    /// <summary>
    /// Traces the flood fills (Fill and Shade commands, including those in sub-pictures) of a Smooth picture on a grid
    /// <paramref name="supersample"/> times finer than the picture, in the order they are drawn. Each region is a list
    /// of horizontal runs in grid cells, split into ink and paper (for patterns).
    /// </summary>
    public static IReadOnlyList<FillRegion> TraceFills(Picture picture, Adventure? adventure = null, int supersample = 4, int? commandLimit = null)
    {
        var r = new PictureRenderer(picture, adventure, Math.Max(1, supersample)) { regions = new List<FillRegion>() };
        r.DrawBackground();
        var commands = commandLimit.HasValue ? picture.Commands.Take(commandLimit.Value) : picture.Commands;
        r.Run(commands, supersample / 2, supersample / 2, 8 * supersample);
        return r.regions;
    }

    /// <summary>Convenience: render straight to PNG bytes, optionally scaled up for display.</summary>
    public static byte[] RenderPng(Picture picture, Adventure? adventure = null, int scale = 1, int? commandLimit = null) =>
        Render(picture, adventure, commandLimit).Scale(scale).ToPng();

    private uint Colour(int index)
    {
        var pal = picture.Palette.Count > 0 ? picture.Palette : Palettes.Spectrum.ToList();
        if (index < 0) return 0x00000000;
        return pal[index % pal.Count];
    }

    private void DrawBackground()
    {
        if (spectrum || string.IsNullOrEmpty(picture.BitmapAsset) || adventure == null) return;
        var bg = LoadAsset(picture.BitmapAsset);
        if (bg == null) return;
        var scaled = bg.Width == image.Width && bg.Height == image.Height ? bg : bg.Resize(image.Width, image.Height);
        Array.Copy(scaled.Pixels, image.Pixels, image.Pixels.Length);
    }

    private RasterImage? LoadAsset(string name)
    {
        if (adventure == null || !adventure.Assets.TryGetValue(name, out var data) || !RasterImage.IsPng(data)) return null;
        try { return cachedBackground = RasterImage.DecodePng(data); }
        catch { return null; }
    }

    // ------------------------------------------------------------------ command interpreter

    private void Run(IEnumerable<DrawCommand> commands, int ox, int oy, int scale)
    {
        if (++depth > 16) { depth--; return; }
        // In fill-tracing mode coordinates point at cell centres; areas (text, filled boxes) start at cell corners.
        int ox0 = supersample / 2;
        int Tx(int x) => ox + x * scale / 8;
        int Ty(int y) => oy + y * scale / 8;

        foreach (var c in commands)
        {
            switch (c.Op)
            {
                case DrawOp.Clear:
                    paper = c.Color;
                    ClearAll();
                    break;
                case DrawOp.SetInk: ink = c.Color; break;
                case DrawOp.SetPaper: paper = c.Color; break;
                case DrawOp.SetBright: bright = c.X != 0; break;
                case DrawOp.Plot:
                    if (supersample > 1) Line(Tx(c.X), Ty(c.Y), Tx(c.X), Ty(c.Y), c);
                    else Plot(Tx(c.X), Ty(c.Y), c);
                    break;
                case DrawOp.Line: Line(Tx(c.X), Ty(c.Y), Tx(c.X2), Ty(c.Y2), c); break;
                case DrawOp.Rectangle:
                {
                    int x1 = Tx(c.X), y1 = Ty(c.Y), x2 = Tx(c.X2), y2 = Ty(c.Y2);
                    Line(x1, y1, x2, y1, c); Line(x2, y1, x2, y2, c); Line(x2, y2, x1, y2, c); Line(x1, y2, x1, y1, c);
                    break;
                }
                case DrawOp.FilledRectangle:
                {
                    int x1 = Math.Min(Tx(c.X), Tx(c.X2)), x2 = Math.Max(Tx(c.X), Tx(c.X2));
                    int y1 = Math.Min(Ty(c.Y), Ty(c.Y2)), y2 = Math.Max(Ty(c.Y), Ty(c.Y2));
                    // When tracing fills, cover whole pixels as the smooth renderer does.
                    if (supersample > 1) { x1 -= ox0; y1 -= ox0; x2 += scale / 8 - ox0 - 1; y2 += scale / 8 - ox0 - 1; }
                    for (int y = y1; y <= y2; y++) for (int x = x1; x <= x2; x++) Set(x, y, c);
                    break;
                }
                case DrawOp.Ellipse:
                case DrawOp.FilledEllipse:
                    Ellipse(Tx(c.X), Ty(c.Y), Tx(c.X2), Ty(c.Y2), c.Op == DrawOp.FilledEllipse, c);
                    break;
                case DrawOp.Polygon:
                case DrawOp.FilledPolygon:
                case DrawOp.Freehand:
                {
                    var pts = new List<(int X, int Y)>();
                    for (int i = 0; i + 1 < c.Points.Count; i += 2) pts.Add((Tx(c.Points[i]), Ty(c.Points[i + 1])));
                    if (pts.Count == 0) break;
                    if (c.Op == DrawOp.FilledPolygon) FillPolygon(pts, c);
                    int brush = c.Op == DrawOp.Freehand ? Math.Max(1, c.Scale) : 1;
                    for (int i = 0; i + 1 < pts.Count; i++) Line(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, c, brush);
                    if (c.Op != DrawOp.Freehand && pts.Count > 2) Line(pts[^1].X, pts[^1].Y, pts[0].X, pts[0].Y, c);
                    if (pts.Count == 1) Plot(pts[0].X, pts[0].Y, c);
                    break;
                }
                case DrawOp.Fill: FloodFill(Tx(c.X), Ty(c.Y), null, scale / 8); break;
                case DrawOp.Shade: FloodFill(Tx(c.X), Ty(c.Y), c.Pattern ?? new byte[] { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 }, scale / 8); break;
                case DrawOp.AttributeBlock: Block(Tx(c.X), Ty(c.Y), Tx(c.X2), Ty(c.Y2), c.Color, c.Color2); break;
                case DrawOp.Text: Text(Tx(c.X) - ox0, Ty(c.Y) - ox0, c.Text ?? "", Math.Max(1, (c.Scale == 8 ? 1 : c.Scale) * scale / 8), c); break;
                case DrawOp.Call:
                    if (adventure?.FindPicture(c.SubPictureId) is { } sub && sub != picture)
                        Run(sub.Commands, Tx(c.X), Ty(c.Y), Math.Max(1, c.Scale) * scale / 8);
                    break;
                case DrawOp.Image:
                    if (!spectrum && c.Text != null && LoadAsset(c.Text) is { } bmp)
                    {
                        int w = c.X2 > 0 ? c.X2 * scale / 8 : bmp.Width, h = c.Y2 > 0 ? c.Y2 * scale / 8 : bmp.Height;
                        var s = bmp.Width == w && bmp.Height == h ? bmp : bmp.Resize(Math.Max(1, w), Math.Max(1, h));
                        int x0 = Tx(c.X), y0 = Ty(c.Y);
                        for (int y = 0; y < s.Height; y++)
                            for (int x = 0; x < s.Width; x++)
                            {
                                uint px = s.Pixels[y * s.Width + x];
                                if ((px >> 24) < 128 || !image.InBounds(x0 + x, y0 + y)) continue;
                                image[x0 + x, y0 + y] = px;
                            }
                    }
                    break;
            }
        }
        depth--;
    }

    // ------------------------------------------------------------------ primitives

    private void ClearAll()
    {
        if (spectrum)
        {
            Array.Clear(bits);
            Array.Fill(cellPaper, (byte)(paper & 7));
            Array.Fill(cellInk, (byte)(ink & 7));
            Array.Fill(cellBright, bright);
        }
        else Array.Fill(image.Pixels, Colour(paper));
    }

    private void Set(int x, int y, DrawCommand c)
    {
        if (!image.InBounds(x, y)) return;
        if (spectrum)
        {
            int i = y * image.Width + x;
            if (c.Xor) bits[i] = !bits[i];
            else bits[i] = !c.Inverse;
            int cell = (y / 8) * cols + x / 8;
            if (ink < 8) cellInk[cell] = (byte)(ink & 7);
            cellBright[cell] = bright;
            return;
        }
        uint col = c.Inverse ? Colour(paper) : Colour(ink);
        if (c.Xor) col = image[x, y] ^ (col & 0x00FFFFFF);
        image[x, y] = col;
    }

    private void Plot(int x, int y, DrawCommand c) => Set(x, y, c);

    private void Line(int x0, int y0, int x1, int y1, DrawCommand c, int brush = 1)
    {
        if (supersample > 1) brush = Math.Max(1, (int)Math.Round(brush * strokeScale));
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int guard = 0;
        while (guard++ < 100000)
        {
            if (brush <= 1) Set(x0, y0, c);
            else
                for (int by = -brush / 2; by <= brush / 2; by++)
                    for (int bx = -brush / 2; bx <= brush / 2; bx++)
                        if (bx * bx + by * by <= brush * brush / 4 + 1) Set(x0 + bx, y0 + by, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void Ellipse(int x1, int y1, int x2, int y2, bool filled, DrawCommand c)
    {
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);
        double cx = (x1 + x2) / 2.0, cy = (y1 + y2) / 2.0;
        double rx = Math.Max(0.5, (x2 - x1) / 2.0), ry = Math.Max(0.5, (y2 - y1) / 2.0);
        if (filled)
        {
            for (int y = y1; y <= y2; y++)
            {
                double dy = (y - cy) / ry;
                if (Math.Abs(dy) > 1) continue;
                double half = rx * Math.Sqrt(1 - dy * dy);
                for (int x = (int)Math.Round(cx - half); x <= (int)Math.Round(cx + half); x++) Set(x, y, c);
            }
            return;
        }
        int steps = (int)Math.Max(16, (rx + ry) * 4);
        int px = (int)Math.Round(cx + rx), py = (int)Math.Round(cy);
        for (int i = 1; i <= steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            int nx = (int)Math.Round(cx + rx * Math.Cos(t)), ny = (int)Math.Round(cy + ry * Math.Sin(t));
            Line(px, py, nx, ny, c);
            px = nx; py = ny;
        }
    }

    private void FillPolygon(List<(int X, int Y)> pts, DrawCommand c)
    {
        int minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        var xs = new List<int>();
        for (int y = minY; y <= maxY; y++)
        {
            xs.Clear();
            double sy = y + 0.5;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                if ((a.Y <= sy && b.Y > sy) || (b.Y <= sy && a.Y > sy))
                    xs.Add((int)Math.Round(a.X + (sy - a.Y) * (b.X - a.X) / (double)(b.Y - a.Y)));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = xs[k]; x <= xs[k + 1]; x++) Set(x, y, c);
        }
    }

    /// <summary>
    /// Flood fill. Full colour: replaces the contiguous region of the seed colour with ink (or pattern ink/paper).
    /// Spectrum: fills contiguous "off" pixels, setting them on (or per pattern), like PAWS FILL / SHADE.
    /// </summary>
    private void FloodFill(int sx, int sy, byte[]? pattern, int cellSize = 1)
    {
        if (!image.InBounds(sx, sy)) { regions?.Add(new FillRegion(image.Width, image.Height, ink, paper, new(), new())); return; }
        int w = image.Width, h = image.Height;
        var visited = new bool[w * h];
        uint target = spectrum ? 0 : image[sx, sy];
        uint inkColour = Colour(ink), paperColour = Colour(paper);
        if (spectrum && bits[sy * w + sx]) return;
        if (!spectrum && pattern == null && target == inkColour && regions == null) return;

        bool Inside(int x, int y)
        {
            int i = y * w + x;
            if (visited[i]) return false;
            return spectrum ? !bits[i] : image.Pixels[i] == target;
        }

        var stack = new Stack<(int X, int Y)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (!Inside(x, y)) continue;
            int left = x, right = x;
            while (left > 0 && Inside(left - 1, y)) left--;
            while (right < w - 1 && Inside(right + 1, y)) right++;
            for (int xi = left; xi <= right; xi++)
            {
                int i = y * w + xi;
                visited[i] = true;
                bool on = pattern == null || PatternBit(pattern, xi / cellSize, y / cellSize);
                if (spectrum)
                {
                    if (on) bits[i] = true;
                    int cell = (y / 8) * cols + xi / 8;
                    if (ink < 8) cellInk[cell] = (byte)(ink & 7);
                    cellBright[cell] = bright;
                }
                else image.Pixels[i] = on ? inkColour : paperColour;
                if (y > 0 && Inside(xi, y - 1)) stack.Push((xi, y - 1));
                if (y < h - 1 && Inside(xi, y + 1)) stack.Push((xi, y + 1));
            }
        }
        if (regions != null) regions.Add(FillRegion.FromMask(visited, w, h, pattern, cellSize, ink, paper));
    }

    internal static bool PatternBit(byte[] pattern, int px, int py) => ((pattern[py & 7] >> (7 - (px & 7))) & 1) != 0;

    private void Block(int x1, int y1, int x2, int y2, int inkIndex, int paperIndex)
    {
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);
        if (!spectrum)
        {
            uint col = Colour(paperIndex >= 0 ? paperIndex : inkIndex);
            for (int y = Math.Max(0, y1); y <= Math.Min(image.Height - 1, y2); y++)
                for (int x = Math.Max(0, x1); x <= Math.Min(image.Width - 1, x2); x++)
                    image[x, y] = col;
            return;
        }
        for (int row = Math.Max(0, y1 / 8); row <= Math.Min(rows - 1, y2 / 8); row++)
            for (int col = Math.Max(0, x1 / 8); col <= Math.Min(cols - 1, x2 / 8); col++)
            {
                int cell = row * cols + col;
                // Indices 8-15 are the bright colours; 0-7 use the current BRIGHT setting.
                if (inkIndex >= 0 && inkIndex < 16) cellInk[cell] = (byte)(inkIndex & 7);
                if (paperIndex >= 0 && paperIndex < 16) cellPaper[cell] = (byte)(paperIndex & 7);
                cellBright[cell] = bright || inkIndex >= 8 && inkIndex < 16 || paperIndex >= 8 && paperIndex < 16;
            }
    }

    private void Text(int x, int y, string text, int size, DrawCommand c)
    {
        int cx = x;
        foreach (char ch in text)
        {
            if (ch == '\n') { cx = x; y += 8 * size; continue; }
            var glyph = Font8x8.Glyph(ch);
            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 8; col++)
                {
                    if (((glyph[row] >> col) & 1) == 0) continue;
                    for (int dy = 0; dy < size; dy++)
                        for (int dx = 0; dx < size; dx++)
                            Set(cx + col * size + dx, y + row * size + dy, c);
                }
            cx += 8 * size;
        }
    }

    private RasterImage Compose()
    {
        if (!spectrum) return image;
        int w = image.Width;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < w; x++)
            {
                int cell = (y / 8) * cols + x / 8;
                int b = cellBright[cell] ? 8 : 0;
                image.Pixels[y * w + x] = Colour((bits[y * w + x] ? cellInk[cell] : cellPaper[cell]) + b);
            }
        return image;
    }
}
