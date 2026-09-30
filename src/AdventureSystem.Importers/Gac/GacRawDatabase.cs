using System.Text;

namespace AdventureSystem.Importers.Gac;

/// <summary>
/// A GAC database held as the bytes of its entries, so that a game can be written back with everything the
/// importer didn't understand left exactly as it was. Walks the tables the same way <see cref="GacReader"/> does,
/// keeping each entry's bytes; <see cref="Serialize"/> lays the tables out again one after the other.
/// </summary>
internal sealed class GacRawDatabase
{
    private readonly byte[] mem;
    public GacLayout Layout { get; }
    public byte[] Punctuation { get; }

    /// <summary>Word entries: [id][token word] (3 bytes).</summary>
    public List<byte[]> Verbs { get; } = new();
    public List<byte[]> Nouns { get; } = new();
    public List<byte[]> Adverbs { get; } = new();
    /// <summary>Whole entries, header included, keyed by number (objects and messages: [id][len][..]; rooms: [id w][len w][..]).</summary>
    public List<(int Id, byte[] Bytes)> Objects { get; } = new();
    public List<(int Id, byte[] Bytes)> Rooms { get; } = new();
    public List<(int Id, byte[] Bytes)> Messages { get; } = new();
    public List<(int Id, byte[] Bytes)> Pictures { get; } = new();
    /// <summary>Condition tables split into lines, each ending with END (a last line may lack it).</summary>
    public List<byte[]> High { get; } = new();
    public List<byte[]> Low { get; } = new();
    public List<(int Room, List<byte[]> Lines)> Local { get; } = new();
    /// <summary>Dictionary entries: [len][chars, the last with bit 7 set].</summary>
    public List<byte[]> Tokens { get; } = new();
    public int StartRoom { get; set; }

    /// <summary>First byte of the database (the pointer table) and one past its last byte.</summary>
    public int Start => Layout.Nouns;
    public int End { get; private set; }

    private readonly Dictionary<string, int> tokenIndex = new();
    private readonly int limit;
    /// <summary>The highest token number any text or word uses (some games use numbers past their dictionary).</summary>
    private int maxReferenced;
    /// <summary>Texts use words the dictionary doesn't have (a truncated data file); new words had to be numbered past them.</summary>
    public bool IncompleteDictionary { get; private set; }

    private int B(int a) => mem[a & 0xFFFF];
    private int W(int a) => mem[a & 0xFFFF] | (mem[(a + 1) & 0xFFFF] << 8);

    /// <param name="limit">One past the last byte that belongs to the database (a data file's end), if known.</param>
    public GacRawDatabase(byte[] memory, GacLayout layout, byte[]? punctuation, int limit = 0x10000)
    {
        this.limit = limit;
        mem = memory;
        Layout = layout;
        Punctuation = punctuation ?? (layout.HasPunctuationMagic(memory)
            ? Enumerable.Range(0, 8).Select(i => memory[(layout.Punctuation + i) & 0xFFFF]).ToArray()
            : GacLayout.PunctuationMagic);
        StartRoom = W(layout.StartRoom);
        var ends = new int[10];
        ends[0] = Words(layout.Verbs, Verbs);
        ends[1] = Words(W(layout.Nouns), Nouns);
        ends[2] = Words(W(layout.Adverbs), Adverbs);
        ends[3] = Entries(W(layout.Objects), Objects);
        ends[4] = RoomEntries();
        High.AddRange(Lines(W(layout.HighPriority), out ends[5]));
        ends[6] = LocalEntries();
        Low.AddRange(Lines(W(layout.LowPriority), out ends[7]));
        ends[8] = Entries(W(layout.Messages), Messages);
        ends[9] = PictureEntries();
        TokenEntries();
        // What lies between the end of each table and the start of the next: normally the two-byte terminator.
        var next = layout.PointerAddresses.Select(W).ToArray();
        for (int k = 0; k < 10; k++)
            trailers[k] = next[k] >= ends[k] + 1 && next[k] - ends[k] < 0x4000 ? Slice(ends[k], next[k] - ends[k]) : new byte[2];
    }

