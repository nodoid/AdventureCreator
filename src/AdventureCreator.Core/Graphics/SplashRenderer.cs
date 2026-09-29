using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Graphics;

/// <summary>
/// Draws a splash / title image for a game: its intro picture (or first room picture), the title and the author,
/// on the game's background colour. Used for exported apps' native launch screens.
/// </summary>
public static class SplashRenderer
{
    public static RasterImage Render(Adventure adventure, int size = 480)
    {
        uint background = ParseColour(adventure.Settings.BackgroundColor, 0xFFFBFAF6);
        uint text = ParseColour(adventure.Settings.TextColor, 0xFF1F1F1F);
        uint accent = Luminance(background) > 0.5 ? 0xFFA0661B : 0xFFE0B050;
        var img = new RasterImage(size, size, background);

        // Picture in the upper part.
        var pic = adventure.FindPicture(adventure.IntroPictureId)
                  ?? adventure.FindPicture(adventure.FindRoom(adventure.StartRoomId)?.PictureId)
                  ?? adventure.Pictures.FirstOrDefault(p => !p.IsSubroutine);
        int y = size / 12;
        if (pic != null)
        {
            var rendered = PictureRenderer.Render(pic, adventure);
            int maxW = size * 5 / 6, maxH = size / 2;
            int scale = Math.Max(1, Math.Min(maxW / rendered.Width, maxH / rendered.Height));
            var scaled = scale > 1 ? rendered.Scale(scale) : rendered.Width > maxW || rendered.Height > maxH
                ? rendered.Resize(Math.Min(maxW, rendered.Width * maxH / rendered.Height), Math.Min(maxH, rendered.Height * maxW / rendered.Width))
                : rendered;
            Blit(img, scaled, (size - scaled.Width) / 2, y);
            y += scaled.Height + size / 24;
        }
        else y = size / 3;

        // Title (large, wrapped) and author (small).
        var titleScale = Math.Max(2, size / 120);
        foreach (var lineText in Wrap(adventure.Title, (size - 20) / (8 * titleScale)))
        {
            DrawText(img, lineText, (size - lineText.Length * 8 * titleScale) / 2, y, titleScale, accent);
            y += 10 * titleScale;
        }
        if (!string.IsNullOrWhiteSpace(adventure.Author))
        {
            var byScale = Math.Max(1, titleScale / 2);
            y += 4;
            var byLines = Wrap("by " + adventure.Author, (size - 20) / (8 * byScale)).ToList();
            if (byLines.Count > 2) byLines = new List<string> { byLines[0], byLines[1].Length > 2 ? byLines[1][..^2] + ".." : byLines[1] };
            foreach (var lineText in byLines)
            {
                DrawText(img, lineText, (size - lineText.Length * 8 * byScale) / 2, y, byScale, text);
                y += 10 * byScale;
            }
        }
        return img;
    }

    /// <summary>
    /// The splash as SVG (MAUI launch screens need vector images on Android). Each run of same-coloured pixels becomes a
    /// rectangle, so the retro pixels stay perfectly sharp at any size.
    /// </summary>
    public static string RenderSvg(Adventure adventure, int size = 240)
    {
        var img = Render(adventure, size);
        uint background = ParseColour(adventure.Settings.BackgroundColor, 0xFFFBFAF6);
        var byColour = new Dictionary<uint, System.Text.StringBuilder>();
        for (int y = 0; y < img.Height; y++)
        {
            int x = 0;
            while (x < img.Width)
            {
                uint c = img[x, y];
                int start = x;
                while (x < img.Width && img[x, y] == c) x++;
                if ((c & 0x00FFFFFF) == (background & 0x00FFFFFF)) continue;
                if (!byColour.TryGetValue(c, out var sb)) byColour[c] = sb = new System.Text.StringBuilder();
                sb.Append($"M{start} {y}h{x - start}v1h{start - x}z");
            }
        }
        var svg = new System.Text.StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\">");
        foreach (var (c, path) in byColour)
            svg.Append($"<path fill=\"#{c & 0xFFFFFF:X6}\" d=\"{path}\"/>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static IEnumerable<string> Wrap(string text, int maxChars)
    {
        maxChars = Math.Max(4, maxChars);
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word.Length > maxChars ? word[..maxChars] : word;
            if (line.Length == 0) line = w;
            else if (line.Length + 1 + w.Length <= maxChars) line += " " + w;
            else { yield return line; line = w; }
        }
        if (line.Length > 0) yield return line;
    }

    private static void DrawText(RasterImage img, string text, int x, int y, int scale, uint colour)
    {
        foreach (char ch in text)
        {
            var glyph = Font8x8.Glyph(ch);
            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 8; col++)
                    if (((glyph[row] >> col) & 1) != 0)
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int px = x + col * scale + dx, py = y + row * scale + dy;
                                if (img.InBounds(px, py)) img[px, py] = colour;
                            }
            x += 8 * scale;
        }
    }

    private static void Blit(RasterImage dst, RasterImage src, int x0, int y0)
    {
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                if (dst.InBounds(x0 + x, y0 + y)) dst[x0 + x, y0 + y] = src[x, y] | 0xFF000000;
    }

    public static uint ParseColour(string? hex, uint fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 6 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return 0xFF000000 | rgb;
        return fallback;
    }

    private static double Luminance(uint c) => (0.299 * ((c >> 16) & 0xFF) + 0.587 * ((c >> 8) & 0xFF) + 0.114 * (c & 0xFF)) / 255.0;
}
