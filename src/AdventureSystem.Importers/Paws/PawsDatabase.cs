using System.Text;
using AdventureSystem.Core.Snapshots;

namespace AdventureSystem.Importers.Paws;

/// <summary>A vocabulary entry: five significant letters, a word value (synonyms share it) and a word type.</summary>
internal sealed record PawsWord(string Text, int Number, PawsWordType Type);

internal enum PawsWordType
{
    Verb = 0,
    Adverb = 1,
    Noun = 2,
    Adjective = 3,
    Preposition = 4,
    Conjunction = 5,
    Pronoun = 6,
}

/// <summary>One condact of a process table entry: opcode plus up to two raw parameters.</summary>
internal sealed record PawsCondact(int Opcode, int[] Args);

/// <summary>One process table entry: verb and noun word values (1 = "*", 255 = "_") and its condacts.</summary>
internal sealed record PawsEntry(int Verb, int Noun, List<PawsCondact> Condacts);

/// <summary>A picture drawstring together with its "location flags" byte.</summary>
internal sealed record PawsPictureData(int Number, int Attribute, byte[] Memory, int Address)
{
    /// <summary>Bit 7: drawn when the location is described (otherwise a subroutine picture).</summary>
    public bool IsLocationPicture => (Attribute & 0x80) != 0;
    public int Ink => Attribute & 7;
    public int Paper => (Attribute >> 3) & 7;
    public bool IsEmpty => (Memory[Address & 0xFFFF] & 7) == 7;
}

/// <summary>
/// Low-level reader for a Gilsoft PAWS database held in a ZX Spectrum memory image.
/// <para>
/// Layout (from the PAW Technical Guide and the UnPAWS / pawgr sources): the word at 65533 points at
/// "MainTop", which holds the 19 UDGs (152 bytes), 16 shade patterns (128 bytes) and 50 bytes of
/// miscellaneous data: +281 default character set, +283 / +297 the 128K message / location page tables,
/// +311 the default colours as the control sequence 16,ink,17,paper,18,flash,19,bright,20,inverse,21,over
/// followed by the border (this sequence is the PAWS signature), +324 object count, +325 location count,
/// +326 message count, +327 system message count, +328 process table count, +329 character set count,
/// +330 character set address, +332 compression dictionary address.
/// The top of memory holds the table pointers: 65497 processes, 65499 object texts, 65501 location texts,
/// 65503 messages, 65505 system messages, 65507 connections, 65509 vocabulary, 65511 initially-at,
/// 65513 object words, 65515 object weights/attributes, 65521 pictures, 65523 picture attributes.
/// On a 128K database every RAM page (0,1,3,4,6,7 at 0xC000) repeats this pointer block for the locations,
/// messages, connections and pictures stored on that page; everything else lives on page 0.
/// </para>
/// Texts are stored XOR 255 and end with 31; bytes 165-255 are dictionary tokens when the database has
/// been compressed (otherwise Spectrum BASIC keywords).
/// </summary>
internal sealed class PawsDatabase
{
    public const int PtrProcesses = 65497;
    public const int PtrObjects = 65499;
    public const int PtrLocations = 65501;
    public const int PtrMessages = 65503;
    public const int PtrSysMessages = 65505;
    public const int PtrConnections = 65507;
    public const int PtrVocabulary = 65509;
    public const int PtrInitiallyAt = 65511;
    public const int PtrObjectWords = 65513;
    public const int PtrObjectWeights = 65515;
    public const int PtrGraphics = 65521;
    public const int PtrGraphicAttributes = 65523;
    public const int PtrVersion = 65527;
    public const int PtrMainTop = 65533;

    private const int MaxTextLength = 8192;

    /// <summary>Page 0 view (the main database).</summary>
    public byte[] Main { get; }
    /// <summary>128K: memory views with each RAM page mapped at 0xC000 (key = page number).</summary>
    public IReadOnlyDictionary<int, byte[]> Pages { get; }
    public bool Is128K { get; }
    public int MainTop { get; }
    public bool Compressed { get; }
    public int DictionaryAddress { get; }
    public int NumObjects { get; }
    public int NumLocations { get; }
    public int NumMessages { get; }
    public int NumSysMessages { get; }
    public int NumProcesses { get; }
    public int NumCharsets { get; }
    public int CharsetAddress { get; }
    public int DefaultCharset { get; }
    public int DefaultInk => Peek(Main, MainTop + 312);
    public int DefaultPaper => Peek(Main, MainTop + 314);
    public int DefaultBright => Peek(Main, MainTop + 318);
    public int DefaultBorder => Peek(Main, MainTop + 323);
    public int VersionByte => Peek(Main, PtrVersion);
    /// <summary>Location pages: (page, first location, one past last location).</summary>
    public List<(int Page, int Start, int End)> LocationPages { get; }
    public List<(int Page, int Start, int End)> MessagePages { get; }
    public List<string> Warnings { get; } = new();