    /// <summary>The bytes after each table (verbs … pictures) up to the next one, kept so that a rebuild is exact.</summary>
    private readonly byte[][] trailers = new byte[10][];

    // ---- reading ----------------------------------------------------------------------------------------------

    private byte[] Slice(int a, int n) => Enumerable.Range(0, n).Select(i => mem[(a + i) & 0xFFFF]).ToArray();

    private int Words(int a, List<byte[]> into)
    {
        for (int i = 0; i < 8192 && B(a) != 0; i++, a += 3) into.Add(Slice(a, 3));
        return a;
    }

    private int Entries(int a, List<(int, byte[])> into)
    {
        for (int i = 0; i < 8192 && B(a) != 0; i++)
        {
            int len = B(a + 1);
            into.Add((B(a), Slice(a, 2 + len)));
            a += 2 + len;
        }
        return a;
    }

    private int RoomEntries()
    {
        int a = W(Layout.Rooms);
        for (int i = 0; i < 8192 && W(a) != 0; i++)
        {
            int len = W(a + 2);
            Rooms.Add((W(a), Slice(a, 4 + len)));
            a += 4 + len;
        }
        return a;
    }

    /// <summary>A condition table's lines; <paramref name="end"/> is the address of its terminating 0.</summary>
    private List<byte[]> Lines(int a, out int end)
    {
        var lines = new List<byte[]>();
        var line = new List<byte>();
        for (int i = 0; i < 65536; i++)
        {
            int b = B(a);
            if ((b & 0x80) != 0)
            {
                line.Add((byte)b); line.Add((byte)B(a + 1));
                a += 2;
                continue;
            }
            if ((b & 0x3F) == 0) break;
            a++;
            line.Add((byte)b);
            if ((b & 0x3F) == GacOps.End) { lines.Add(line.ToArray()); line.Clear(); }
        }
        if (line.Count > 0) lines.Add(line.ToArray());
        end = a;
        return lines;
    }

    private int LocalEntries()
    {
        int a = W(Layout.Local);
        for (int i = 0; i < 8192 && W(a) != 0; i++)
        {
            int room = W(a);
            Local.Add((room, Lines(a + 2, out a)));
            a++;
        }
        return a;
    }

    private int PictureEntries()
    {
        int a = W(Layout.Graphics);
        bool spectrum = Layout.Machine == GacMachine.Spectrum;
        int limit = W(Layout.Tokens);
        for (int i = 0; i < 8192 && W(a) != 0; i++)
        {
            int len = W(a + 2);
            if (len <= (spectrum ? 4 : 8)) break;
            int size = spectrum ? len : 4 + len;
            if (a + size > limit) break;   // runs into the dictionary: not a picture
            Pictures.Add((W(a), Slice(a, size)));
            a += size;
        }
        return a;
    }

    /// <summary>The dictionary: every token a text or word uses, and any well-formed entries after them.</summary>
    private void TokenEntries()
    {
        int max = -1;
        foreach (var w in Verbs.Concat(Nouns).Concat(Adverbs)) max = Math.Max(max, (w[1] | (w[2] << 8)) & 0x7FF);
        foreach (var (_, e) in Objects) max = Math.Max(max, MaxToken(e, 5));
        foreach (var (_, e) in Messages) max = Math.Max(max, MaxToken(e, 2));
        foreach (var (_, e) in Rooms) max = Math.Max(max, MaxToken(e, RoomTextOffset(e)));

        maxReferenced = max;
        int a = W(Layout.Tokens);
        for (int i = 0; i < 2048; i++)
        {
            int len = B(a);
            if (a + 1 + len > limit) break;
            if (i > max && !WellFormedToken(a)) break;
            if (len == 0 && i > max) break;
            var entry = Slice(a, 1 + len);
            tokenIndex.TryAdd(TokenString(entry), Tokens.Count);
            Tokens.Add(entry);
            a += 1 + len;
        }
        End = a;
    }

