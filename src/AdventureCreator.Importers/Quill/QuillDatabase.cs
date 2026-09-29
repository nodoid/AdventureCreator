using System.Text;

namespace AdventureCreator.Importers.Quill;

/// <summary>Machine the Quill database was written for.</summary>
internal enum QuillPlatform
{
    Spectrum,
    AmstradCpc,
    Commodore64,
}

/// <summary>A condition or action as stored in a Quill table. Actions use the "late" Spectrum numbering (see <see cref="QuillDatabase.ActionNames"/>).</summary>
internal sealed record QuillCondact(int Op, int[] Args);

/// <summary>One entry of the response ("event") or process ("status") table.</summary>
internal sealed class QuillEntry
{
    public int Verb { get; init; }
    public int Noun { get; init; }
    public int Address { get; init; }
    public List<QuillCondact> Conditions { get; } = new();
    public List<QuillCondact> Actions { get; } = new();
}

/// <summary>Thrown while decoding when the data does not form a valid Quill database.</summary>
internal sealed class QuillFormatException(string message) : Exception(message);

/// <summary>
/// Reader for Gilsoft's The Quill databases held in a memory image.
/// <para>
/// Layout (after a 13 byte colour table <c>10 ink 11 paper 12 flash 13 bright 14 inverse 15 over border</c> on the
/// Spectrum): max carried, no. of objects, no. of locations, no. of messages, [no. of system messages — later
/// databases only], then pointers to the response table, process table, object text pointers, location text
/// pointers, message text pointers, [system message pointers], connection pointers, vocabulary, object start
/// locations, [object word table] and (later databases) a text compression dictionary.
/// All text and vocabulary bytes are stored complemented (XOR 0xFF).
/// </para>
/// </summary>
internal sealed class QuillDatabase
{
    public const int EarlySignatureAddress = 0x6D04;
    public const int LateSignatureAddress = 0x6B85;

    /// <summary>Action names in "late" numbering, which every version is normalised to.</summary>
    public static readonly string[] ActionNames =
    {
        "INVEN", "DESC", "QUIT", "END", "DONE", "OK", "ANYKEY", "SAVE", "LOAD", "TURNS", "SCORE", "CLS", "DROPALL",
        "AUTOG", "AUTOD", "AUTOW", "AUTOR", "PAUSE", "PAPER", "INK", "BORDER", "GOTO", "MESSAGE", "REMOVE", "GET",
        "DROP", "WEAR", "DESTROY", "CREATE", "SWAP", "PLACE", "SET", "CLEAR", "PLUS", "MINUS", "LET", "BEEP",
    };

    public static readonly string[] ConditionNames =
    {
        "AT", "NOTAT", "ATGT", "ATLT", "PRESENT", "ABSENT", "WORN", "NOTWORN", "CARRIED", "NOTCARR", "CHANCE",
        "ZERO", "NOTZERO", "EQ", "GT", "LT",
    };

    // Action opcodes (late numbering).
    public const int A_INVEN = 0, A_DESC = 1, A_QUIT = 2, A_END = 3, A_DONE = 4, A_OK = 5, A_ANYKEY = 6, A_SAVE = 7,
        A_LOAD = 8, A_TURNS = 9, A_SCORE = 10, A_CLS = 11, A_DROPALL = 12, A_AUTOG = 13, A_AUTOD = 14, A_AUTOW = 15,
        A_AUTOR = 16, A_PAUSE = 17, A_PAPER = 18, A_INK = 19, A_BORDER = 20, A_GOTO = 21, A_MESSAGE = 22, A_REMOVE = 23,
        A_GET = 24, A_DROP = 25, A_WEAR = 26, A_DESTROY = 27, A_CREATE = 28, A_SWAP = 29, A_PLACE = 30, A_SET = 31,
        A_CLEAR = 32, A_PLUS = 33, A_MINUS = 34, A_LET = 35, A_BEEP = 36;

    private readonly byte[] mem;
    private readonly int memBase;
    private readonly int memEnd;

    private QuillDatabase(byte[] memory, QuillPlatform platform, int version, int memBase, int memEnd)
    {
        mem = memory;
        Platform = platform;
        Version = version;
        this.memBase = memBase;
        this.memEnd = memEnd;
    }

    public QuillPlatform Platform { get; }
    /// <summary>0 = early Spectrum ("A"), 5 = Commodore 64, 10 = later Spectrum ("C") / Amstrad CPC.</summary>
    public int Version { get; }
    public bool IsEarly => Version == 0;
    public bool IsLate => Version >= 10;
    /// <summary>Address of the colour table signature (Spectrum only), otherwise -1.</summary>
    public int SignatureAddress { get; private set; } = -1;
    public int HeaderAddress { get; private set; }