    private readonly string[] tokens = new string[256];

    private PawsDatabase(byte[] main, Dictionary<int, byte[]> pages, bool is128K)
    {
        Main = main;
        Pages = pages;
        Is128K = is128K;
        MainTop = Word(main, PtrMainTop);
        NumObjects = Peek(main, MainTop + 324);
        NumLocations = Peek(main, MainTop + 325);
        NumMessages = Peek(main, MainTop + 326);
        NumSysMessages = Peek(main, MainTop + 327);
        NumProcesses = Peek(main, MainTop + 328);
        NumCharsets = Peek(main, MainTop + 329);
        CharsetAddress = Word(main, MainTop + 330);
        DefaultCharset = Peek(main, MainTop + 281);
        DictionaryAddress = Word(main, MainTop + 332);
        Compressed = Peek(main, DictionaryAddress) == 0;
        if (Compressed) LoadDictionary();
        LocationPages = PageDistribution(MainTop + 297, NumLocations);
        MessagePages = PageDistribution(MainTop + 283, NumMessages);
    }

    // ------------------------------------------------------------------ detection

    public static bool HasSignature(byte[] mem)
    {
        int top = Word(mem, PtrMainTop);
        if (top < 0x4000 - 311 || top > 0xFFFF - 340) return false;
        int attr = top + 311;
        for (int i = 0; i < 6; i++)
            if (mem[attr + 2 * i] != 16 + i) return false;
        // Sanity checks that make a false positive on random memory very unlikely.
        if (Word(mem, PtrVocabulary) < 0x4000) return false;
        if (Word(mem, PtrProcesses) < 0x4000) return false;
        if (mem[top + 328] < 3) return false; // Response, Process 1 and Process 2 always exist.
        return true;
    }

    public static PawsDatabase? TryOpen(SpectrumSnapshot snap)
    {
        if (snap.Banks128 is { } banks && banks[0] != null)
        {
            var pages = new Dictionary<int, byte[]>();
            foreach (var p in new[] { 0, 1, 3, 4, 6, 7 })
            {
                if (banks[p] == null) continue;
                var view = (byte[])snap.Memory.Clone();
                Array.Copy(banks[p], 0, view, 0xC000, Math.Min(16384, banks[p].Length));
                pages[p] = view;
            }
            if (HasSignature(pages[0]))
                return new PawsDatabase(pages[0], pages, true);
        }
        if (HasSignature(snap.Memory))
            return new PawsDatabase(snap.Memory, new Dictionary<int, byte[]> { [0] = snap.Memory }, false);
        return null;
    }

    // ------------------------------------------------------------------ memory helpers

    public static int Peek(byte[] mem, int address) => mem[address & 0xFFFF];
    public static int Word(byte[] mem, int address) => mem[address & 0xFFFF] | (mem[(address + 1) & 0xFFFF] << 8);

    private List<(int Page, int Start, int End)> PageDistribution(int table, int total)
    {
        var result = new List<(int, int, int)>();
        if (!Is128K)
        {
            result.Add((0, 0, total));
            return result;
        }
        // Pairs of (page, first number NOT on this page); 255 as the count = "all the rest".
        int start = 0, last = 0;
        for (int i = 0; i < 14; i += 2)
        {
            int page = Peek(Main, table + i);
            if (page == 255) break;
            int val = Peek(Main, table + i + 1);
            int end = val != 255 ? val : last != 255 ? total : start;
            last = val;
            end = Math.Min(end, total);
            if (end > start && page < 8)
            {
                if (Pages.ContainsKey(page)) result.Add((page, start, end));
                else Warnings.Add($"RAM page {page} (entries {start}-{end - 1}) is missing from the snapshot.");
                start = end;
            }
        }
        if (start < total && result.Count == 0) result.Add((0, 0, total));
        return result;
    }

    /// <summary>Finds the memory view and table index holding entry n of a paged table.</summary>
    public (byte[] Mem, int Index)? Locate(List<(int Page, int Start, int End)> pages, int n)
    {
        foreach (var (page, start, end) in pages)
            if (n >= start && n < end && Pages.TryGetValue(page, out var mem))
                return (mem, n - start);
        return null;
    }