    private bool WellFormedToken(int a)
    {
        int len = B(a);
        if (len is < 1 or > 32) return false;
        for (int k = 0; k < len; k++)
        {
            int c = B(a + 1 + k);
            bool last = k == len - 1;
            if (((c & 0x80) != 0) != last || (c & 0x7F) < 32) return false;
        }
        return true;
    }

    internal static int RoomTextOffset(byte[] e)
    {
        int len = e[2] | (e[3] << 8);
        int p = 6;
        while (p < e.Length && e[p] != 0 && p - 4 < len) p += 3;
        return p + 1;
    }

    private static int MaxToken(byte[] e, int offset)
    {
        int max = -1;
        for (int n = offset; n + 1 < e.Length; n += 2)
        {
            int w = e[n] | (e[n + 1] << 8);
            if ((w >> 14) == 3) { if ((w >> 11 & 7) == 0) break; continue; }
            max = Math.Max(max, w & 0x7FF);
            if ((w >> 11 & 7) == 0) break;
        }
        return max;
    }

    private static string TokenString(byte[] entry) =>
        new(entry.Skip(1).Select(c => (char)(c & 0x7F)).ToArray());

    /// <summary>The free bytes after the database: a run of zeros, below the UDGs at $FF58.</summary>
    public int FreeLimit()
    {
        int a = End;
        while (a < 0xFF58 && mem[a] == 0) a++;
        return a;
    }

    // ---- text -------------------------------------------------------------------------------------------------

    private const string Separators = " .,-!?:";

    private int PunctIndex(char c)
    {
        for (int i = 1; i < 8; i++) if (Punctuation[i] == c) return i;
        return -1;
    }

    private int Token(string stored)
    {
        if (tokenIndex.TryGetValue(stored, out var i)) return i;
        // New tokens go after every number already in use, so no old reference starts meaning a new word.
        if (Tokens.Count <= maxReferenced) IncompleteDictionary = true;
        while (Tokens.Count <= maxReferenced) Tokens.Add(new byte[] { 1, 0xA0 });
        if (Tokens.Count >= 2048) throw new InvalidOperationException("The GAC dictionary is full (2048 words).");
        var entry = new byte[stored.Length + 1];
        entry[0] = (byte)stored.Length;
        for (int k = 0; k < stored.Length; k++) entry[k + 1] = (byte)(stored[k] | (k == stored.Length - 1 ? 0x80 : 0));
        tokenIndex[stored] = Tokens.Count;
        Tokens.Add(entry);
        return Tokens.Count - 1;
    }

    /// <summary>A word as (case mode, stored token text): see <see cref="GacReader.DecodeText"/>.</summary>
    private static (int Mode, string Stored) CaseOf(string word)
    {
        var upper = word.ToUpperInvariant();
        bool lowersSafely = upper.Skip(1).All(ch => (ch & 0x40) == 0 || char.IsLetter(ch));
        bool firstSafe = (upper[0] & 0x40) == 0 || char.IsLetter(upper[0]);
        if (word == upper) return (2, upper);
        if (lowersSafely && firstSafe && word == upper.ToLowerInvariant()) return (1, upper);
        if (lowersSafely && char.IsUpper(word[0]) && word[1..] == upper[1..].ToLowerInvariant()) return (0, upper);
        return (2, word);
    }