    public int MaxCarried { get; private set; }
    public int ObjectCount { get; private set; }
    public int LocationCount { get; private set; }
    public int MessageCount { get; private set; }
    public int SystemMessageCount { get; private set; }

    public int Ink { get; private set; } = 7;
    public int Paper { get; private set; }
    public int Bright { get; private set; }
    public int Border { get; private set; }

    public int ResponseTable { get; private set; }
    public int ProcessTable { get; private set; }
    public int ObjectTextTable { get; private set; }
    public int LocationTextTable { get; private set; }
    public int MessageTextTable { get; private set; }
    public int SystemMessageBase { get; private set; }
    public int ConnectionTable { get; private set; }
    public int VocabularyTable { get; private set; }
    public int ObjectStartTable { get; private set; }
    public int ObjectWordTable { get; private set; } = -1;
    public int Dictionary { get; private set; } = -1;

    public List<(string Word, int Number)> Words { get; } = new();
    public List<string> Locations { get; } = new();
    public List<string> Objects { get; } = new();
    public List<string> Messages { get; } = new();
    public List<string> SystemMessages { get; } = new();
    /// <summary>System messages with their original spacing (used when joining fragments such as "You have taken " + n + " turns").</summary>
    public List<string> RawSystemMessages { get; } = new();
    public List<List<(int Word, int Target)>> Connections { get; } = new();
    public byte[] ObjectStart { get; private set; } = Array.Empty<byte>();
    /// <summary>Word number naming each object (later databases), 255 = none; null when the version has no such table.</summary>
    public byte[]? ObjectWords { get; private set; }
    public List<QuillEntry> Responses { get; private set; } = new();
    public List<QuillEntry> Processes { get; private set; } = new();

    public string VersionName => Platform switch
    {
        QuillPlatform.AmstradCpc => "Quill (Amstrad CPC)",
        QuillPlatform.Commodore64 => "Quill (Commodore 64)",
        _ => IsEarly ? "Quill (ZX Spectrum, early version A)" : "Quill (ZX Spectrum, later version C)",
    };

    public byte this[int address] => mem[address & 0xFFFF];
    public int Word(int address) => mem[address & 0xFFFF] | (mem[(address + 1) & 0xFFFF] << 8);
    public byte[] Memory => mem;

    public bool IsValidAddress(int address) => address >= memBase && address < memEnd;

    // ---------------------------------------------------------------- detection

    /// <summary>True when the 13 bytes at <paramref name="a"/> form the Quill colour-table signature.</summary>
    public static bool HasSignature(byte[] memory, int a) =>
        a >= 0 && a + 12 < memory.Length &&
        memory[a] == 0x10 && memory[a + 2] == 0x11 && memory[a + 4] == 0x12 &&
        memory[a + 6] == 0x13 && memory[a + 8] == 0x14 && memory[a + 10] == 0x15 &&
        memory[a + 1] <= 9 && memory[a + 3] <= 9 && memory[a + 5] <= 8 && memory[a + 7] <= 8 && memory[a + 12] <= 7;

    /// <summary>Locates and fully decodes a Spectrum Quill database in a 64K memory image, or returns null.</summary>
    public static QuillDatabase? FindSpectrum(byte[] memory64K)
    {
        // Known positions first (early games at 6D04h, later at 6B85h), then scan everything.
        if (HasSignature(memory64K, EarlySignatureAddress) && TryLoadSpectrum(memory64K, EarlySignatureAddress, 0) is { } e) return e;
        if (HasSignature(memory64K, LateSignatureAddress) && TryLoadSpectrum(memory64K, LateSignatureAddress, 10) is { } l) return l;
        for (int a = 0x5C00; a < 0xFFF0; a++)
        {
            if (!HasSignature(memory64K, a)) continue;
            if (TryLoadSpectrum(memory64K, a, 10) is { } late) return late;
            if (TryLoadSpectrum(memory64K, a, 0) is { } early) return early;
        }
        return null;
    }

