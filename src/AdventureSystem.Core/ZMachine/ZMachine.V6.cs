namespace AdventureSystem.Core.ZMachine;

/// <summary>Version 6: windows with properties, user stacks, picture data and formatted printing.</summary>
public sealed partial class ZMachine
{
    private const int ScreenUnitsWide = 640, ScreenUnitsHigh = 400, FontWide = 8, FontHigh = 16, WindowProps = 18;
    private readonly short[,] wprop = new short[8, WindowProps];
    private int window6;
    private readonly Dictionary<int, char[][]> grids6 = new();
    private readonly Dictionary<int, System.Text.StringBuilder> text6 = new();
    /// <summary>The window input is read in: its text is the main text; the others make the status bar.</summary>
    private int mainWindow6;

    private void ResetWindows6()
    {
        Array.Clear(wprop);
        for (int w = 0; w < 8; w++)
        {
            wprop[w, 0] = 1; wprop[w, 1] = 1; wprop[w, 4] = 1; wprop[w, 5] = 1;
            wprop[w, 12] = 1;
            wprop[w, 13] = (short)((FontHigh << 8) | FontWide);
        }
        wprop[0, 2] = ScreenUnitsHigh;
        wprop[0, 3] = ScreenUnitsWide;
        wprop[0, 14] = 0b1010;   // wrapping, buffered
        window6 = 0;
        mainWindow6 = 0;
        grids6.Clear();
        text6.Clear();
    }

    private int WindowIndex(int w) => w == -3 ? window6 : Math.Clamp(w, 0, 7);

    private void SetWindowProps(int w, params (int Prop, int Value)[] values)
    {
        int i = WindowIndex(w);
        foreach (var (p, v) in values) wprop[i, p] = (short)v;
    }

    private void WindowStyle(int w, int flags, int operation)
    {
        int i = WindowIndex(w);
        int old = wprop[i, 14];
        wprop[i, 14] = (short)(operation switch { 1 => old | flags, 2 => old & ~flags, 3 => old ^ flags, _ => flags });
    }

    private void SetWindow6(int w)
    {
        window6 = WindowIndex(w);
        window = window6 == 0 ? 0 : 1;
    }

    private void SetCursor6(int line, int column, int w)
    {
        if (line < 0) return;   // -1 hides and -2 shows the cursor
        int i = WindowIndex(w);
        wprop[i, 4] = (short)line;
        wprop[i, 5] = (short)column;
    }

    /// <summary>Text in V6 windows other than the main one: kept as a grid (for the status bar).</summary>
    private void Print6(string text)
    {
        int w = window6;
        if (!text6.TryGetValue(w, out var buffer)) text6[w] = buffer = new System.Text.StringBuilder();
        buffer.Append(text);
        int rows = Math.Max(1, wprop[w, 2] / FontHigh);
        if (!grids6.TryGetValue(w, out var grid) || grid.Length != rows)
        {
            grid = new char[rows][];
            for (int r = 0; r < rows; r++) grid[r] = NewRow();
            grids6[w] = grid;
        }
        foreach (char c in text)
        {
            int row = (wprop[w, 4] - 1) / FontHigh, col = (wprop[w, 5] - 1) / FontWide;
            if (c == '\n') { wprop[w, 4] += FontHigh; wprop[w, 5] = 1; continue; }
            if (row >= 0 && row < grid.Length && col >= 0 && col < ScreenWidth) grid[row][col] = c;
            wprop[w, 5] += FontWide;
        }
    }

    private void Erase6(int w)
    {
        if (w < 0) { grids6.Clear(); return; }
        grids6.Remove(w);
    }

    /// <summary>At input: the current window is the main one; its text goes to the main output.</summary>
    private void Flush6()
    {
        mainWindow6 = window6;
        if (text6.TryGetValue(mainWindow6, out var main)) lower.Append(main);
        text6.Clear();
    }

    private string Status6()
    {
        foreach (var w in grids6.Keys.Where(k => k != mainWindow6).Order())
        {
            foreach (var row in grids6[w])
            {
                var text = new string(row).Trim();
                if (text.Length > 0) return System.Text.RegularExpressions.Regex.Replace(text, " {3,}", "   ");
            }
        }
        return "";
    }

    private void PictureData(int picture, int array)
    {
        if (picture == 0)
        {
            WW(array, pictureSizes.Count == 0 ? 0 : pictureSizes.Keys.Max());
            WW(array + 2, 0);
            Branch(pictureSizes.Count > 0);
            return;
        }
        if (pictureSizes.TryGetValue(picture, out var size))
        {
            WW(array, size.Height);
            WW(array + 2, size.Width);
            Branch(true);
        }
        else Branch(false);
    }

    // User stacks: the first word is the number of free slots; values sit above it.
    private bool UserStackPush(int stackAddr, int value)
    {
        int free = RW(stackAddr);
        if (free == 0) return false;
        WW(stackAddr + 2 * free, value);
        WW(stackAddr, free - 1);
        return true;
    }

    private int UserStackPop(int stackAddr)
    {
        int free = RW(stackAddr) + 1;
        WW(stackAddr, free);
        return RW(stackAddr + 2 * free);
    }

    private void UserStackFree(int stackAddr, int items)
    {
        if (stackAddr < 0) { for (int i = 0; i < items; i++) ReadVar(0); return; }
        WW(stackAddr, RW(stackAddr) + items);
    }

    private void PrintForm(int table)
    {
        for (int p = table; ;)
        {
            int length = RW(p);
            if (length == 0) break;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < length; i++) sb.Append(ZsciiToChar(RB(p + 2 + i)));
            Print(sb.ToString() + "\n");
            p += 2 + length;
        }
    }
}
