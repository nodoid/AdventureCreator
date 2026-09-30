using System.Text;

namespace AdventureSystem.Tests.Importers;

/// <summary>
/// Builds a synthetic Graphic Adventure Creator database laid out exactly as the ZX Spectrum runtime keeps it:
/// the punctuation table at $A1E5, ten table pointers at $A51F, the start room at $A54D, the verb table at $A54F
/// and then nouns, adverbs, objects, rooms, high priority, local, low priority conditions, messages, pictures and
/// the token dictionary. Texts are compressed with GAC's word-token scheme.
/// </summary>
internal sealed class GacTestDatabaseBuilder
{
    public const int PunctuationAddress = 0xA1E5;
    public const int DatabaseAddress = 0xA51F;
    private const string Punctuation = "\0 .,-!?:";

    private readonly List<string> tokens = new();
    private readonly List<(int Id, string Word)> verbs = new(), nouns = new(), adverbs = new();
    private readonly SortedDictionary<int, string> messages = new();
    private readonly List<(int Id, int Weight, int Location, string Name)> objects = new();
    private readonly List<(int Id, int Picture, (int Verb, int Dest)[] Exits, string Description)> rooms = new();
    private readonly List<byte> high = new(), low = new();
    private readonly List<(int Room, List<byte> Code)> local = new();
    private readonly List<(int Id, int Count, List<byte> Bytes)> pictures = new();

    public int StartRoom { get; set; } = 1;

    public GacTestDatabaseBuilder Verb(int id, params string[] words) { foreach (var w in words) verbs.Add((id, w)); return this; }
    public GacTestDatabaseBuilder Noun(int id, params string[] words) { foreach (var w in words) nouns.Add((id, w)); return this; }
    public GacTestDatabaseBuilder Adverb(int id, params string[] words) { foreach (var w in words) adverbs.Add((id, w)); return this; }
    public GacTestDatabaseBuilder Message(int id, string text) { messages[id] = text; return this; }
    public GacTestDatabaseBuilder Object(int id, int weight, int location, string name) { objects.Add((id, weight, location, name)); return this; }
    public GacTestDatabaseBuilder Room(int id, int picture, string description, params (int Verb, int Dest)[] exits)
    {
        rooms.Add((id, picture, exits, description));
        return this;
    }

    /// <summary>Adds a condition line to the high priority table, written in GAC's postfix order (see <see cref="Code"/>).</summary>
    public GacTestDatabaseBuilder High(params object[] code) { high.AddRange(Code(code)); return this; }
    public GacTestDatabaseBuilder Low(params object[] code) { low.AddRange(Code(code)); return this; }
    public GacTestDatabaseBuilder Local(int room, params object[] code)
    {
        var entry = local.FindIndex(l => l.Room == room);
        if (entry < 0) { local.Add((room, new List<byte>())); entry = local.Count - 1; }
        local[entry].Code.AddRange(Code(code));
        return this;
    }

    /// <summary>Adds a Spectrum picture; commands are (name, args...) tuples, e.g. ("LINE", 0, 175, 255, 48).</summary>
    public GacTestDatabaseBuilder Picture(int id, params object[][] commands)
    {
        var bytes = new List<byte>();
        foreach (var c in commands)
        {
            var name = (string)c[0];
            int op = name switch
            {
                "BORDER" => 0x01, "PLOT" => 0x02, "ELLIPSE" => 0x03, "FILL" => 0x04, "BGFILL" => 0x05, "SHADE" => 0x06,
                "CALL" => 0x07, "RECT" => 0x08, "LINE" => 0x09, "INK" => 0x10, "PAPER" => 0x11, "BRIGHT" => 0x12, "FLASH" => 0x13,
                _ => throw new ArgumentException(name),
            };
            bytes.Add((byte)op);
            if (name == "CALL") { int n = (int)c[1]; bytes.Add((byte)n); bytes.Add((byte)(n >> 8)); }
            else foreach (var a in c.Skip(1)) bytes.Add((byte)(int)a);
        }
        pictures.Add((id, commands.Length, bytes));
        return this;
    }