    public static QuillDatabase? TryLoadSpectrum(byte[] memory64K, int signature, int version)
    {
        try
        {
            var db = new QuillDatabase(memory64K, QuillPlatform.Spectrum, version, 0x5C00, 0x10000)
            {
                SignatureAddress = signature,
                Ink = memory64K[signature + 1],
                Paper = memory64K[signature + 3],
                Bright = memory64K[signature + 7],
                Border = memory64K[signature + 12],
            };
            db.Load(signature + 13);
            return db;
        }
        catch (QuillFormatException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>Loads a CPC (version 10) or C64 (version 5) database whose header starts at <paramref name="header"/>.</summary>
    public static QuillDatabase? TryLoadOther(byte[] memory64K, QuillPlatform platform, int header)
    {
        try
        {
            int version = platform == QuillPlatform.Commodore64 ? 5 : 10;
            int b = platform == QuillPlatform.Commodore64 ? 0x800 : 0x1B00;
            var db = new QuillDatabase(memory64K, platform, version, b, b + 0xA500) { Ink = 7, Paper = 0 };
            db.Load(header);
            return db;
        }
        catch (QuillFormatException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    // ---------------------------------------------------------------- header / tables

    private int Ptr(int address, string what)
    {
        int p = Word(address);
        if (!IsValidAddress(p)) throw new QuillFormatException($"{what} pointer {p:X4} out of range");
        return p;
    }

    private void Load(int header)
    {
        HeaderAddress = header;
        MaxCarried = this[header];
        ObjectCount = this[header + 1];
        LocationCount = this[header + 2];
        MessageCount = this[header + 3];
        if (LocationCount == 0 || ObjectCount == 0) throw new QuillFormatException("no locations/objects");

        int h = header;
        if (Version > 0)
        {
            h++;
            SystemMessageCount = this[h + 3];
            if (SystemMessageCount == 0) throw new QuillFormatException("no system messages");
        }
        else SystemMessageCount = 32;

        ResponseTable = Ptr(h + 4, "response");
        ProcessTable = Ptr(h + 6, "process");
        ObjectTextTable = Ptr(h + 8, "objects");
        LocationTextTable = Ptr(h + 10, "locations");
        MessageTextTable = Ptr(h + 12, "messages");
        if (Version > 0)
        {
            SystemMessageBase = Ptr(h + 14, "system messages");
            ConnectionTable = Ptr(h + 16, "connections");
            VocabularyTable = Ptr(h + 18, "vocabulary");
            ObjectStartTable = Ptr(h + 20, "object positions");
            ObjectWordTable = Version >= 10 ? Ptr(h + 22, "object words") : -1;
            Dictionary = h + 29;
        }
        else
        {
            ConnectionTable = Ptr(h + 14, "connections");
            VocabularyTable = Ptr(h + 16, "vocabulary");
            ObjectStartTable = Ptr(h + 18, "object positions");
            // Early games keep their system messages in the runtime, just after the UDGs (address in system variable UDG).
            SystemMessageBase = Word(23675) + 168;
            if (!IsValidAddress(SystemMessageBase)) throw new QuillFormatException("system messages not found");
        }

        ReadVocabulary();
        for (int i = 0; i < LocationCount; i++) Locations.Add(TextAt(Ptr(LocationTextTable + 2 * i, "location text")));
        for (int i = 0; i < ObjectCount; i++) Objects.Add(TextAt(Ptr(ObjectTextTable + 2 * i, "object text")));
        for (int i = 0; i < MessageCount; i++) Messages.Add(TextAt(Ptr(MessageTextTable + 2 * i, "message text")));
        if (Version > 0)
        {
            for (int i = 0; i < SystemMessageCount; i++)
            {
                int p = Ptr(SystemMessageBase + 2 * i, "system message");
                SystemMessages.Add(TextAt(p));
                RawSystemMessages.Add(TextAt(p, out _, normalise: false));
            }
        }
        else
        {
            int a = SystemMessageBase;
            for (int i = 0; i < SystemMessageCount; i++)
            {
                RawSystemMessages.Add(TextAt(a, out _, normalise: false));
                SystemMessages.Add(TextAt(a, out int next));
                a = next;
            }
        }

        ReadConnections();

        ObjectStart = new byte[ObjectCount];
        for (int i = 0; i < ObjectCount; i++)
        {
            byte loc = this[ObjectStartTable + i];
            if (loc >= LocationCount && loc < 252) throw new QuillFormatException($"object {i} starts in invalid location {loc}");
            ObjectStart[i] = loc;
        }
        if (ObjectWordTable >= 0)
        {
            ObjectWords = new byte[ObjectCount];
            for (int i = 0; i < ObjectCount; i++) ObjectWords[i] = this[ObjectWordTable + i];
        }

        Responses = ReadTable(ResponseTable);
        Processes = ReadTable(ProcessTable);
    }

    private void ReadVocabulary()
    {
        int a = VocabularyTable;
        for (int n = 0; this[a] != 0; n++, a += 5)
        {
            if (n > 2000 || !IsValidAddress(a + 4)) throw new QuillFormatException("vocabulary not terminated");
            var sb = new StringBuilder(4);
            for (int k = 0; k < 4; k++)
            {
                int c = 0xFF - this[a + k];
                if (Platform == QuillPlatform.Commodore64) c &= 0x7F;
                if (c < 32 || c > 126) throw new QuillFormatException("invalid vocabulary character");
                sb.Append((char)c);
            }
            var w = sb.ToString().TrimEnd();
            if (w.Length == 0) throw new QuillFormatException("empty vocabulary word");
            Words.Add((w, this[a + 4]));
        }
        if (Words.Count == 0) throw new QuillFormatException("empty vocabulary");
    }

    private void ReadConnections()
    {
        for (int loc = 0; loc < LocationCount; loc++)
        {
            int a = Ptr(ConnectionTable + 2 * loc, "connection");
            var list = new List<(int, int)>();
            int guard = 0;
            while (this[a] != 0xFF)
            {
                if (++guard > 128 || !IsValidAddress(a + 1)) throw new QuillFormatException("connection list not terminated");
                int target = this[a + 1];
                if (target >= LocationCount) throw new QuillFormatException($"connection to invalid location {target}");
                list.Add((this[a], target));
                a += 2;
            }
            Connections.Add(list);
        }
    }

    private static int ActionParamCount(int op, QuillPlatform platform)
    {
        if (op == A_SWAP || op == A_PLACE) return 2;
        if (op == A_INK && platform == QuillPlatform.AmstradCpc) return 2;
        if (op < A_PAUSE) return 0;
        if (op < A_SET + 2) return 1;
        return 2;
    }

    /// <summary>Maps a raw action byte to the "late" numbering used throughout the importer, or -1.</summary>
    public int NormaliseAction(int raw)
    {
        int op = raw;
        if (Version == 0)
        {
            // Early: INVEN..SCORE 0-10, PAUSE 11, GOTO 12 ... SWAP 20, SET 21, CLEAR 22, PLUS 23 ... BEEP 26.
            if (op == 11) op = A_PAUSE;
            else if (op > 11) { op += 9; if (op > 29) op++; }
        }
        else if (Version < 10)
        {
            // Commodore 64: as late, but without AUTOG/AUTOD/AUTOW/AUTOR.
            if (op >= 13) op += 4;
        }
        return op <= A_BEEP ? op : -1;
    }

    private List<QuillEntry> ReadTable(int table)
    {
        var result = new List<QuillEntry>();
        int a = table;
        while (this[a] != 0)
        {
            if (result.Count > 4000 || !IsValidAddress(a + 3)) throw new QuillFormatException("condition table not terminated");
            var e = new QuillEntry { Verb = this[a], Noun = this[a + 1], Address = a };
            int p = Ptr(a + 2, "condition entry");
            // Conditions
            int guard = 0;
            while (this[p] != 0xFF)
            {
                int op = this[p];
                if (op > 15 || ++guard > 255) throw new QuillFormatException($"invalid condition {op} at {p:X4}");
                if (op <= 12) { e.Conditions.Add(new QuillCondact(op, new int[] { this[p + 1] })); p += 2; }
                else { e.Conditions.Add(new QuillCondact(op, new int[] { this[p + 1], this[p + 2] })); p += 3; }
            }
            p++;
            guard = 0;
            while (this[p] != 0xFF)
            {
                int op = NormaliseAction(this[p]);
                if (op < 0 || ++guard > 255) throw new QuillFormatException($"invalid action {this[p]} at {p:X4}");
                int n = ActionParamCount(op, Platform);
                var args = new int[n];
                for (int k = 0; k < n; k++) args[k] = this[p + 1 + k];
                e.Actions.Add(new QuillCondact(op, args));
                p += 1 + n;
            }
            result.Add(e);
            a += 4;
        }
        return result;
    }

    // ---------------------------------------------------------------- text

    public string TextAt(int address) => TextAt(address, out _);

    /// <summary>Decodes a complemented, terminated string (0x1F on the Spectrum, 0x00 elsewhere).</summary>
    public string TextAt(int address, out int next, bool normalise = true)
    {
        var sb = new ScreenText(Platform == QuillPlatform.Spectrum ? 32 : 40);
        int term = Platform == QuillPlatform.Spectrum ? 0x1F : 0x00;
        int a = address;
        int guard = 0;
        while (true)
        {
            if (!IsValidAddress(a) || ++guard > 8192) throw new QuillFormatException($"unterminated text at {address:X4}");
            int c = 0xFF - this[a++];
            if (c == term) break;
            switch (Platform)
            {
                case QuillPlatform.Spectrum:
                    if (c >= 0x10 && c <= 0x15) { a++; break; }            // colour control + parameter
                    if (c == 0x17) { a += 2; sb.Append(' '); break; }      // TAB n
                    AppendSpectrumChar(sb, c);
                    break;
                case QuillPlatform.AmstradCpc:
                    if (c == 0x0D || c == 0x14) sb.NewLine();
                    else if (c >= 32 && c < 127) sb.Append((char)c);
                    break;
                case QuillPlatform.Commodore64:
                    c &= 0x7F;
                    if (c == 0x0D) sb.NewLine();
                    else if (c >= 'A' && c <= 'Z') sb.Append((char)(c + 32));
                    else if (c >= 32 && c < 127) sb.Append((char)c);
                    break;
            }
        }
        next = a;
        return normalise ? Normalise(sb.ToString()) : sb.ToString();
    }

    private void AppendSpectrumChar(ScreenText sb, int c)
    {
        if (c == 0x0D) sb.NewLine();
        else if (c == 0x06) sb.Tab();
        else if (c == 96) sb.Append('£');
        else if (c == 127) sb.Append('©');
        else if (c >= 32 && c < 127) sb.Append((char)c);
        else if (c > 164 && Version > 0 && Dictionary >= 0) ExpandToken(sb, c);
        else if (c >= 128 && c <= 164) sb.Append(' ', printable: false); // block graphics / UDGs occupy a cell
    }

    /// <summary>Compressed text: bytes 165-255 index the dictionary of words terminated by bit 7 (entry 0 is the 80h marker).</summary>
    private void ExpandToken(ScreenText sb, int token)
    {
        int d = Dictionary;
        int skip = token - 164;
        int guard = 0;
        while (skip > 0)
        {
            if (++guard > 4096) return;
            if ((this[d++] & 0x80) != 0) skip--;
        }
        guard = 0;
        int skipParams = 0; // colour controls inside dictionary entries take their parameters from the dictionary too
        while (++guard < 64)
        {
            int b = this[d++];
            int ch = b & 0x7F;
            if (skipParams > 0) skipParams--;
            else if (ch is >= 0x10 and <= 0x15) skipParams = 1;
            else if (ch == 0x17) { skipParams = 2; sb.Append(' '); }
            else AppendSpectrumChar(sb, ch);
            if ((b & 0x80) != 0) break;
        }
    }

    /// <summary>
    /// Collects decoded text while tracking the screen column. Quill authors laid text out for a 32 (or 40) column
    /// screen without word wrap, so a word that ends exactly at the right margin is often followed by the next word
    /// with no space; a space is inserted at such line breaks.
    /// </summary>
    private sealed class ScreenText(int width)
    {
        private readonly StringBuilder sb = new();
        private int column;
        private bool atBreak;

        public void Append(char ch, bool printable = true)
        {
            if (atBreak && ch != ' ' && sb.Length > 0 && sb[^1] is not (' ' or '-' or '\n')) sb.Append(' ');
            atBreak = false;
            if (printable) sb.Append(ch);
            if (++column >= width) { column = 0; atBreak = true; }
        }

        public void NewLine()
        {
            sb.Append('\n');
            column = 0;
            atBreak = false;
        }

        public void Tab()
        {
            sb.Append(' ');
            column = (column + 16) & ~15;
            if (column >= width) { column = 0; atBreak = true; }
        }

        public override string ToString() => sb.ToString();
    }

    /// <summary>Removes the 32/40-column layout padding: collapses runs of spaces and trims each line.</summary>
    public static string Normalise(string s)
    {
        var lines = s.Replace("\r", "").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = System.Text.RegularExpressions.Regex.Replace(lines[i], " {2,}", " ").Trim();
            if (i > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString().Trim('\n', ' ');
    }

    // ---------------------------------------------------------------- vocabulary helpers

    /// <summary>First vocabulary word with the given number (lower case), or null.</summary>
    public string? FirstWord(int number)
    {
        foreach (var (w, n) in Words) if (n == number) return w.ToLowerInvariant();
        return null;
    }

    public IEnumerable<string> Synonyms(int number) =>
        Words.Where(w => w.Number == number).Select(w => w.Word.ToLowerInvariant()).Distinct();
}
