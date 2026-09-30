using System.Text;

namespace AdventureCreator.Core.ZMachine;

public sealed partial class ZMachine
{
    // ================================================================= output

    private void PrintZscii(int z)
    {
        if (z == 0) return;   // print_char 0 does nothing
        if (stream3.Count > 0) { Stream3Write(z); return; }
        Print(ZsciiToChar(z).ToString());
    }

    private void Print(string text)
    {
        if (text.Length == 0) return;
        if (stream3.Count > 0)
        {
            foreach (char c in text) Stream3Write(CharToZscii(c));
            return;
        }
        if (!screenOutput) return;
        if (Version == 6)
        {
            Print6(text);
            return;
        }
        if (window == 1 && upperLines > 0)
        {
            foreach (char c in text)
            {
                if (c == '\n') { cursorRow++; cursorCol = 0; continue; }
                if (cursorRow < upperGrid.Length && cursorCol < ScreenWidth) upperGrid[cursorRow][cursorCol] = c;
                cursorCol++;
            }
            return;
        }
        if (window == 1) return;   // the upper window has no lines: nothing is visible
        lower.Append(text);
    }

    private void Stream3Write(int zscii)
    {
        var (addr, count) = stream3[^1];
        if (zscii == '\n') zscii = 13;
        mem[addr + 2 + count] = (byte)zscii;
        stream3[^1] = (addr, count + 1);
    }

    private void OutputStream(int n, int table)
    {
        switch (n)
        {
            case 1: screenOutput = true; break;
            case -1: screenOutput = false; break;
            case 3:
                if (stream3.Count >= 16) throw new ZException("Too many nested output stream 3 tables.");
                stream3.Add((table, 0));
                break;
            case -3:
                if (stream3.Count == 0) break;
                var (addr, count) = stream3[^1];
                stream3.RemoveAt(stream3.Count - 1);
                WW(addr, count);
                break;
        }
    }

    private void SplitWindow(int lines)
    {
        if (Version == 6)
        {
            wprop[1, 2] = (short)lines;
            wprop[1, 3] = ScreenUnitsWide;
            wprop[0, 0] = (short)(lines + 1);
            wprop[0, 2] = (short)(ScreenUnitsHigh - lines);
            return;
        }
        upperLines = Math.Clamp(lines, 0, 50);
        var grid = new char[upperLines][];
        for (int i = 0; i < upperLines; i++)
        {
            grid[i] = i < upperGrid.Length ? upperGrid[i] : NewRow();
        }
        upperGrid = grid;
        if (Version == 3) for (int i = 0; i < upperLines; i++) upperGrid[i] = NewRow();
        if (cursorRow >= upperLines) { cursorRow = 0; cursorCol = 0; }
    }

    private static char[] NewRow() => Enumerable.Repeat(' ', ScreenWidth).ToArray();

    private void EraseWindow(int w)
    {
        if (Version == 6)
        {
            Erase6(w);
            if (w is 0 or -1 or -2) ClearRequested = true;
            return;
        }
        if (w == -1) { SplitWindow(0); window = 0; ClearRequested = true; }
        else if (w == -2) { for (int i = 0; i < upperGrid.Length; i++) upperGrid[i] = NewRow(); ClearRequested = true; }
        else if (w == 1) for (int i = 0; i < upperGrid.Length; i++) upperGrid[i] = NewRow();
        else if (w == 0) ClearRequested = true;
    }

    private void PrintTable(int text, int width, int height, int skip)
    {
        for (int row = 0; row < height; row++)
        {
            if (row > 0) Print("\n");
            var sb = new StringBuilder();
            for (int col = 0; col < width; col++) sb.Append(ZsciiToChar(RB(text + row * (width + skip) + col)));
            Print(sb.ToString());
        }
    }

    // ================================================================= status line

    private void UpdateStatus()
    {
        if (Version <= 3)
        {
            int loc = ReadGlobal(0);
            bool time = Version == 3 && (RB(1) & 0x02) != 0;
            Status3 = (loc == 0 ? "" : ObjectName(loc), S(ReadGlobal(1)), S(ReadGlobal(2)), time);
            var (name, a, b, isTime) = Status3.Value;
            StatusLine = isTime ? $"{name}    Time: {a}:{b:00}" : $"{name}    Score: {a}   Moves: {b}";
        }
        else if (Version == 6) { Flush6(); StatusLine = Status6(); }
        else if (upperGrid.Length > 0)
        {
            // Collapse the first row's layout spaces so it fits a status bar.
            var row = new string(upperGrid[0]).Trim();
            StatusLine = System.Text.RegularExpressions.Regex.Replace(row, " {3,}", "   ");
        }
    }

    private int ReadGlobal(int n) => RW(Globals + 2 * n);

