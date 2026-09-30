using System.Text;

namespace AdventureSystem.Importers.Gac;

/// <summary>Which GAC interpreter a memory image came from.</summary>
internal enum GacMachine
{
    Spectrum,
    AmstradCpc,
}

/// <summary>
/// Where a GAC interpreter keeps the pointers to its tables. The database itself has the same shape on every
/// machine (the interpreter was ported from one machine to the next); only the addresses of the pointer table,
/// the fixed verb table and the punctuation table differ, and the pictures use each machine's own opcodes.
/// </summary>
internal sealed class GacLayout
{
    public GacMachine Machine { get; init; }
    /// <summary>The 8-byte punctuation table "\0 .,-!?:" (part of the interpreter, not of a saved data file).</summary>
    public int Punctuation { get; init; }
    public int Nouns { get; init; }
    public int Adverbs { get; init; }
    public int Objects { get; init; }
    public int Rooms { get; init; }
    public int HighPriority { get; init; }
    public int Local { get; init; }
    public int LowPriority { get; init; }
    public int Messages { get; init; }
    public int Graphics { get; init; }
    public int Tokens { get; init; }
    public int StartRoom { get; init; }
    /// <summary>The verb table is not reached through a pointer: it starts at a fixed address.</summary>
    public int Verbs { get; init; }

    public int[] PointerAddresses => new[] { Nouns, Adverbs, Objects, Rooms, HighPriority, Local, LowPriority, Messages, Graphics, Tokens };

    /// <summary>
    /// ZX Spectrum: the runtime interpreter keeps the punctuation at $A1E5 and the database (a saved GAC data file,
    /// which the editor saves as CODE 42271 = $A51F) begins with the ten table pointers at $A51F, the start room at
    /// $A54D and the verb table at $A54F. <paramref name="shift"/> relocates everything (for non-standard builds).
    /// </summary>
    public static GacLayout Spectrum(int shift = 0) => new()
    {
        Machine = GacMachine.Spectrum,
        Punctuation = 0xA1E5 + shift,
        Nouns = 0xA51F + shift,
        Adverbs = 0xA521 + shift,
        Objects = 0xA523 + shift,
        Rooms = 0xA525 + shift,
        HighPriority = 0xA527 + shift,
        Local = 0xA529 + shift,
        LowPriority = 0xA52B + shift,
        Messages = 0xA52D + shift,
        Graphics = 0xA52F + shift,
        Tokens = 0xA531 + shift,
        StartRoom = 0xA54D + shift,
        Verbs = 0xA54F + shift,
    };

    /// <summary>Amstrad CPC (layout as documented by the reGAC project; not verified here against a real game).</summary>
    public static readonly GacLayout AmstradCpc = new()
    {
        Machine = GacMachine.AmstradCpc,
        Punctuation = 0x210C,
        Nouns = 0x4000,
        Adverbs = 0x4002,
        Objects = 0x4004,
        Rooms = 0x4006,
        HighPriority = 0x4008,
        Local = 0x400A,
        LowPriority = 0x400C,
        Messages = 0x400E,
        Graphics = 0x4012,
        Tokens = 0x4014,
        StartRoom = 0x4018,
        Verbs = 0x4100,
    };

    public static readonly byte[] PunctuationMagic = { 0, (byte)' ', (byte)'.', (byte)',', (byte)'-', (byte)'!', (byte)'?', (byte)':' };

    public bool HasPunctuationMagic(byte[] mem)
    {
        for (int i = 0; i < PunctuationMagic.Length; i++)
            if (mem[(Punctuation + i) & 0xFFFF] != PunctuationMagic[i]) return false;
        return true;
    }

    /// <summary>The ten table pointers must climb through memory (each table follows the previous one).</summary>
    public bool PointersLookRight(byte[] mem, int lowerBound)
    {
        int last = lowerBound;
        foreach (var at in PointerAddresses)
        {
            int p = mem[at & 0xFFFF] | (mem[(at + 1) & 0xFFFF] << 8);
            if (p <= last || p > 0xFFFF) return false;
            last = p;
        }
        return true;
    }
}

internal readonly record struct GacWord(string Text, int Number);

internal sealed record GacObject(int Number, int Weight, int Location, string Name);

internal sealed class GacRoom
{
    public int Number { get; init; }
    public int Picture { get; init; }
    public List<(int Verb, int Destination)> Exits { get; } = new();
    public string Description { get; set; } = "";
}

/// <summary>One item of condition bytecode: a 15-bit constant or an opcode (0..0x3F).</summary>
internal readonly record struct GacCode(bool IsConstant, int Value)
{
    public override string ToString() => IsConstant ? Value.ToString() : GacOps.Name(Value);
}

internal sealed record GacGfxCommand(int Opcode, int[] Args);