    /// <summary>Text → GAC word stream: tokens each followed by one punctuation character, runs of punctuation as case 3.</summary>
    public byte[] EncodeText(string text, Action<string>? warn = null)
    {
        var clean = new StringBuilder();
        foreach (var c in text.Replace("\r", "").Replace('\n', ' ').Replace('\t', ' '))
        {
            if (c is >= ' ' and < (char)127) clean.Append(c);
            else { clean.Append('?'); warn?.Invoke("Characters outside ASCII were replaced by \"?\" (GAC texts are 7-bit)"); }
        }
        var s = clean.ToString();
        var words = new List<int>();
        bool terminated = false;
        int i = 0;
        while (i < s.Length)
        {
            if (Separators.IndexOf(s[i]) < 0 || PunctIndex(s[i]) < 0)
            {
                int start = i;
                while (i < s.Length && (Separators.IndexOf(s[i]) < 0 || PunctIndex(s[i]) < 0)) i++;
                var (mode, stored) = CaseOf(s[start..i]);
                int punct = 0;
                if (i < s.Length) punct = PunctIndex(s[i++]);
                else terminated = true;
                words.Add((mode << 14) | (punct << 11) | Token(stored));
            }
            else
            {
                char c = s[i];
                int count = 0;
                while (i < s.Length && s[i] == c && count < 255) { i++; count++; }
                words.Add(0xC000 | (PunctIndex(c) << 11) | count);
            }
        }
        if (!terminated) words.Add(0xC000);
        var bytes = new byte[words.Count * 2];
        for (int k = 0; k < words.Count; k++) { bytes[2 * k] = (byte)words[k]; bytes[2 * k + 1] = (byte)(words[k] >> 8); }
        return bytes;
    }

    /// <summary>A vocabulary entry for a new word, with the same flag bits as the table's other entries.</summary>
    public byte[] WordEntry(List<byte[]> table, int number, string word)
    {
        int flags = table.Count > 0 ? table[0][2] & 0xF8 : 0x80;
        int token = Token(word.ToUpperInvariant());
        return new[] { (byte)number, (byte)token, (byte)(flags | (token >> 8)) };
    }

    // ---- writing ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The database laid out from the pointer table: the bytes from <see cref="Start"/> to the verb table are kept
    /// (with the new pointers and start room), then every table in the order the pointers expect.
    /// </summary>
    public byte[] Serialize()
    {
        var output = new List<byte>(Slice(Start, Layout.Verbs - Start));
        var pointers = new int[10];
        int Here() => Start + output.Count;
        void Word(int w) { output.Add((byte)w); output.Add((byte)(w >> 8)); }

        // Each table is followed by what followed it in the original (normally two zero bytes).
        foreach (var w in Verbs) output.AddRange(w);
        output.AddRange(trailers[0]);
        pointers[0] = Here(); foreach (var w in Nouns) output.AddRange(w); output.AddRange(trailers[1]);
        pointers[1] = Here(); foreach (var w in Adverbs) output.AddRange(w); output.AddRange(trailers[2]);
        pointers[2] = Here(); foreach (var (_, e) in Objects) output.AddRange(e); output.AddRange(trailers[3]);
        pointers[3] = Here(); foreach (var (_, e) in Rooms) output.AddRange(e); output.AddRange(trailers[4]);
        pointers[4] = Here(); foreach (var l in High) output.AddRange(l); output.AddRange(trailers[5]);
        pointers[5] = Here();
        foreach (var (room, lines) in Local) { Word(room); foreach (var l in lines) output.AddRange(l); output.Add(0); }
        output.AddRange(trailers[6]);
        pointers[6] = Here(); foreach (var l in Low) output.AddRange(l); output.AddRange(trailers[7]);
        pointers[7] = Here(); foreach (var (_, e) in Messages) output.AddRange(e); output.AddRange(trailers[8]);
        pointers[8] = Here(); foreach (var (_, e) in Pictures) output.AddRange(e); output.AddRange(trailers[9]);
        pointers[9] = Here(); foreach (var t in Tokens) output.AddRange(t);

        var result = output.ToArray();
        var at = Layout.PointerAddresses;
        for (int k = 0; k < 10; k++)
        {
            result[at[k] - Start] = (byte)pointers[k];
            result[at[k] - Start + 1] = (byte)(pointers[k] >> 8);
        }
        result[Layout.StartRoom - Start] = (byte)StartRoom;
        result[Layout.StartRoom - Start + 1] = (byte)(StartRoom >> 8);
        return result;
    }
}
