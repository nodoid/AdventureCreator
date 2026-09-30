using System.Text.Json;
using System.Text.RegularExpressions;
using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Importers.Common;

namespace AdventureSystem.Importers.Gac;

/// <summary>
/// Applies the changes made to an imported GAC game to its original database. The game is compared with a fresh
/// import of the original file (the baseline): whatever is unchanged keeps its original bytes (including anything
/// the importer could not convert), and only edited or new rooms, objects, words, messages and triggers are
/// compiled back into GAC entries and condition lines.
/// </summary>
internal sealed partial class GacCompiler
{
    private readonly Adventure a;
    private readonly Adventure baseline;
    private readonly GacRawDatabase raw;
    private readonly GacDatabase db;
    private readonly ExportWarnings warnings;

    public bool Changed { get; private set; }

    public GacCompiler(Adventure adventure, Adventure baseline, GacRawDatabase raw, GacDatabase db, ExportWarnings warnings)
    {
        a = adventure;
        this.baseline = baseline;
        this.raw = raw;
        this.db = db;
        this.warnings = warnings;
    }

    public void Run()
    {
        Vocabulary();
        Rooms();
        Objects();
        SystemMessages();
        Pictures();
        Conditions();
        var start = RoomNumber(a.StartRoomId);
        if (start != raw.StartRoom) { raw.StartRoom = start; Changed = true; }
    }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static string J(object? o) => JsonSerializer.Serialize(o, Json);

    // ================================================================= numbering

    private static int? Numbered(string? id, char prefix) =>
        id != null && id.Length > 1 && id[0] == prefix && int.TryParse(id[1..], out var n) && id[1..] == n.ToString() ? n : null;

    private readonly Dictionary<string, int> newRooms = new(), newObjects = new();

    private int RoomNumber(string? id)
    {
        if (Numbered(id, 'r') is int n) return n;
        if (id == null) return 0;
        if (newRooms.TryGetValue(id, out var m)) return m;
        int next = Math.Max(raw.Rooms.Select(r => r.Id).DefaultIfEmpty(0).Max(), a.Rooms.Select(r => Numbered(r.Id, 'r') ?? 0).DefaultIfEmpty(0).Max());
        next = Math.Max(next, newRooms.Values.DefaultIfEmpty(0).Max()) + 1;
        return newRooms[id] = next;
    }

    private int? ObjectNumber(string? id)
    {
        if (Numbered(id, 'o') is int n) return n;
        if (id == null || a.FindItem(id) == null) return null;
        if (newObjects.TryGetValue(id, out var m)) return m;
        var used = raw.Objects.Select(o => o.Id).Concat(a.Items.Select(i => Numbered(i.Id, 'o') ?? 0)).Concat(newObjects.Values).ToHashSet();
        for (int k = 1; k < 255; k++)
            if (!used.Contains(k)) return newObjects[id] = k;
        warnings.Add("GAC has room for 254 objects; the extra objects were left out");
        return null;
    }

    private int? LocationNumber(string? loc, string what)
    {
        switch (loc)
        {
            case null or "": return 0;
            case Locations.Carried: return 255;
            case Locations.Worn:
                warnings.Add("GAC has no worn objects; worn objects are carried");
                return 255;
        }
        if (loc.StartsWith('r') || a.FindRoom(loc) != null) return RoomNumber(loc);
        warnings.Add($"GAC objects can only be in a room or carried; {what} was put nowhere");
        return 0;
    }

    // ================================================================= vocabulary

    private readonly Dictionary<string, int> verbByWord = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> verbById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> nounByWord = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> adverbByWord = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> verbByDirection = new(StringComparer.OrdinalIgnoreCase);