internal sealed class GacPictureData
{
    public int Number { get; init; }
    /// <summary>Amstrad only: the 8 ink bytes (four pairs, one per pen) at the head of each picture.</summary>
    public byte[]? Inks { get; init; }
    public List<GacGfxCommand> Commands { get; } = new();
}

/// <summary>The raw contents of a GAC database, as read from memory.</summary>
internal sealed class GacDatabase
{
    public GacMachine Machine { get; init; }
    public int StartRoom { get; set; }
    public List<GacWord> Verbs { get; } = new();
    public List<GacWord> Nouns { get; } = new();
    public List<GacWord> Adverbs { get; } = new();
    public SortedDictionary<int, string> Messages { get; } = new();
    public SortedDictionary<int, GacObject> Objects { get; } = new();
    public SortedDictionary<int, GacRoom> Rooms { get; } = new();
    public List<GacCode> HighPriority { get; } = new();
    public List<GacCode> LowPriority { get; } = new();
    /// <summary>Local condition tables in database order: (room number, code).</summary>
    public List<(int Room, List<GacCode> Code)> Local { get; } = new();
    public SortedDictionary<int, GacPictureData> Pictures { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>Names of the 64 GAC condition opcodes.</summary>
internal static class GacOps
{
    public const int And = 0x01, Or = 0x02, Not = 0x03, Xor = 0x04, Hold = 0x05, Get = 0x06, Drop = 0x07, Swap = 0x08,
        To = 0x09, Obj = 0x0A, Set = 0x0B, Rese = 0x0C, SetQ = 0x0D, ResQ = 0x0E, Cset = 0x0F, Ctr = 0x10, Decr = 0x11,
        Incr = 0x12, EquQ = 0x13, Desc = 0x14, Look = 0x15, Mess = 0x16, Prin = 0x17, Rand = 0x18, Lt = 0x19, Gt = 0x1A,
        Eq = 0x1B, Save = 0x1C, Load = 0x1D, Here = 0x1E, Avai = 0x1F, Carr = 0x20, Plus = 0x21, Minus = 0x22,
        Turn = 0x23, At = 0x24, Brin = 0x25, Find = 0x26, In = 0x27, Nop28 = 0x28, Nop29 = 0x29, Okay = 0x2A,
        Wait = 0x2B, Quit = 0x2C, Exit = 0x2D, Room = 0x2E, Noun = 0x2F, Verb = 0x30, Adve = 0x31, Goto = 0x32,
        No1 = 0x33, No2 = 0x34, Vbno = 0x35, List = 0x36, Pict = 0x37, Text = 0x38, Conn = 0x39, Weig = 0x3A,
        With = 0x3B, Stre = 0x3C, Lf = 0x3D, If = 0x3E, End = 0x3F;

    private static readonly string[] Names =
    {
        "ENDTABLE", "AND", "OR", "NOT", "XOR", "HOLD", "GET", "DROP", "SWAP", "TO", "OBJ", "SET", "RESE", "SET?", "RES?", "CSET",
        "CTR", "DECR", "INCR", "EQU?", "DESC", "LOOK", "MESS", "PRIN", "RAND", "<", ">", "=", "SAVE", "LOAD", "HERE", "AVAI",
        "CARR", "+", "-", "TURN", "AT", "BRIN", "FIND", "IN", "NOP", "NOP", "OKAY", "WAIT", "QUIT", "EXIT", "ROOM", "NOUN",
        "VERB", "ADVE", "GOTO", "NO1", "NO2", "VBNO", "LIST", "PICT", "TEXT", "CONN", "WEIG", "WITH", "STRE", "LF", "IF", "END",
    };

    public static string Name(int op) => op >= 0 && op < Names.Length ? Names[op] : $"OP{op:X2}";

    /// <summary>Opcodes written between their two operands (x OP y).</summary>
    public static bool IsInfix(int op) => op is And or Or or Xor or Swap or To or Cset or EquQ or Lt or Gt or Eq or Plus or Minus or In;

    /// <summary>Opcodes that take one operand from the stack.</summary>
    public static bool IsPrefix(int op) => op is Not or Hold or Get or Drop or Obj or Set or Rese or SetQ or ResQ or Ctr or Decr
        or Incr or Desc or Mess or Prin or Rand or Here or Avai or Carr or At or Brin or Find or Noun or Verb or Adve or Goto
        or List or Conn or Weig or Stre;

    /// <summary>Opcodes that leave a value on the stack.</summary>
    public static bool Pushes(int op) => op is And or Or or Not or Xor or SetQ or ResQ or Ctr or EquQ or Rand or Lt or Gt or Eq
        or Here or Avai or Carr or Plus or Minus or Turn or At or In or Room or Noun or Verb or Adve or No1 or No2 or Vbno
        or Conn or Weig or With;
}

/// <summary>Reads a GAC database out of a 64K memory image.</summary>
internal sealed class GacReader
{
    private const int MaxEntries = 8192;
    private readonly byte[] mem;
    private readonly GacLayout layout;
    private readonly byte[] punctuation;

    public GacReader(byte[] memory, GacLayout layout, byte[]? punctuation = null)
    {
        mem = memory;
        this.layout = layout;
        this.punctuation = punctuation ?? (layout.HasPunctuationMagic(memory)
            ? Enumerable.Range(0, 8).Select(i => memory[(layout.Punctuation + i) & 0xFFFF]).ToArray()
            : GacLayout.PunctuationMagic);
    }

    private int B(int a) => mem[a & 0xFFFF];
    private int W(int a) => mem[a & 0xFFFF] | (mem[(a + 1) & 0xFFFF] << 8);

    public GacDatabase Read()
    {
        var db = new GacDatabase { Machine = layout.Machine, StartRoom = W(layout.StartRoom) };
        ReadWords(layout.Verbs, db.Verbs);
        ReadWords(W(layout.Nouns), db.Nouns);
        ReadWords(W(layout.Adverbs), db.Adverbs);
        ReadMessages(db);
        ReadObjects(db);
        ReadRooms(db);
        db.HighPriority.AddRange(ReadCode(W(layout.HighPriority), out _));
        db.LowPriority.AddRange(ReadCode(W(layout.LowPriority), out _));
        ReadLocal(db);
        if (layout.Machine == GacMachine.Spectrum) ReadSpectrumGraphics(db);
        else ReadAmstradGraphics(db);

        var words = db.Verbs.Concat(db.Nouns).Concat(db.Adverbs).ToList();
        int bad = words.Count(w => w.Text.Length == 0 || !w.Text.All(c => char.IsLetterOrDigit(c) || c is '\'' or '-' or '.'));
        if (words.Count > 0 && bad * 2 > words.Count)
            db.Warnings.Add("The word dictionary could not be decoded (unrecognised or protected format): vocabulary and texts will be garbled.");
        return db;
    }

    // ---- text -------------------------------------------------------------------------------------------------

    /// <summary>Address of the characters of dictionary token <paramref name="token"/> (entries are [len][chars]).</summary>
    private int TokenAddress(int token)
    {
        int a = W(layout.Tokens);
        for (int i = 0; i < token; i++) a += B(a) + 1;
        return a + 1;
    }

    private string TokenText(int token, int caseMode)
    {
        var sb = new StringBuilder();
        int a = TokenAddress(token);
        for (int n = 0; n < 64; n++, a++)
        {
            int c = B(a);
            int ch = c & 0x7F;
            // Mode 0: first letter as stored (capital), then lower case; mode 1: lower case; mode 2: as stored (capitals).
            bool lower = caseMode == 1 || (caseMode == 0 && n > 0);
            if (lower && (ch & 0x40) != 0) ch |= 0x20;
            sb.Append(ch is >= 32 and < 127 ? (char)ch : ' ');
            if ((c & 0x80) != 0) break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Decodes GAC compressed text: a sequence of 16-bit little-endian words. Bits 15-14 select the case of a
    /// dictionary word (0 capitalised, 1 lower, 2 upper) or, when 3, a run of punctuation (low byte = count);
    /// bits 13-11 index the punctuation table (the character following the word; index 0 = end of text);
    /// bits 10-0 are the dictionary token number.
    /// </summary>
    public string DecodeText(int addr, int length)
    {
        var sb = new StringBuilder();
        for (int n = 0; n + 1 < length; n += 2)
        {
            int w = W(addr + n);
            int top = (w >> 14) & 3;
            int p = punctuation[(w >> 11) & 7];
            if (top == 3)
            {
                if (p == 0) break;
                sb.Append((char)p, w & 0xFF);
            }
            else
            {
                sb.Append(TokenText(w & 0x7FF, top));
                if (p == 0) break;
                sb.Append((char)p);
            }
        }
        return sb.ToString();
    }

    // ---- tables -----------------------------------------------------------------------------------------------

    private void ReadWords(int addr, List<GacWord> into)
    {
        for (int i = 0; i < MaxEntries; i++)
        {
            int id = B(addr);
            if (id == 0) return;
            into.Add(new GacWord(TokenText(W(addr + 1) & 0x7FF, 2).Trim(), id));
            addr += 3;
        }
    }

    private void ReadMessages(GacDatabase db)
    {
        int a = W(layout.Messages);
        for (int i = 0; i < MaxEntries; i++)
        {
            int id = B(a);
            if (id == 0) return;
            int len = B(a + 1);
            db.Messages[id] = DecodeText(a + 2, len);
            a += 2 + len;
        }
    }

    private void ReadObjects(GacDatabase db)
    {
        int a = W(layout.Objects);
        for (int i = 0; i < MaxEntries; i++)
        {
            int id = B(a);
            if (id == 0) return;
            int len = B(a + 1);
            a += 2;
            db.Objects[id] = new GacObject(id, B(a), W(a + 1), len > 3 ? DecodeText(a + 3, len - 3) : "");
            a += len;
        }
    }

    private void ReadRooms(GacDatabase db)
    {
        int a = W(layout.Rooms);
        for (int i = 0; i < MaxEntries; i++)
        {
            int id = W(a);
            if (id == 0) return;
            int len = W(a + 2);
            a += 4;
            int start = a;
            var room = new GacRoom { Number = id, Picture = W(a) };
            a += 2;
            for (int e = 0; e < 256 && B(a) != 0 && a - start < len; e++)
            {
                room.Exits.Add((B(a), W(a + 1)));
                a += 3;
            }
            a++;
            room.Description = DecodeText(a, len - (a - start));
            db.Rooms[id] = room;
            a = start + len;
        }
    }

    /// <summary>Reads condition bytecode up to the end-of-table byte (0).</summary>
    private List<GacCode> ReadCode(int addr, out int next)
    {
        var list = new List<GacCode>();
        for (int i = 0; i < 65536; i++)
        {
            int b = B(addr);
            if (b == 0) { addr++; break; }
            if ((b & 0x80) != 0)
            {
                list.Add(new GacCode(true, ((b & 0x7F) << 8) | B(addr + 1)));
                addr += 2;
            }
            else
            {
                addr++;
                int op = b & 0x3F;
                if (op == 0) break;
                list.Add(new GacCode(false, op));
            }
        }
        next = addr;
        return list;
    }

    private void ReadLocal(GacDatabase db)
    {
        int a = W(layout.Local);
        for (int i = 0; i < MaxEntries; i++)
        {
            int room = W(a);
            if (room == 0) return;
            var code = ReadCode(a + 2, out a);
            db.Local.Add((room, code));
        }
    }

    // ---- pictures ---------------------------------------------------------------------------------------------

    /// <summary>Spectrum picture records: [id w][length w, including this 4-byte header][count b][commands].</summary>
    private void ReadSpectrumGraphics(GacDatabase db)
    {
        int a = W(layout.Graphics);
        for (int i = 0; i < MaxEntries; i++)
        {
            int id = W(a);
            if (id == 0) return;
            int len = W(a + 2);
            if (len <= 4) return;
            int start = a + 4;
            int end = a + len;
            var pic = new GacPictureData { Number = id };
            int count = B(start);
            int p = start + 1;
            for (int n = 0; n < count && p < end; n++)
            {
                int cmd = B(p++);
                int argc = cmd switch
                {
                    0x01 => 1, 0x02 => 2, 0x03 => 4, 0x04 => 2, 0x05 => 2, 0x06 => 2, 0x07 => 2, 0x08 => 4, 0x09 => 4,
                    0x10 => 1, 0x11 => 1, 0x12 => 1, 0x13 => 1,
                    _ => -1,
                };
                if (argc < 0)
                {
                    db.Warnings.Add($"Picture {id}: unknown drawing command 0x{cmd:X2}; rest of picture ignored.");
                    break;
                }
                var args = new int[argc];
                for (int k = 0; k < argc; k++) args[k] = B(p + k);
                p += argc;
                pic.Commands.Add(new GacGfxCommand(cmd, args));
            }
            db.Pictures[id] = pic;
            a = end;
        }
    }

    /// <summary>Amstrad picture records: [id w][length w, excluding header][8 ink bytes][commands..., 0].</summary>
    private void ReadAmstradGraphics(GacDatabase db)
    {
        int a = W(layout.Graphics);
        for (int i = 0; i < MaxEntries; i++)
        {
            int id = W(a);
            if (id == 0) return;
            int len = W(a + 2);
            if (len <= 8) return;
            int p = a + 4;
            int end = p + len;
            var inks = new byte[8];
            for (int k = 0; k < 8; k++) inks[k] = (byte)B(p + k);
            p += 8;
            var pic = new GacPictureData { Number = id, Inks = inks };
            while (p < end)
            {
                int cmd = B(p++);
                if (cmd == 0) break;
                int argc = cmd switch { 0x01 or 0x02 or 0x08 => 4, 0x03 or 0x09 or 0x0A or 0x0B => 2, _ => 0 };
                var args = new int[argc];
                for (int k = 0; k < argc; k++) args[k] = B(p + k);
                p += argc;
                pic.Commands.Add(new GacGfxCommand(cmd, args));
            }
            db.Pictures[id] = pic;
            a = end;
        }
    }
}
