namespace AdventureSystem.Core.Graphics;

/// <summary>
/// The area covered by one flood fill of a Smooth picture, traced on a fine grid by
/// <see cref="PictureRenderer.TraceFills"/>. Runs are (row, first cell, last cell), inclusive, in grid cells.
/// </summary>
public sealed record FillRegion(int GridWidth, int GridHeight, int Ink, int Paper,
    List<(int Y, int X0, int X1)> InkRuns, List<(int Y, int X0, int X1)> PaperRuns)
{
    /// <summary>How far (in grid cells) a region is grown so it tucks under the neighbouring smooth outlines.</summary>
    public const int Overlap = 2;

    internal static FillRegion FromMask(bool[] mask, int w, int h, byte[]? pattern, int cellSize, int ink, int paper)
    {
        // Grow the region slightly, so no hairline gap shows between a fill and anti-aliased edges next to it.
        // Separable: grow along rows, then along columns.
        var across = new bool[mask.Length];
        for (int y = 0; y < h; y++)
        {
            int row = y * w, last = -1;
            for (int x = 0; x < w; x++)
                if (mask[row + x]) last = x;
                else if (last >= 0 && x - last <= Overlap) across[row + x] = true;
            last = -1;
            for (int x = w - 1; x >= 0; x--)
            {
                if (mask[row + x]) { last = x; across[row + x] = true; }
                else if (last >= 0 && last - x <= Overlap) across[row + x] = true;
            }
        }
        var grown = new bool[mask.Length];
        for (int x = 0; x < w; x++)
        {
            int last = -1;
            for (int y = 0; y < h; y++)
                if (across[y * w + x]) { last = y; grown[y * w + x] = true; }
                else if (last >= 0 && y - last <= Overlap) grown[y * w + x] = true;
            last = -1;
            for (int y = h - 1; y >= 0; y--)
                if (across[y * w + x]) last = y;
                else if (last >= 0 && last - y <= Overlap) grown[y * w + x] = true;
        }

        var inkRuns = new List<(int, int, int)>();
        var paperRuns = new List<(int, int, int)>();
        for (int y = 0; y < h; y++)
        {
            int x = 0;
            while (x < w)
            {
                if (!grown[y * w + x]) { x++; continue; }
                bool on = pattern == null || PictureRenderer.PatternBit(pattern, x / cellSize, y / cellSize);
                int start = x;
                while (x < w && grown[y * w + x] && (pattern == null || PictureRenderer.PatternBit(pattern, x / cellSize, y / cellSize) == on)) x++;
                (on ? inkRuns : paperRuns).Add((y, start, x - 1));
            }
        }
        return new FillRegion(w, h, ink, paper, inkRuns, paperRuns);
    }
}