    // ------------------------------------------------------------------ text

    private void LoadDictionary()
    {
        int a = DictionaryAddress;
        for (int c = 164; c < 256; c++)
        {
            var sb = new StringBuilder();
            for (int guard = 0; guard < 32; guard++)
            {
                int b = Peek(Main, a++);
                if ((b & 0x80) != 0) { sb.Append((char)(b & 0x7F)); break; }
                sb.Append((char)b);
            }
            tokens[c] = sb.ToString();
        }
    }

    /// <summary>Decodes a text (XOR 255, terminated by 31) into tidy text with '\n' line breaks.</summary>
    public string ReadText(byte[] mem, int address) => Clean(ReadRaw(mem, address));

    /// <summary>Decodes a text without any whitespace cleanup (spacing matters for compound messages).</summary>
    public string ReadRaw(byte[] mem, int address)
    {
        var sb = new StringBuilder();
        int a = address;
        int skip = 0;
        for (int n = 0; n < MaxTextLength; n++)
        {
            int c = Peek(mem, a++) ^ 0xFF;
            if (c == 31) break;
            if (skip > 0) { skip--; continue; }
            AppendChar(sb, c, ref skip);
        }
        return sb.ToString();
    }

    private void AppendChar(StringBuilder sb, int c, ref int skip)
    {
        switch (c)
        {
            case 6: sb.Append(' '); return;                  // TAB (PRINT comma)
            case 7: case 13: sb.Append('\n'); return;        // newline
            case >= 16 and <= 21: skip = 1; return;          // INK/PAPER/FLASH/BRIGHT/INVERSE/OVER + value
            case 22 or 23: skip = 2; return;                 // AT / TAB + 2 values
            case 96: sb.Append('£'); return;
            case 127: sb.Append('©'); return;
            case >= 32 and < 127: sb.Append(Shown(c)); return;
            case >= 165 when Compressed:
                foreach (var t in tokens[c] ?? "")
                    if (t >= 32 && t < 127) sb.Append(Shown(t));
                    else if (t is '\x07' or '\x0D') sb.Append('\n');
                return;
            case >= 163:
                if (c - 163 < SpectrumKeywords.Length) sb.Append(SpectrumKeywords[c - 163]);
                return;
            default: return;                                  // charset selects, UDGs, block graphics
        }
    }