    private void Vocabulary()
    {
        originalWords = new[] { raw.Verbs.ToList(), raw.Nouns.ToList(), raw.Adverbs.ToList() };
        // Verbs: one definition per verb number; synonyms share it.
        int nextVerb = db.Verbs.Select(w => w.Number).DefaultIfEmpty(0).Max();
        var verbPairs = new List<(int Number, string Word)>();
        // The importer's verb ids (see GacConverter.BuildVocabulary), so each definition finds its GAC number.
        var idNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in db.Verbs.Where(w => w.Text.Length > 0).GroupBy(w => w.Number))
        {
            var groupWords = group.Select(w => w.Text.ToLowerInvariant()).Distinct().ToList();
            var id = GacConverter.CanonicalDirection(groupWords) ?? groupWords[0];
            if (idNumbers.ContainsKey(id)) id = $"{groupWords[0]}{group.Key}";
            idNumbers[id] = group.Key;
        }
        foreach (var def in a.Vocabulary.Verbs)
        {
            var words = def.Words.Count > 0 ? def.Words : new List<string> { def.Id };
            int? number = idNumbers.TryGetValue(def.Id, out var fromId) ? fromId : null;
            foreach (var w in words)
                if (number == null && db.Verbs.FirstOrDefault(v => v.Text.Equals(w, StringComparison.OrdinalIgnoreCase)) is { Text.Length: > 0 } found) number = found.Number;
            if (number == null)
            {
                if (nextVerb >= 255) { warnings.Add("GAC has room for 255 verbs; extra verbs were left out"); continue; }
                number = ++nextVerb;
            }
            verbById[def.Id] = number.Value;
            foreach (var w in words.Select(w => w.ToLowerInvariant()).Distinct())
            {
                verbPairs.Add((number.Value, w));
                verbByWord.TryAdd(w, number.Value);
            }
        }
        raw.Verbs.Clear();
        raw.Verbs.AddRange(WordTable(db.Verbs, OriginalVerbs, verbPairs, keep: w => w.Text.Length == 0));
        foreach (var g in verbPairs.GroupBy(p => p.Number))
            verbByDirection.TryAdd(GacConverter.CanonicalDirection(g.Select(p => p.Word)) ?? g.First().Word, g.Key);

        // Nouns: the vocabulary's nouns and the objects' nouns. New words for an object share its other nouns'
        // number, else take the object's number (GAC's convention: noun n is object n), else a new one.
        int nextNoun = db.Nouns.Where(w => w.Number < 255).Select(w => w.Number).DefaultIfEmpty(0).Max();
        foreach (var w in db.Nouns.Where(w => w.Text.Length > 0 && w.Number != 255)) nounByWord.TryAdd(w.Text.ToLowerInvariant(), w.Number);
        var nounWords = a.Vocabulary.Nouns.Concat(a.Items.SelectMany(i => i.Nouns)).Select(w => w.ToLowerInvariant()).Distinct().ToList();
        var known = nounWords.Where(nounByWord.ContainsKey).ToHashSet();
        foreach (var item in a.Items)
            foreach (var w in item.Nouns.Select(w => w.ToLowerInvariant()).Where(w => !nounByWord.ContainsKey(w)))
            {
                int? shared = item.Nouns.Select(n => n.ToLowerInvariant()).Where(known.Contains).Select(n => (int?)nounByWord[n]).FirstOrDefault();
                int? own = Numbered(item.Id, 'o') is int on && !nounByWord.ContainsValue(on) ? on : null;
                nounByWord[w] = shared ?? own ?? (nextNoun < 254 ? ++nextNoun : 0);
            }
        foreach (var w in nounWords.Where(w => !nounByWord.ContainsKey(w))) nounByWord[w] = nextNoun < 254 ? ++nextNoun : 0;
        if (nounByWord.ContainsValue(0)) warnings.Add("GAC has room for 254 nouns; extra nouns were left out");
        var nounPairs = nounWords.Where(w => nounByWord[w] > 0).Select(w => (nounByWord[w], w)).ToList();
        raw.Nouns.Clear();
        raw.Nouns.AddRange(WordTable(db.Nouns, OriginalNouns, nounPairs, keep: w => w.Text.Length == 0 || w.Number == 255));