    private static readonly string[] OpNames =
    {
        "ENDTABLE", "AND", "OR", "NOT", "XOR", "HOLD", "GET", "DROP", "SWAP", "TO", "OBJ", "SET", "RESE", "SET?", "RES?", "CSET",
        "CTR", "DECR", "INCR", "EQU?", "DESC", "LOOK", "MESS", "PRIN", "RAND", "<", ">", "=", "SAVE", "LOAD", "HERE", "AVAI",
        "CARR", "+", "-", "TURN", "AT", "BRIN", "FIND", "IN", "NOP", "NOP29", "OKAY", "WAIT", "QUIT", "EXIT", "ROOM", "NOUN",
        "VERB", "ADVE", "GOTO", "NO1", "NO2", "VBNO", "LIST", "PICT", "TEXT", "CONN", "WEIG", "WITH", "STRE", "LF", "IF", "END",
    };

    /// <summary>Assembles postfix bytecode: ints are 15-bit constants (two bytes, high bit set), strings are opcodes.</summary>
    public static List<byte> Code(params object[] items)
    {
        var bytes = new List<byte>();
        foreach (var item in items)
        {
            if (item is int n)
            {
                bytes.Add((byte)(0x80 | (n >> 8)));
                bytes.Add((byte)n);
            }
            else
            {
                int op = Array.IndexOf(OpNames, (string)item);
                if (op <= 0) throw new ArgumentException($"Unknown GAC opcode {item}");
                bytes.Add((byte)op);
            }
        }
        return bytes;
    }

    // ---- text compression -------------------------------------------------------------------------------------

    private int Token(string word)
    {
        var upper = word.ToUpperInvariant();
        int i = tokens.IndexOf(upper);
        if (i < 0) { tokens.Add(upper); i = tokens.Count - 1; }
        return i;
    }

    private static int PunctIndex(char c) => c == '\0' ? -1 : Punctuation.IndexOf(c);