    // ================================================================= input

    private void CompleteRead(string input)
    {
        var r = pendingRead!;
        pendingRead = null;
        if (r.Char)
        {
            WriteVar(r.StoreVar, input.Length == 0 ? 13 : CharToZscii(input[0]));
            return;
        }
        string text = input.ToLowerInvariant();
        int max = RB(r.TextBuffer);
        if (Version <= 4)
        {
            // Characters from byte 1, zero terminated; at most max-1 characters.
            int limit = Math.Max(0, max - 1);
            if (text.Length > limit) text = text[..limit];
            for (int i = 0; i < text.Length; i++) mem[r.TextBuffer + 1 + i] = (byte)CharToZscii(text[i]);
            mem[r.TextBuffer + 1 + text.Length] = 0;
        }
        else
        {
            // Count in byte 1, characters from byte 2 (appended to any preloaded input).
            int existing = RB(r.TextBuffer + 1);
            if (existing + text.Length > max) text = text[..Math.Max(0, max - existing)];
            for (int i = 0; i < text.Length; i++) mem[r.TextBuffer + 2 + existing + i] = (byte)CharToZscii(text[i]);
            mem[r.TextBuffer + 1] = (byte)(existing + text.Length);
        }
        if (r.ParseBuffer != 0) Tokenise(r.TextBuffer, r.ParseBuffer, 0, false);
        if (Version >= 5) WriteVar(r.StoreVar, 13);
    }

    private void Tokenise(int textBuffer, int parseBuffer, int dictionary, bool skipUnknown)
    {
        // The text as stored.
        string text;
        int offset;
        if (Version <= 4)
        {
            var sb = new StringBuilder();
            for (int i = textBuffer + 1; RB(i) != 0 && i < mem.Length; i++) sb.Append((char)RB(i));
            text = sb.ToString();
            offset = 1;
        }
        else
        {
            int n = RB(textBuffer + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append((char)RB(textBuffer + 2 + i));
            text = sb.ToString();
            offset = 2;
        }

        var dict = Dictionary(dictionary == 0 ? RW(0x08) : dictionary);
        var words = new List<(string Word, int Position)>();
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            char c = i < text.Length ? text[i] : ' ';
            bool separator = dict.Separators.Contains(c);
            if (c == ' ' || separator)
            {
                if (start >= 0) { words.Add((text[start..i], start)); start = -1; }
                if (separator) words.Add((c.ToString(), i));
            }
            else if (start < 0) start = i;
        }

        int maxWords = RB(parseBuffer);
        int count = Math.Min(words.Count, maxWords);
        mem[parseBuffer + 1] = (byte)count;
        for (int k = 0; k < count; k++)
        {
            var (word, position) = words[k];
            int entry = parseBuffer + 2 + 4 * k;
            int address = dict.Lookup(Encode(word));
            if (address == 0 && skipUnknown) continue;
            WW(entry, address);
            mem[entry + 2] = (byte)word.Length;
            mem[entry + 3] = (byte)(position + offset);
        }
    }

    private sealed class ZDictionary
    {
        public HashSet<char> Separators = new();
        public Dictionary<string, int> Entries = new();
        public int Lookup(byte[] encoded) => Entries.TryGetValue(Convert.ToHexString(encoded), out var a) ? a : 0;
    }

    private readonly Dictionary<int, ZDictionary> dictionaries = new();

    private ZDictionary Dictionary(int addr)
    {
        if (dictionaries.TryGetValue(addr, out var d)) return d;
        d = new ZDictionary();
        int n = RB(addr);
        for (int i = 0; i < n; i++) d.Separators.Add(ZsciiToChar(RB(addr + 1 + i)));
        int p = addr + 1 + n;
        int entryLength = RB(p);
        int count = Math.Abs(S(RW(p + 1)));
        int first = p + 3;
        int keyLength = Version <= 3 ? 4 : 6;
        for (int i = 0; i < count; i++)
        {
            int e = first + i * entryLength;
            var key = Convert.ToHexString(mem, e, keyLength);
            d.Entries.TryAdd(key, e);
        }
        // Only the story's own (static) dictionary can be cached; user dictionaries may change.
        if (addr >= staticBase) dictionaries[addr] = d;
        return d;
    }