        int nextAdverb = db.Adverbs.Select(w => w.Number).DefaultIfEmpty(0).Max();
        foreach (var w in db.Adverbs.Where(w => w.Text.Length > 0)) adverbByWord.TryAdd(w.Text.ToLowerInvariant(), w.Number);
        foreach (var w in a.Vocabulary.Adverbs.Select(w => w.ToLowerInvariant()).Where(w => !adverbByWord.ContainsKey(w)))
            adverbByWord[w] = nextAdverb < 255 ? ++nextAdverb : 0;
        var adverbPairs = a.Vocabulary.Adverbs.Select(w => w.ToLowerInvariant()).Distinct().Where(w => adverbByWord[w] > 0).Select(w => (adverbByWord[w], w)).ToList();
        raw.Adverbs.Clear();
        raw.Adverbs.AddRange(WordTable(db.Adverbs, OriginalAdverbs, adverbPairs, keep: w => w.Text.Length == 0));

        if (a.Vocabulary.Prepositions.Except(baseline.Vocabulary.Prepositions).Any())
            warnings.Add("GAC has no prepositions; new prepositions were left out");
    }

    private List<byte[]> OriginalVerbs => originalWords[0];
    private List<byte[]> OriginalNouns => originalWords[1];
    private List<byte[]> OriginalAdverbs => originalWords[2];
    private List<byte[]>[] originalWords = Array.Empty<List<byte[]>>();

    /// <summary>A word table: the original entries that are still wanted (in their order), then the new words.</summary>
    private List<byte[]> WordTable(List<GacWord> decoded, List<byte[]> original, List<(int Number, string Word)> wanted, Func<GacWord, bool> keep)
    {
        var remaining = wanted.Select(p => (p.Number, p.Word.ToLowerInvariant())).ToHashSet();
        var table = new List<byte[]>();
        for (int i = 0; i < decoded.Count; i++)
        {
            var w = decoded[i];
            if (keep(w) || remaining.Remove((w.Number, w.Text.ToLowerInvariant()))) table.Add(original[i]);
        }
        foreach (var (number, word) in wanted.Where(p => remaining.Contains((p.Number, p.Word.ToLowerInvariant()))).Distinct())
        {
            remaining.Remove((number, word.ToLowerInvariant()));
            table.Add(raw.WordEntry(table, number, word));
        }
        if (table.Count != original.Count || table.Where((t, i) => !ReferenceEquals(t, original[i])).Any()) Changed = true;
        return table;
    }

    private int? VerbNumber(string word) =>
        verbById.TryGetValue(word, out var n) || verbByWord.TryGetValue(word, out n) ? n : null;

    private int? NounNumber(string word) =>
        word == "-" ? 0 : nounByWord.TryGetValue(word, out var n) ? n
        : a.FindItem(word) is { } item && ObjectNumber(item.Id) is int o ? o : null;

    // ================================================================= rooms and objects

    private void Rooms()
    {
        var original = raw.Rooms.ToDictionary(r => r.Id, r => r.Bytes);
        var list = new List<(int, byte[])>();
        foreach (var room in a.Rooms)
        {
            var old = baseline.FindRoom(room.Id);
            int number = RoomNumber(room.Id);
            if (!original.ContainsKey(number) && old != null && Numbered(room.Id, 'r') != null)
                continue;   // a placeholder the importer added to fill gaps in the room numbers
            if (old != null && room.Name != old.Name)
                warnings.Add("GAC rooms have no names (the name comes from the description); new names were not kept");
            if (old != null && original.TryGetValue(number, out var bytes) && SameRoom(room, old))
            {
                list.Add((number, bytes));
                continue;
            }
            Changed = true;
            list.Add((number, RoomEntry(room, number, old != null && original.TryGetValue(number, out var o) ? o : null)));
        }
        if (list.Count != raw.Rooms.Count) Changed = true;
        list = InOriginalOrder(list, raw.Rooms);
        raw.Rooms.Clear();
        raw.Rooms.AddRange(list);
    }

    /// <summary>
    /// Entries in the order the original table had them (games don't always keep them sorted by number), new ones
    /// last, so a rebuilt table differs from the original only where something was edited.
    /// </summary>
    private static List<(int, byte[])> InOriginalOrder(List<(int, byte[])> list, List<(int Id, byte[] Bytes)> original)
    {
        var position = new Dictionary<int, int>();
        for (int i = 0; i < original.Count; i++) position.TryAdd(original[i].Id, i);
        return list.OrderBy(e => position.TryGetValue(e.Item1, out var i) ? i : int.MaxValue).ToList();
    }

    private static bool SameExits(Room x, Room y) => J(x.Exits.Select(e => (e.Direction, e.TargetRoomId, e.Conditions.Count, e.Hidden)).ToList())
                                                      == J(y.Exits.Select(e => (e.Direction, e.TargetRoomId, e.Conditions.Count, e.Hidden)).ToList());

    private static bool SameRoom(Room x, Room y) =>
        x.Description.Trim() == y.Description.Trim() && x.PictureId == y.PictureId && SameExits(x, y) && x.IsDark == y.IsDark;

    private byte[] RoomEntry(Room room, int number, byte[]? original)
    {
        var body = new List<byte>();
        int picture = original != null && baseline.FindRoom(room.Id)?.PictureId == room.PictureId
            ? original[4] | (original[5] << 8)
            : Numbered(room.PictureId, 'p') ?? 0;
        if (room.PictureId != null && Numbered(room.PictureId, 'p') == null)
            warnings.Add("Pictures made in Adventure System can't be written to GAC; rooms using them have none");
        body.Add((byte)picture); body.Add((byte)(picture >> 8));
        foreach (var e in room.Exits)
        {
            if (e.Conditions.Count > 0) warnings.Add("GAC connections can't have conditions; conditional exits were written as ordinary ones");
            int? verb = verbByDirection.TryGetValue(e.Direction, out var v) ? v : VerbNumber(e.Direction);
            if (verb == null)
            {
                warnings.Add($"Exits need a GAC verb for their direction; exits \"{e.Direction}\" were left out");
                continue;
            }
            int dest = RoomNumber(e.TargetRoomId);
            body.Add((byte)verb.Value); body.Add((byte)dest); body.Add((byte)(dest >> 8));
        }
        body.Add(0);
        if (room.IsDark) warnings.Add("GAC darkness comes from markers 1 and 2; dark rooms were not marked");
        body.AddRange(raw.EncodeText(room.Description.Trim(), warnings.Add));
        var entry = new List<byte> { (byte)number, (byte)(number >> 8), (byte)body.Count, (byte)(body.Count >> 8) };
        entry.AddRange(body);
        return entry.ToArray();
    }

    private void Objects()
    {
        var original = raw.Objects.ToDictionary(o => o.Id, o => o.Bytes);
        var list = new List<(int, byte[])>();
        foreach (var item in a.Items)
        {
            if (ObjectNumber(item.Id) is not int number) continue;
            var old = baseline.FindItem(item.Id);
            if (item.Container != (old?.Container ?? false) || item.Wearable != (old?.Wearable ?? false) || item.LightSource != (old?.LightSource ?? false)
                || item.Edible != (old?.Edible ?? false) || item.Switchable != (old?.Switchable ?? false) || item.Scenery != (old?.Scenery ?? false))
                warnings.Add("GAC objects have only a name, a weight and a place; other object settings were left out");
            if (old != null && original.TryGetValue(number, out var bytes) && item.Name == old.Name && item.Article == old.Article
                && item.Weight == old.Weight && item.Location == old.Location)
            {
                list.Add((number, bytes));
                continue;
            }
            Changed = true;
            var name = $"{item.Article} {item.Name}".Trim();
            var text = raw.EncodeText(name, warnings.Add);
            int loc = LocationNumber(item.Location, $"\"{item.Name}\"") ?? 0;
            if (text.Length + 3 > 255) { warnings.Add("GAC object names are limited to about 120 words; long names were cut"); text = text[..252]; }
            var entry = new List<byte> { (byte)number, (byte)(3 + text.Length), (byte)Math.Clamp(item.Weight, 0, 255), (byte)loc, (byte)(loc >> 8) };
            entry.AddRange(text);
            list.Add((number, entry.ToArray()));
        }
        if (list.Count != raw.Objects.Count) Changed = true;
        list = InOriginalOrder(list, raw.Objects);
        raw.Objects.Clear();
        raw.Objects.AddRange(list);
    }

    // ================================================================= messages

    private static readonly (string Key, int Number)[] SystemMessageNumbers =
    {
        (Msg.CantDoThat, 241), (Msg.DontUnderstand, 242), (Msg.AlreadyCarrying, 245), (Msg.NotCarrying, 246),
        (Msg.NotHere, 247), (Msg.TooHeavy, 248), (Msg.Dark, 251), (Msg.YouCanSee, 253), (Msg.Ok, 254),
    };

    private void SystemMessages()
    {
        foreach (var (key, number) in SystemMessageNumbers)
        {
            var now = a.Messages.GetValueOrDefault(key);
            if (now == null || now == baseline.Messages.GetValueOrDefault(key)) continue;
            if (key == Msg.YouCanSee) now = now.Replace("{list}", "").Trim();
            SetMessage(number, now);
        }
        if (a.Messages.GetValueOrDefault(Msg.Score) != baseline.Messages.GetValueOrDefault(Msg.Score))
            warnings.Add("The score message is built from GAC messages 249, 250 and 255; edit those texts instead");
        if (a.Settings.Prompt != baseline.Settings.Prompt) SetMessage(240, a.Settings.Prompt.TrimEnd());
    }

    private void SetMessage(int number, string text)
    {
        var bytes = raw.EncodeText(text, warnings.Add);
        if (bytes.Length > 255)
        {
            warnings.Add("GAC messages are limited to 255 bytes (about 120 words); long messages were cut");
            bytes = bytes[..252].Concat(new byte[] { 0x00, 0xC0 }).ToArray();
        }
        var entry = new byte[2 + bytes.Length];
        entry[0] = (byte)number;
        entry[1] = (byte)bytes.Length;
        bytes.CopyTo(entry, 2);
        int i = raw.Messages.FindIndex(m => m.Id == number);
        if (i >= 0) raw.Messages[i] = (number, entry);
        else
        {
            int at = raw.Messages.FindIndex(m => m.Id > number);
            raw.Messages.Insert(at < 0 ? raw.Messages.Count : at, (number, entry));
        }
        Texts[number] = text;
        Changed = true;
    }

    private Dictionary<int, string>? textsCache;
    private Dictionary<int, string> Texts => textsCache ??= db.Messages.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>The number of a message with this text: an existing one, or a new message.</summary>
    private int? MessageFor(string text)
    {
        foreach (var (n, t) in Texts)
            if (n < 240 && (t == text || t.Trim() == text.Trim())) return n;
        for (int n = 1; n < 240; n++)
            if (!Texts.ContainsKey(n))
            {
                SetMessage(n, text);
                return n;
            }
        warnings.Add("GAC has room for 239 messages; some texts were left out");
        return null;
    }

    // ================================================================= pictures

    private void Pictures()
    {
        foreach (var p in a.Pictures)
        {
            var old = baseline.FindPicture(p.Id);
            if (old == null) warnings.Add("Pictures made in Adventure System can't be written to GAC; new pictures were left out");
            else if (J(old) != J(p)) warnings.Add("Edited pictures can't be written back to GAC; the original pictures were kept");
        }
    }
}