    /// <summary>Encodes text as GAC word tokens: [case:2][punctuation:3][token:11], punctuation runs as case 3.</summary>
    public List<byte> EncodeText(string text)
    {
        var words = new List<int>();
        int i = 0;
        bool terminated = false;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\'')) i++;
                var word = text[start..i];
                int mode = word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)) && word.Any(char.IsLetter) && word.Length > 1 ? 2
                    : char.IsUpper(word[0]) ? 0 : 1;
                int punct = 0;
                if (i < text.Length)
                {
                    punct = PunctIndex(text[i]);
                    if (punct <= 0) throw new ArgumentException($"Character '{text[i]}' cannot follow a word in GAC text");
                    i++;
                }
                else terminated = true;
                words.Add((mode << 14) | (punct << 11) | Token(word));
            }
            else
            {
                int p = PunctIndex(c);
                if (p <= 0) throw new ArgumentException($"Character '{c}' is not GAC punctuation");
                int count = 0;
                while (i < text.Length && text[i] == c) { i++; count++; }
                words.Add(0xC000 | (p << 11) | count);
            }
        }
        if (!terminated) words.Add(0xC000);
        var bytes = new List<byte>();
        foreach (var w in words) { bytes.Add((byte)w); bytes.Add((byte)(w >> 8)); }
        return bytes;
    }

    // ---- layout -----------------------------------------------------------------------------------------------

    /// <summary>The database from $A51F onwards (what the GAC editor saves as a data file).</summary>
    public byte[] BuildDatabase()
    {
        // Encode all texts first so that the token dictionary is complete.
        var verbEntries = verbs.Select(v => (v.Id, Token(v.Word))).ToList();
        var nounEntries = nouns.Select(v => (v.Id, Token(v.Word))).ToList();
        var adverbEntries = adverbs.Select(v => (v.Id, Token(v.Word))).ToList();
        var messageBytes = messages.Select(m => (m.Key, EncodeText(m.Value))).ToList();
        var objectBytes = objects.Select(o => (o, EncodeText(o.Name))).ToList();
        var roomBytes = rooms.Select(r => (r, EncodeText(r.Description))).ToList();

        var mem = new List<byte>(new byte[0xA54F - DatabaseAddress]);
        var pointers = new int[10];
        void Pointer(int index) => pointers[index] = DatabaseAddress + mem.Count;
        void Word(int w) { mem.Add((byte)w); mem.Add((byte)(w >> 8)); }

        void Words(List<(int Id, int Token)> list)
        {
            foreach (var (id, token) in list) { mem.Add((byte)id); Word(0x8000 | token); }
            mem.Add(0);
        }

        Words(verbEntries);
        Pointer(0); Words(nounEntries);
        Pointer(1); Words(adverbEntries);
        Pointer(2);
        foreach (var (o, text) in objectBytes)
        {
            mem.Add((byte)o.Id);
            mem.Add((byte)(3 + text.Count));
            mem.Add((byte)o.Weight);
            Word(o.Location);
            mem.AddRange(text);
        }
        mem.Add(0);
        Pointer(3);
        foreach (var (r, text) in roomBytes)
        {
            Word(r.Id);
            Word(2 + 3 * r.Exits.Length + 1 + text.Count);
            Word(r.Picture);
            foreach (var (verb, dest) in r.Exits) { mem.Add((byte)verb); Word(dest); }
            mem.Add(0);
            mem.AddRange(text);
        }
        Word(0);
        Pointer(4); mem.AddRange(high); mem.Add(0);
        Pointer(5);
        foreach (var (room, code) in local) { Word(room); mem.AddRange(code); mem.Add(0); }
        Word(0);
        Pointer(6); mem.AddRange(low); mem.Add(0);
        Pointer(7);
        foreach (var (id, text) in messageBytes) { mem.Add((byte)id); mem.Add((byte)text.Count); mem.AddRange(text); }
        mem.Add(0);
        Pointer(8);
        foreach (var (id, count, bytes) in pictures) { Word(id); Word(4 + 1 + bytes.Count); mem.Add((byte)count); mem.AddRange(bytes); }
        Word(0);
        Pointer(9);
        foreach (var t in tokens)
        {
            mem.Add((byte)t.Length);
            for (int k = 0; k < t.Length; k++) mem.Add((byte)(t[k] | (k == t.Length - 1 ? 0x80 : 0)));
        }
        mem.Add(0);

        var result = mem.ToArray();
        for (int k = 0; k < 10; k++) { result[2 * k] = (byte)pointers[k]; result[2 * k + 1] = (byte)(pointers[k] >> 8); }
        result[0xA54D - DatabaseAddress] = (byte)StartRoom;
        result[0xA54E - DatabaseAddress] = (byte)(StartRoom >> 8);
        return result;
    }

    /// <summary>A 48K .SNA snapshot with the database and the runtime's punctuation table in place.</summary>
    public byte[] BuildSna()
    {
        var db = BuildDatabase();
        var sna = new byte[27 + 49152];
        int Offset(int address) => 27 + address - 0x4000;
        Encoding.ASCII.GetBytes(Punctuation).CopyTo(sna, Offset(PunctuationAddress));
        db.CopyTo(sna, Offset(DatabaseAddress));
        return sna;
    }

    /// <summary>A .TAP holding the database as a GAC data file (CODE block loaded at 42271 = $A51F).</summary>
    public byte[] BuildDataFileTap(string name = "TEST")
    {
        var db = BuildDatabase();
        var header = new byte[19];
        header[0] = 0x00;
        header[1] = 3;
        var n = Encoding.ASCII.GetBytes(name.PadRight(10));
        Array.Copy(n, 0, header, 2, 10);
        header[12] = (byte)db.Length; header[13] = (byte)(db.Length >> 8);
        header[14] = DatabaseAddress & 0xFF; header[15] = DatabaseAddress >> 8;
        header[16] = 0x20; header[17] = 0x20;
        header[18] = Checksum(header, 18);
        var body = new byte[db.Length + 2];
        body[0] = 0xFF;
        db.CopyTo(body, 1);
        body[^1] = Checksum(body, body.Length - 1);
        var tap = new List<byte>();
        foreach (var block in new[] { header, body })
        {
            tap.Add((byte)block.Length); tap.Add((byte)(block.Length >> 8));
            tap.AddRange(block);
        }
        return tap.ToArray();
    }

    private static byte Checksum(byte[] data, int count)
    {
        byte x = 0;
        for (int i = 0; i < count; i++) x ^= data[i];
        return x;
    }
}