    private static string Clean(string s)
    {
        // Collapse the runs of spaces PAWS authors used for screen layout.
        var lines = s.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            while (l.Contains("  ")) l = l.Replace("  ", " ");
            lines[i] = l.Trim();
        }
        return string.Join("\n", lines).Trim('\n');
    }

    public string TableText(byte[] mem, int pointerTable, int index)
    {
        int table = Word(mem, pointerTable);
        return ReadText(mem, Word(mem, table + 2 * index));
    }

    public string LocationText(int n) =>
        Locate(LocationPages, n) is { } l ? TableText(l.Mem, PtrLocations, l.Index) : "";

    public string MessageText(int n) =>
        Locate(MessagePages, n) is { } l ? TableText(l.Mem, PtrMessages, l.Index) : "";

    public string SysMessageText(int n) => n < NumSysMessages ? TableText(Main, PtrSysMessages, n) : "";
    public string ObjectText(int n) => n < NumObjects ? TableText(Main, PtrObjects, n) : "";

    /// <summary>Raw texts without cleanup (used where leading/trailing spaces and newlines matter).</summary>
    public string RawSysMessage(int n) =>
        n < NumSysMessages ? ReadRaw(Main, Word(Main, Word(Main, PtrSysMessages) + 2 * n)) : "";

    public string RawMessage(int n) =>
        Locate(MessagePages, n) is { } l ? ReadRaw(l.Mem, Word(l.Mem, Word(l.Mem, PtrMessages) + 2 * l.Index)) : "";

    // ------------------------------------------------------------------ tables

    public List<PawsWord> ReadVocabulary()
    {
        var words = new List<PawsWord>();
        int a = Word(Main, PtrVocabulary);
        for (int guard = 0; guard < 4000 && a < 0xFFF0; guard++, a += 7)
        {
            if (Peek(Main, a) == 0) break;
            var sb = new StringBuilder();
            for (int k = 0; k < 5; k++)
            {
                int c = Peek(Main, a + k) ^ 0xFF;
                if (c >= 32 && c < 127) sb.Append((char)c);
            }
            var text = sb.ToString().Trim();
            int type = Peek(Main, a + 6);
            if (text.Length == 0 || type > 6) continue;
            words.Add(new PawsWord(text.ToLowerInvariant(), Peek(Main, a + 5), (PawsWordType)type));
        }
        return words;
    }

    /// <summary>Connections of location n: (word value, destination).</summary>
    public List<(int Word, int Destination)> Connections(int n)
    {
        var list = new List<(int, int)>();
        if (Locate(LocationPages, n) is not { } l) return list;
        int table = Word(l.Mem, PtrConnections);
        int a = Word(l.Mem, table + 2 * l.Index);
        for (int guard = 0; guard < 128 && Peek(l.Mem, a) != 255; guard++, a += 2)
            list.Add((Peek(l.Mem, a), Peek(l.Mem, a + 1)));
        return list;
    }

    public int InitiallyAt(int obj) => Peek(Main, Word(Main, PtrInitiallyAt) + obj);
    public int ObjectNoun(int obj) => Peek(Main, Word(Main, PtrObjectWords) + 2 * obj);
    public int ObjectAdjective(int obj) => Peek(Main, Word(Main, PtrObjectWords) + 2 * obj + 1);
    public int ObjectAttributes(int obj) => Peek(Main, Word(Main, PtrObjectWeights) + obj);

    public List<PawsEntry> ReadProcessTable(int table)
    {
        var entries = new List<PawsEntry>();
        int a = Word(Main, Word(Main, PtrProcesses) + 2 * table);
        for (int guard = 0; guard < 2000; guard++, a += 4)
        {
            int verb = Peek(Main, a);
            if (verb == 0) break;
            int noun = Peek(Main, a + 1);
            int c = Word(Main, a + 2);
            var condacts = new List<PawsCondact>();
            for (int k = 0; k < 256; k++)
            {
                int op = Peek(Main, c);
                if (op == 255) break;
                if (op >= PawsCondactInfo.All.Length)
                {
                    Warnings.Add($"Process {table}: unknown condact {op} in entry {entries.Count}; rest of entry ignored.");
                    break;
                }
                int n = PawsCondactInfo.All[op].Params;
                var args = new int[n];
                for (int p = 0; p < n; p++) args[p] = Peek(Main, c + 1 + p);
                condacts.Add(new PawsCondact(op, args));
                c += 1 + n;
            }
            entries.Add(new PawsEntry(verb, noun, condacts));
        }
        return entries;
    }

    public PawsPictureData? Picture(int n)
    {
        if (Locate(LocationPages, n) is not { } l) return null;
        int table = Word(l.Mem, PtrGraphics);
        int attrs = Word(l.Mem, PtrGraphicAttributes);
        if (table == 0 || attrs == 0) return null;
        return new PawsPictureData(n, Peek(l.Mem, attrs + l.Index), l.Mem, Word(l.Mem, table + 2 * l.Index));
    }

    /// <summary>8 bytes of shade pattern 0-15.</summary>
    public byte[] Shade(int n) => Enumerable.Range(0, 8).Select(i => (byte)Peek(Main, MainTop + 152 + 8 * (n & 15) + i)).ToArray();

    private Dictionary<int, char>? lineCharacters;

    /// <summary>
    /// Characters the game's own character set draws as lines or blocks (say "$" redrawn as a thick bar, printed in a
    /// row to frame a message), with the Unicode character that looks like each. Letters, digits and characters that
    /// are lines anyway (_ - |) are left alone.
    /// </summary>
    public IReadOnlyDictionary<int, char> LineCharacters => lineCharacters ??= FindLineCharacters();

    /// <summary>How a character code in the game's text is shown.</summary>
    public char Shown(int c) => LineCharacters.TryGetValue(c, out var ch) ? ch : (char)c;

    /// <summary>The character code for a shown character that stands for one of <see cref="LineCharacters"/>, or -1.</summary>
    public int Stored(char shown)
    {
        foreach (var (code, ch) in LineCharacters) if (ch == shown) return code;
        return -1;
    }

    private Dictionary<int, char> FindLineCharacters()
    {
        var map = new Dictionary<int, char>();
        if (DefaultCharset < 1 || DefaultCharset > NumCharsets) return map;
        for (int c = 33; c < 127; c++)
        {
            if (char.IsLetterOrDigit((char)c) || c is '_' or '-' or '|' or 96) continue;
            // Each shown character must stand for one code (it's turned back on export): look-alikes when the nearest is taken.
            if (Glyph(DefaultCharset, c) is { } g && LineCharacter(g) is { } line &&
                (line + "━─═▬").FirstOrDefault(x => !map.ContainsValue(x)) is var free and not '\0')
                map[c] = free;
        }
        return map;
    }

    /// <summary>A glyph made only of whole rows or of the same columns in every row, as the nearest Unicode line or block character; null otherwise.</summary>
    internal static char? LineCharacter(byte[] g)
    {
        if (g.Length != 8 || g.All(b => b == 0)) return null;
        if (g.All(b => b is 0 or 0xFF))
        {
            int rows = g.Count(b => b == 0xFF), first = Array.IndexOf(g, (byte)0xFF), last = Array.LastIndexOf(g, (byte)0xFF);
            if (last - first + 1 != rows) return null;                     // separate bars: not a single line
            if (rows == 8) return '█';
            if (last == 7) return "▁▂▃▄▅▆▇"[rows - 1];
            if (first == 0) return rows >= 4 ? '▀' : '▔';
            return rows >= 2 ? '━' : '─';
        }
        if (g.All(b => b == g[0]))
        {
            int bits = System.Numerics.BitOperations.PopCount(g[0]), run = g[0] >> System.Numerics.BitOperations.TrailingZeroCount(g[0]);
            if ((run & (run + 1)) != 0) return null;                          // separate columns: not a single line
            if ((g[0] & 0x80) != 0) return bits >= 4 ? '▌' : '▏';          // bit 7 is the leftmost pixel
            if ((g[0] & 0x01) != 0) return bits >= 4 ? '▐' : '▕';
            return bits >= 2 ? '┃' : '│';
        }
        return null;
    }

    /// <summary>Glyph bytes for a picture TEXT character, or null when only the ROM font has it.</summary>
    public byte[]? Glyph(int set, int ch)
    {
        int addr;
        if (ch >= 144 && ch <= 162) addr = MainTop + 8 * (ch - 144);                       // UDGs
        else if (ch >= 128 && ch <= 143) return BlockGraphic(ch - 128);                      // ROM block graphics
        else if (set >= 1 && set <= NumCharsets && ch >= 32 && ch < 128) addr = CharsetAddress + 768 * (set - 1) + 8 * (ch - 32);
        else return null;
        return Enumerable.Range(0, 8).Select(i => (byte)Peek(Main, addr + i)).ToArray();
    }

    private static byte[] BlockGraphic(int n)
    {
        // Bit 0 top-right, bit 1 top-left, bit 2 bottom-right, bit 3 bottom-left quadrant.
        byte top = (byte)(((n & 2) != 0 ? 0xF0 : 0) | ((n & 1) != 0 ? 0x0F : 0));
        byte bottom = (byte)(((n & 8) != 0 ? 0xF0 : 0) | ((n & 4) != 0 ? 0x0F : 0));
        return new[] { top, top, top, top, bottom, bottom, bottom, bottom };
    }

    private static readonly string[] SpectrumKeywords =
    {
        "SPECTRUM ", "PLAY ", "RND", "INKEY$", "PI", "FN ", "POINT ", "SCREEN$ ", "ATTR ", "AT ", "TAB ", "VAL$ ",
        "CODE ", "VAL ", "LEN ", "SIN ", "COS ", "TAN ", "ASN ", "ACS ", "ATN ", "LN ", "EXP ", "INT ", "SQR ", "SGN ",
        "ABS ", "PEEK ", "IN ", "USR ", "STR$ ", "CHR$ ", "NOT ", "BIN ", " OR ", " AND ", "<=", ">=", "<>", " LINE ",
        " THEN ", " TO ", " STEP ", " DEF FN ", " CAT ", " FORMAT ", " MOVE ", " ERASE ", " OPEN #", " CLOSE #",
        " MERGE ", " VERIFY ", " BEEP ", " CIRCLE ", " INK ", " PAPER ", " FLASH ", " BRIGHT ", " INVERSE ", " OVER ",
        " OUT ", " LPRINT ", " LLIST ", " STOP ", " READ ", " DATA ", " RESTORE ", " NEW ", " BORDER ", " CONTINUE ",
        " DIM ", " REM ", " FOR ", " GO TO ", " GO SUB ", " INPUT ", " LOAD ", " LIST ", " LET ", " PAUSE ", " NEXT ",
        " POKE ", " PRINT ", " PLOT ", " RUN ", " SAVE ", " RANDOMIZE ", " IF ", " CLS ", " DRAW ", " CLEAR ",
        " RETURN ", " COPY ",
    };
}