    private void EncodeText(int text, int length, int from, int coded)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < length; i++) sb.Append(ZsciiToChar(RB(text + from + i)));
        var bytes = Encode(sb.ToString());
        for (int i = 0; i < bytes.Length; i++) mem[coded + i] = bytes[i];
    }

    // ================================================================= tables

    private void ScanTable(int x, int table, int length, int form)
    {
        int size = form & 0x7F;
        bool words = (form & 0x80) != 0;
        for (int i = 0; i < length; i++)
        {
            int a = table + i * size;
            int v = words ? RW(a) : RB(a);
            if (v == x) { Store(a); Branch(true); return; }
        }
        Store(0);
        Branch(false);
    }

    private void CopyTable(int first, int second, int size)
    {
        if (second == 0)
        {
            for (int i = 0; i < Math.Abs(size); i++) mem[first + i] = 0;
            return;
        }
        int n = Math.Abs(size);
        if (size > 0 && second > first && second < first + n)
            for (int i = n - 1; i >= 0; i--) mem[second + i] = mem[first + i];   // overlap: copy backwards
        else
            for (int i = 0; i < n; i++) mem[second + i] = mem[first + i];
    }

    // ================================================================= snapshots (undo and saved games)

    private sealed class Snapshot
    {
        public byte[] Dynamic = Array.Empty<byte>();
        public ushort[] Stack = Array.Empty<ushort>();
        public List<Frame> Frames = new();
        public int Pc;
        public int StoreVar;
        public PendingRead? Pending;
    }

    private Snapshot TakeSnapshot(int storeVar) => new()
    {
        Dynamic = mem[..staticBase],
        Stack = stack.ToArray(),
        Frames = frames.Select(f => new Frame { ReturnPc = f.ReturnPc, StoreVar = f.StoreVar, Locals = f.Locals.ToArray(), ArgCount = f.ArgCount, StackBase = f.StackBase }).ToList(),
        Pc = pc,
        StoreVar = storeVar,
        Pending = pendingRead,
    };

    private void ApplySnapshot(Snapshot s)
    {
        int flags2 = RW(0x10);
        Array.Copy(s.Dynamic, mem, s.Dynamic.Length);
        WW(0x10, (RW(0x10) & ~3) | (flags2 & 3));
        SetHeader();
        stack.Clear();
        stack.AddRange(s.Stack);
        frames.Clear();
        frames.AddRange(s.Frames.Select(f => new Frame { ReturnPc = f.ReturnPc, StoreVar = f.StoreVar, Locals = f.Locals.ToArray(), ArgCount = f.ArgCount, StackBase = f.StackBase }));
        pc = s.Pc;
        pendingRead = s.Pending;
        quit = false;
    }

    private const string SaveMagic = "ACZM1";

    /// <summary>
    /// The machine's state while it waits for input, as bytes (for saved games). Only valid between turns.
    /// </summary>
    public byte[] SaveState()
    {
        if (pendingRead == null) throw new InvalidOperationException("The story can only be saved while it waits for input.");
        var s = TakeSnapshot(-1);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(SaveMagic);
        w.Write(Release); w.Write(Serial); w.Write(Checksum);
        w.Write(s.Dynamic.Length); w.Write(s.Dynamic);
        w.Write(s.Stack.Length); foreach (var v in s.Stack) w.Write(v);
        w.Write(s.Frames.Count);
        foreach (var f in s.Frames)
        {
            w.Write(f.ReturnPc); w.Write(f.StoreVar); w.Write(f.ArgCount); w.Write(f.StackBase);
            w.Write(f.Locals.Length); foreach (var v in f.Locals) w.Write(v);
        }
        w.Write(s.Pc);
        var p = s.Pending!;
        w.Write(p.Char); w.Write(p.TextBuffer); w.Write(p.ParseBuffer); w.Write(p.StoreVar);
        w.Write(upperLines);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Restores a state from <see cref="SaveState"/>. The machine is then waiting for input again.</summary>
    public void RestoreState(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (r.ReadString() != SaveMagic) throw new InvalidDataException("Not a saved Z-machine position.");
        int release = r.ReadInt32(); string serial = r.ReadString(); int checksum = r.ReadInt32();
        if (release != Release || serial != Serial || checksum != Checksum)
            throw new InvalidDataException("This saved position belongs to a different story file.");
        var s = new Snapshot { Dynamic = r.ReadBytes(r.ReadInt32()) };
        s.Stack = new ushort[r.ReadInt32()];
        for (int i = 0; i < s.Stack.Length; i++) s.Stack[i] = r.ReadUInt16();
        int frameCount = r.ReadInt32();
        for (int i = 0; i < frameCount; i++)
        {
            var f = new Frame { ReturnPc = r.ReadInt32(), StoreVar = r.ReadInt32(), ArgCount = r.ReadInt32(), StackBase = r.ReadInt32() };
            f.Locals = new ushort[r.ReadInt32()];
            for (int k = 0; k < f.Locals.Length; k++) f.Locals[k] = r.ReadUInt16();
            s.Frames.Add(f);
        }
        s.Pc = r.ReadInt32();
        s.Pending = new PendingRead(r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        int lines = r.ReadInt32();
        ApplySnapshot(s);
        SplitWindow(lines);
        UpdateStatus();
    }
}
