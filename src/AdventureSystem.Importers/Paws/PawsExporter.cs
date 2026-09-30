using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Packaging;
using AdventureSystem.Core.Parsing;
using AdventureSystem.Core.Snapshots;
using AdventureSystem.Importers.Common;

namespace AdventureSystem.Importers.Paws;

/// <summary>
/// Writes a game imported from a PAWS snapshot back into that snapshot. The original is re-imported as a baseline:
/// whatever is unchanged keeps its original bytes (including condacts the importer couldn't translate), and only
/// edited or new locations, objects, exits, messages, words and process entries are compiled. The database is then
/// laid out again in the memory it occupied, and the snapshot written in its original format.
/// </summary>
public sealed class PawsExporter : IAdventureExporter
{
    public string Id => "paws";
    public string Name => "PAWS (ZX Spectrum)";

    public string Extension(Adventure a)
    {
        var ext = Path.GetExtension(a.Origin?.FileName ?? "").TrimStart('.').ToLowerInvariant();
        return ext is "sna" or "z80" ? ext : "sna";
    }

    public string? CannotExport(Adventure a) =>
        a.IsStory ? "Z-code stories can't be converted." :
        a.Origin?.OriginalAsset is not { } asset || !a.Assets.ContainsKey(asset) ? "Needs the original PAWS snapshot; this game wasn't imported from one." : null;

    public ExportResult Export(Adventure a)
    {
        if (CannotExport(a) is { } why) throw new InvalidOperationException(why);
        var original = a.Assets[a.Origin!.OriginalAsset!];
        var snap = SpectrumSnapshot.Load(original, Path.GetExtension(a.Origin.FileName));
        var db = PawsDatabase.TryOpen(snap) ?? throw new InvalidDataException("No PAWS database was found in the original snapshot.");
        var baseline = new PawsConversion(PawsDatabase.TryOpen(SpectrumSnapshot.Load(original, Path.GetExtension(a.Origin.FileName)))!, a.Origin.FileName).Run().Adventure;
        var writer = new PawsWriter(a, baseline, db);
        writer.Build();
        writer.CopyBack(snap);
        var result = new ExportResult(snap.Save(original)) { Summary = writer.Summary };
        writer.Warnings.CopyTo(result.Warnings);
        return result;
    }
}

/// <summary>Compiles the changes and lays the database out again.</summary>
internal sealed class PawsWriter
{
    private const int WordAny = 1, WordNone = 255;
    private readonly Adventure a, baseline;
    private readonly PawsDatabase db;
    public readonly ExportWarnings Warnings = new();
    public string Summary = "";

    // The database as it will be written.
    private readonly List<byte[]> objTexts = new(), locTexts = new(), messages = new(), sysTexts = new();
    private readonly List<byte[]> connections = new();
    private readonly List<RawWord> vocab = new();
    private readonly List<int> initiallyAt = new(), objAttributes = new();
    private readonly List<(int Noun, int Adjective)> objWords = new();
    private readonly List<List<RawEntry>> tables = new();
    private readonly List<(int Address, int Attribute)> pictures = new();
    private readonly List<byte[]> originalLocs = new(), originalConnections = new(), originalMessages = new();

    private readonly Dictionary<string, int> roomNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> itemNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> flagOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> tableOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> usedFlags = new();
    private readonly string?[] tokens = new string?[256];
    private int newline = 7;

    private sealed record RawWord(byte[] Text, int Number, int Type)
    {
        public string Key => Encoding.ASCII.GetString(Text.Select(b => (byte)(b ^ 0xFF)).ToArray()).Trim().ToLowerInvariant();
    }

    private sealed record RawEntry(int Verb, int Noun, byte[] Condacts);

    public PawsWriter(Adventure a, Adventure baseline, PawsDatabase db)
    {
        this.a = a;
        this.baseline = baseline;
        this.db = db;
    }

    private static string Json(object? o) => JsonSerializer.Serialize(o, AdventurePackage.JsonOptions);
    private static bool Same(object? x, object? y) => Json(x) == Json(y);

    // ================================================================ reading the original

    private byte[] RawText(byte[] mem, int address)
    {
        int end = address;
        while ((mem[end & 0xFFFF] ^ 0xFF) != 31 && end - address < 8192) end++;
        return Enumerable.Range(address, end - address + 1).Select(i => mem[i & 0xFFFF]).ToArray();
    }

    private byte[] TableRaw(byte[] mem, int pointer, int index) =>
        RawText(mem, PawsDatabase.Word(mem, PawsDatabase.Word(mem, pointer) + 2 * index));

    private void ReadOriginal()
    {
        var m = db.Main;
        for (int n = 0; n < db.NumObjects; n++) objTexts.Add(TableRaw(m, PawsDatabase.PtrObjects, n));
        for (int n = 0; n < db.NumSysMessages; n++) sysTexts.Add(TableRaw(m, PawsDatabase.PtrSysMessages, n));
        for (int n = 0; n < db.NumLocations; n++)
        {
            var (mem, index) = db.Locate(db.LocationPages, n)!.Value;
            locTexts.Add(TableRaw(mem, PawsDatabase.PtrLocations, index));
            int c = PawsDatabase.Word(mem, PawsDatabase.Word(mem, PawsDatabase.PtrConnections) + 2 * index), e = c;
            while (mem[e & 0xFFFF] != 255 && e - c < 256) e += 2;
            connections.Add(Enumerable.Range(c, e - c + 1).Select(i => mem[i & 0xFFFF]).ToArray());
            int gfx = PawsDatabase.Word(mem, PawsDatabase.PtrGraphics), attrs = PawsDatabase.Word(mem, PawsDatabase.PtrGraphicAttributes);
            pictures.Add(gfx == 0 ? (0, 0) : (PawsDatabase.Word(mem, gfx + 2 * index), mem[(attrs + index) & 0xFFFF]));
        }
        for (int n = 0; n < db.NumMessages; n++)
        {
            var (mem, index) = db.Locate(db.MessagePages, n)!.Value;
            messages.Add(TableRaw(mem, PawsDatabase.PtrMessages, index));
        }
        int v = PawsDatabase.Word(m, PawsDatabase.PtrVocabulary);
        for (; m[v] != 0 && v < 0xFFF0; v += 7)
            vocab.Add(new RawWord(m.AsSpan(v, 5).ToArray(), m[v + 5], m[v + 6]));
        for (int n = 0; n < db.NumObjects; n++)
        {
            initiallyAt.Add(db.InitiallyAt(n));
            objWords.Add((db.ObjectNoun(n), db.ObjectAdjective(n)));
            objAttributes.Add(db.ObjectAttributes(n));
        }
        for (int t = 0; t < db.NumProcesses; t++)
        {
            var list = new List<RawEntry>();
            int e = PawsDatabase.Word(m, PawsDatabase.Word(m, PawsDatabase.PtrProcesses) + 2 * t);
            for (; m[e] != 0 && list.Count < 2000; e += 4)
            {
                int c = PawsDatabase.Word(m, e + 2), end = c;
                while (m[end] != 255 && m[end] < PawsCondactInfo.All.Length) end += 1 + PawsCondactInfo.All[m[end]].Params;
                list.Add(new RawEntry(m[e], m[e + 1], m.AsSpan(c, end - c).ToArray()));
            }
            tables.Add(list);
        }
        if (db.Compressed)
        {
            int d = db.DictionaryAddress;
            for (int c = 164; c < 256; c++)
            {
                var sb = new StringBuilder();
                for (int guard = 0; guard < 32; guard++)
                {
                    int b = m[d++ & 0xFFFF];
                    sb.Append((char)(b & 0x7F));
                    if ((b & 0x80) != 0) break;
                }
                if (c >= 165) tokens[c] = sb.ToString();
            }
        }
        // Newlines: whichever code the game's own texts use.
        int sevens = 0, thirteens = 0;
        foreach (var t in locTexts.Concat(messages).Concat(sysTexts))
            foreach (var b in t) { if ((b ^ 0xFF) == 7) sevens++; else if ((b ^ 0xFF) == 13) thirteens++; }
        newline = thirteens > sevens ? 13 : 7;

        originalLocs.AddRange(locTexts);
        originalConnections.AddRange(connections);
        originalMessages.AddRange(messages);
        for (int n = 0; n < db.NumLocations; n++) roomNumber[$"r{n}"] = n;
        for (int n = 0; n < db.NumObjects; n++) itemNumber[$"o{n}"] = n;
        foreach (var entry in tables.SelectMany(t => t))
            foreach (var (op, args) in Condacts(entry.Condacts))
                if (FlagParameters.TryGetValue(PawsCondactInfo.All[op].Name, out var which))
                    foreach (var i in which) usedFlags.Add(args[i]);
        for (int f = 0; f < 64; f++) usedFlags.Add(f);   // system flags
        foreach (var variable in a.Variables)
            if (Regex.Match(variable.Name, @"^f(\d+)$") is { Success: true } fm) usedFlags.Add(int.Parse(fm.Groups[1].Value));
    }

    private static readonly Dictionary<string, int[]> FlagParameters = new()
    {
        ["ZERO"] = new[] { 0 }, ["NOTZERO"] = new[] { 0 }, ["EQ"] = new[] { 0 }, ["NOTEQ"] = new[] { 0 }, ["GT"] = new[] { 0 }, ["LT"] = new[] { 0 },
        ["SAME"] = new[] { 0, 1 }, ["NOTSAME"] = new[] { 0, 1 }, ["SET"] = new[] { 0 }, ["CLEAR"] = new[] { 0 }, ["LET"] = new[] { 0 },
        ["PLUS"] = new[] { 0 }, ["MINUS"] = new[] { 0 }, ["COPYFF"] = new[] { 0, 1 }, ["RANDOM"] = new[] { 0 }, ["PRINT"] = new[] { 0 },
        ["ADD"] = new[] { 0, 1 }, ["SUB"] = new[] { 0, 1 }, ["COPYOF"] = new[] { 1 }, ["COPYFO"] = new[] { 0 }, ["WEIGH"] = new[] { 1 }, ["WEIGHT"] = new[] { 0 },
    };

    private static IEnumerable<(int Op, int[] Args)> Condacts(byte[] raw)
    {
        for (int i = 0; i < raw.Length;)
        {
            int op = raw[i];
            int n = PawsCondactInfo.All[op].Params;
            yield return (op, raw.AsSpan(i + 1, n).ToArray().Select(b => (int)b).ToArray());
            i += 1 + n;
        }
    }

    // ================================================================ building

    public void Build()
    {
        ReadOriginal();
        Rooms();
        Items();
        SystemMessages();
        Vocabulary();
        Processes();
        Settings();
        Summary = $"{locTexts.Count} locations, {objTexts.Count} objects, {messages.Count} messages, {tables.Sum(t => t.Count)} process entries";
    }

    // ---------------------------------------------------------------- locations and exits

    private void Rooms()
    {
        foreach (var r in a.Rooms.Where(r => !roomNumber.ContainsKey(r.Id)))
        {
            roomNumber[r.Id] = locTexts.Count;
            locTexts.Add(Encode(r.Description));
            connections.Add(new byte[] { 255 });
            pictures.Add((-1, 0));
            if (r.PictureId != null) Warnings.Add("New locations can't have PAWS pictures; theirs were left out");
        }
        for (int n = 0; n < db.NumLocations; n++)
        {
            var r = a.FindRoom($"r{n}");
            var b = baseline.FindRoom($"r{n}")!;
            if (r == null)
            {
                locTexts[n] = Encode("");
                connections[n] = new byte[] { 255 };
                Warnings.Add("Deleted locations keep their numbers in PAWS; they were left empty");
                continue;
            }
            if (r.Description != b.Description) locTexts[n] = Encode(r.Description);
            if (r.PictureId != b.PictureId) Warnings.Add("Changing which picture a location shows isn't supported; PAWS draws each location's own picture");
        }
        foreach (var r in a.Rooms)
        {
            int n = roomNumber[r.Id];
            var b = baseline.FindRoom(r.Id);
            if (b != null && Same(r.Exits, b.Exits)) continue;
            var bytes = new List<byte>();
            foreach (var e in r.Exits)
            {
                if (!roomNumber.TryGetValue(e.TargetRoomId, out var target)) continue;
                if (e.Conditions.Count > 0 || e.DoorItemId != null) Warnings.Add("PAWS exits can't have conditions or doors; they became plain exits");
                if (e.TravelMessage.Length > 0 || e.BlockedMessage.Length > 0) Warnings.Add("PAWS exits have no travel or blocked messages");
                bytes.Add((byte)DirectionWord(e.Direction));
                bytes.Add((byte)target);
            }
            bytes.Add(255);
            connections[n] = bytes.ToArray();
        }
        if (!string.Equals(a.StartRoomId, "r0", StringComparison.OrdinalIgnoreCase)) Warnings.Add("PAWS games always start at location 0");
    }

    // ---------------------------------------------------------------- objects

    private void Items()
    {
        foreach (var it in a.Items.Where(i => !itemNumber.ContainsKey(i.Id)))
        {
            itemNumber[it.Id] = objTexts.Count;
            objTexts.Add(Encode(""));
            initiallyAt.Add(252);
            objWords.Add((WordNone, WordNone));
            objAttributes.Add(0);
        }
        if (objTexts.Count > 255) throw new InvalidOperationException("PAWS allows at most 255 objects.");
        for (int n = 0; n < objTexts.Count; n++)
        {
            var it = a.FindItem($"o{n}") ?? a.Items.FirstOrDefault(i => itemNumber[i.Id] == n);
            var b = n < db.NumObjects ? baseline.FindItem($"o{n}") : null;
            if (it == null)
            {
                objTexts[n] = Encode("");
                initiallyAt[n] = 252;
                Warnings.Add("Deleted objects keep their numbers in PAWS; they were left empty and nowhere");
                continue;
            }
            if (b == null || it.Article != b.Article || it.Name != b.Name || it.Description != b.Description)
                objTexts[n] = Encode(ObjectText(it, b));
            if (b == null || it.Location != b.Location) initiallyAt[n] = LocationNumber(it.Location);
            if (b == null || !Same(it.Nouns, b.Nouns) || !Same(it.Adjectives, b.Adjectives))
                objWords[n] = (it.Nouns.Count > 0 ? WordFor(it.Nouns, 2) : WordNone, it.Adjectives.Count > 0 ? WordFor(it.Adjectives, 3) : WordNone);
            if (b == null || it.Weight != b.Weight || it.Container != b.Container || it.Wearable != b.Wearable)
                objAttributes[n] = Math.Clamp(it.Weight, 0, 63) | (it.Container ? 0x40 : 0) | (it.Wearable ? 0x80 : 0);
            if (b != null && (it.Portable != b.Portable || it.Scenery != b.Scenery || it.Edible != b.Edible || it.Openable != b.Openable || it.Switchable != b.Switchable || it.LightSource != b.LightSource))
                Warnings.Add("PAWS objects only have a weight and container / wearable flags; other properties need process entries");
            if (b != null && (it.Container && it.Capacity != b.Capacity)) Warnings.Add("PAWS containers have no capacity");
        }
    }

    /// <summary>
    /// PAWS object text: "a lamp" or "a lamp. It is lit." – the name runs up to the first full stop and the
    /// importer takes the whole text as the description when there is more.
    /// </summary>
    private static string ObjectText(Item it, Item? b)
    {
        var name = $"{it.Article} {it.Name}".Trim();
        var text = it.Description.Trim();
        if (b != null && text == b.Description.Trim() && it.Name != b.Name && b.Name.Length > 0) text = text.Replace(b.Name, it.Name);
        if (b != null && text == b.Description.Trim() && it.Article != b.Article && b.Article.Length > 0 && text.StartsWith(b.Article + " ")) text = it.Article + text[b.Article.Length..];
        if (text.Length == 0) return name;
        return text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length && text[name.Length] == '.' ? text : $"{name}. {text}";
    }

    private int LocationNumber(string? location) => location switch
    {
        null or "" or Locations.Nowhere => 252,
        Locations.Worn => 253,
        Locations.Carried => 254,
        Locations.Here => 255,
        _ when roomNumber.TryGetValue(location, out var r) => r,
        _ when itemNumber.TryGetValue(location, out var o) => o,   // containers: objects inside object n are at location n
        _ => 252,
    };

    // ---------------------------------------------------------------- system messages

    private static readonly (string Id, int Number)[] MappedMessages =
    {
        (Msg.Dark, 0), (Msg.DontUnderstand, 6), (Msg.CantGo, 7), (Msg.CantDoThat, 8), (Msg.Ok, 15), (Msg.NotWorn, 23), (Msg.AlreadyCarrying, 25),
        (Msg.NotHere, 26), (Msg.TooMany, 27), (Msg.NotCarrying, 28), (Msg.Waited, 35), (Msg.Taken, 36), (Msg.Worn, 37), (Msg.Removed, 38),
        (Msg.Dropped, 39), (Msg.NotWearable, 40), (Msg.TooHeavy, 43),
    };

    private void SystemMessages()
    {
        for (int n = 0; n < sysTexts.Count; n++)
            if (a.Messages.TryGetValue($"sys{n}", out var text) && baseline.Messages.TryGetValue($"sys{n}", out var old) && text != old)
                sysTexts[n] = Encode(Unplaceholder(text));
        foreach (var (id, n) in MappedMessages)
            if (n < sysTexts.Count && a.Messages.TryGetValue(id, out var text) && baseline.Messages.TryGetValue(id, out var old) && text != old)
                sysTexts[n] = Encode(Unplaceholder(text));
        foreach (var (id, text) in a.Messages)
            if (!baseline.Messages.TryGetValue(id, out var old) ? true : old != text)
                if (!id.StartsWith("sys", StringComparison.OrdinalIgnoreCase) && MappedMessages.All(m => !m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    Warnings.Add($"The \"{id}\" message has no PAWS system message");
    }

    // ---------------------------------------------------------------- vocabulary

    /// <summary>The five significant letters PAWS keeps.</summary>
    private static string Key(string word) => (word.Length > 5 ? word[..5] : word).ToLowerInvariant();

    private int? Lookup(string word, params int[] types) =>
        types.Select(t => vocab.FirstOrDefault(v => v.Type == t && v.Key == Key(word))).FirstOrDefault(v => v != null)?.Number;

    private void AddWord(string word, int number, int type)
    {
        if (vocab.Any(v => v.Key == Key(word) && v.Type == type)) return;
        var text = Key(word).ToUpperInvariant().PadRight(5).Select(c => (byte)(c ^ 0xFF)).ToArray();
        var entry = new RawWord(text, number, type);
        // The PAWS editor keeps the vocabulary in alphabetical order.
        int at = vocab.FindIndex(v => string.CompareOrdinal(v.Key, entry.Key) > 0);
        if (at < 0) vocab.Add(entry); else vocab.Insert(at, entry);
    }

    private int FreeWordNumber(int from = 20)
    {
        var used = vocab.Select(v => v.Number).ToHashSet();
        for (int n = from; n < 255; n++) if (!used.Contains(n)) return n;
        for (int n = 2; n < from; n++) if (!used.Contains(n)) return n;
        throw new InvalidOperationException("The PAWS vocabulary is full (254 word values).");
    }

    /// <summary>The word value for a group of synonyms (type 2 noun, 3 adjective, 0 verb), adding words PAWS doesn't know.</summary>
    private int WordFor(IReadOnlyList<string> synonyms, int type)
    {
        int number = synonyms.Select(w => Lookup(w, type)).FirstOrDefault(n => n != null) ?? FreeWordNumber();
        foreach (var w in synonyms) if (Lookup(w, type) == null) AddWord(w, number, type);
        return number;
    }

    private void Vocabulary()
    {
        foreach (var v in a.Vocabulary.Verbs)
        {
            var b = baseline.Vocabulary.Verbs.FirstOrDefault(x => x.Id == v.Id);
            if (b != null && Same(v.Words, b.Words)) continue;
            if (b == null && BuiltInLexicon.VerbTable.Any(row => row.Split('|')[0].Trim().TrimEnd('!') == v.Id) && v.Words.Count == 0) continue;
            VerbNumber(v.Id);
        }
        foreach (var list in new[] { (a.Vocabulary.Nouns, 2), (a.Vocabulary.Adjectives, 3), (a.Vocabulary.Adverbs, 1), (a.Vocabulary.Prepositions, 4) })
            foreach (var w in list.Item1)
                if (Lookup(w, list.Item2) == null) AddWord(w, FreeWordNumber(), list.Item2);
        foreach (var (word, dir) in a.Vocabulary.Directions)
            if (!baseline.Vocabulary.Directions.ContainsKey(word) && !word.StartsWith("choice")) AddWord(word, DirectionWord(dir), 0);
    }

    private int VerbNumber(string verb)
    {
        var def = a.Vocabulary.Verbs.FirstOrDefault(v => v.Id.Equals(verb, StringComparison.OrdinalIgnoreCase));
        var words = def?.Words is { Count: > 0 } w ? w.ToList() : new List<string> { verb };
        if (!words.Contains(verb, StringComparer.OrdinalIgnoreCase) && def == null) words.Insert(0, verb);
        int? number = words.Select(x => Lookup(x, 0)).FirstOrDefault(n => n != null) ?? Lookup(verb, 2);
        number ??= a.Vocabulary.Directions.Values.Contains(verb) ? DirectionWord(verb) : FreeWordNumber();
        foreach (var x in words) if (Lookup(x, 0) == null && Lookup(x, 2) != number) AddWord(x, number.Value, 0);
        return number.Value;
    }

    /// <summary>The word value that moves the player in a direction (movement words are verbs under 14 or conversion nouns under 20).</summary>
    private int DirectionWord(string direction)
    {
        foreach (var (word, dir) in baseline.Vocabulary.Directions.Concat(a.Vocabulary.Directions))
            if (dir.Equals(direction, StringComparison.OrdinalIgnoreCase) && (Lookup(word, 0, 2) is { } n)) return n;
        int number = Enumerable.Range(2, 12).FirstOrDefault(n => vocab.All(v => v.Number != n), 0);
        if (number == 0) number = FreeWordNumber(14);
        AddWord(direction, number, 0);
        return number;
    }

    // ---------------------------------------------------------------- process tables

    private static readonly Regex EntryId = new(@"^t(\d+)_(\d+)(_sub)?(?:_(\d+))?$");

    private void Processes()
    {
        // Existing entries: keep, drop or recompile.
        var baseByEntry = baseline.Triggers.Where(t => EntryId.IsMatch(t.Id)).GroupBy(t => EntryKey(t.Id)).ToDictionary(g => g.Key, g => g.ToList());
        var modelById = a.Triggers.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var skippedOps = new HashSet<string> { "LISTOBJ", "LISTAT", "DOALL", "COPYOF", "COPYFO", "COPYOO", "WHATO", "WEIGH", "WEIGHT", "ADD", "SUB", "PARSE", "NEWTEXT", "RESET", "EXTERN", "MODE", "LINE", "PROTECT", "TIME", "INPUT", "PROMPT", "GRAPHIC", "CHARSET", "PAPER", "INK", "BORDER", "SAVEAT", "BACKAT", "PRINTAT", "TIMEOUT", "MOVE" };
        for (int t = 0; t < tables.Count; t++)
        {
            var rebuilt = new List<RawEntry>();
            for (int i = 0; i < tables[t].Count; i++)
            {
                var entry = tables[t][i];
                var variants = new[] { "", "_sub" }.Where(s => baseByEntry.ContainsKey((t, i, s))).ToList();
                if (variants.Count == 0) { rebuilt.Add(entry); continue; }   // did nothing the importer could show: keep
                var changed = variants.Where(s => !Same(baseByEntry[(t, i, s)].Select(x => x.Id).Select(id => modelById.GetValueOrDefault(id)), baseByEntry[(t, i, s)])).ToList();
                if (changed.Count == 0) { rebuilt.Add(entry); continue; }
                var variant = changed.OrderBy(s => s.Length).First();
                var parts = baseByEntry[(t, i, variant)].Select(x => modelById.GetValueOrDefault(x.Id)).Where(x => x != null).Cast<Trigger>().ToList();
                if (parts.Count == 0) continue;   // deleted
                if (Condacts(entry.Condacts).Any(c => skippedOps.Contains(PawsCondactInfo.All[c.Op].Name)))
                    Warnings.Add("Edited process entries lose the PAWS condacts that couldn't be imported (see the import notes)");
                var chain = $"chain_t{t}_{i}{variant}";
                var conditions = new List<Condition>();
                var actions = new List<GameAction>();
                var body = new List<byte>();
                foreach (var p in parts)
                {
                    var conds = p.Conditions.Where(c => !(c.Type == ConditionType.VarEqualsVar && c.A == chain)).ToList();
                    var acts = p.Actions.Where(x => x.A != chain).ToList();
                    body.AddRange(CompileConditions(conds));
                    body.AddRange(CompileActions(acts));
                }
                var first = baseByEntry[(t, i, variant)][0];
                bool words = (t == 0 || variant == "_sub" && parts[0].Verb != null) && (parts[0].Verb != first.Verb || parts[0].Noun1 != first.Noun1);
                foreach (var (verb, noun) in words ? Patterns(parts[0], entry) : new List<(int, int)> { (entry.Verb, entry.Noun) })
                    rebuilt.Add(new RawEntry(verb, noun, body.ToArray()));
            }
            tables[t] = rebuilt;
        }
        foreach (var p in a.Triggers.Where(t => t.Event == TriggerEvent.Subroutine && Regex.IsMatch(t.Name, @"^proc\d+$")))
            tableOf[p.Name] = int.Parse(p.Name[4..]);

        // New triggers.
        var known = baseline.Triggers.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var t in a.Triggers.Where(t => !known.Contains(t.Id)))
        {
            if (!t.Enabled) { Warnings.Add("Disabled triggers were left out (PAWS entries can't be switched off)"); continue; }
            var conds = new List<byte>();
            var acts = new List<byte>();
            if (t.RoomId != null && roomNumber.TryGetValue(t.RoomId, out var room)) conds.AddRange(Op("AT", room));
            if (t.OnceOnly)
            {
                int flag = FreeFlag();
                conds.AddRange(Op("ZERO", flag));
                acts.AddRange(Op("SET", flag));
            }
            int table;
            switch (t.Event)
            {
                case TriggerEvent.BeforeCommand: table = 0; break;
                case TriggerEvent.AfterDescribe: table = 1; break;
                case TriggerEvent.EnterRoom or TriggerEvent.BeforeEnterRoom:
                    table = 1;
                    Warnings.Add("Room entry triggers run after the location is described in PAWS (Process 1)");
                    break;
                case TriggerEvent.EveryTurn: table = 2; break;
                case TriggerEvent.AfterCommand:
                    table = 2;
                    Warnings.Add("After-command triggers run once per turn in PAWS (Process 2)");
                    break;
                case TriggerEvent.GameStart:
                {
                    table = 1;
                    int flag = FreeFlag();
                    conds.InsertRange(0, Op("ZERO", flag));
                    acts.InsertRange(0, Op("SET", flag));
                    Warnings.Add("Game start triggers run after the first description in PAWS");
                    break;
                }
                case TriggerEvent.Subroutine: table = TableFor(t.Name); break;
                default:
                    Warnings.Add($"{t.Event} triggers have no PAWS equivalent");
                    continue;
            }
            if (t.Event == TriggerEvent.BeforeCommand)
            {
                if (t.Noun2 is { Length: > 0 } and not "*") conds.AddRange(Op("NOUN2", t.Noun2 == "-" ? WordNone : NounNumber(t.Noun2)));
                if (t.Adverb is { Length: > 0 } and not "*") conds.AddRange(Op("ADVERB", Lookup(t.Adverb, 1) ?? WordFor(new[] { t.Adverb }, 1)));
                if (t.Preposition is { Length: > 0 } and not "*") conds.AddRange(Op("PREP", Lookup(t.Preposition, 4) ?? WordFor(new[] { t.Preposition }, 4)));
            }
            conds.AddRange(CompileConditions(t.Conditions));
            acts.AddRange(CompileActions(t.Actions));
            if (t.Event == TriggerEvent.BeforeCommand && t.StopsCommand && !EndsTable(t.Actions)) acts.AddRange(Op("DONE"));
            var body = conds.Concat(acts).ToArray();
            var patterns = t.Event == TriggerEvent.BeforeCommand || t.Verb != null ? Patterns(t, null) : new List<(int, int)> { (WordAny, WordAny) };
            foreach (var (verb, noun) in patterns)
            {
                var e = new RawEntry(verb, noun, body);
                if (table == 0)
                {
                    // Sorted by verb and noun, ahead of existing entries for the same words so the new one is tried first.
                    int at = tables[0].FindIndex(x => x.Verb > verb || x.Verb == verb && x.Noun >= noun);
                    if (at < 0) tables[0].Add(e); else tables[0].Insert(at, e);
                }
                else if (t.Event == TriggerEvent.GameStart) tables[table].Insert(0, e);
                else tables[table].Add(e);
            }
        }
        if (tables.Count > 255) throw new InvalidOperationException("PAWS allows at most 255 process tables.");
    }

    private static (int, int, string) EntryKey(string id)
    {
        var m = EntryId.Match(id);
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value);
    }

    private int TableFor(string name)
    {
        if (tableOf.TryGetValue(name, out var t)) return t;
        tables.Add(new List<RawEntry>());
        return tableOf[name] = tables.Count - 1;
    }

    private static bool EndsTable(List<GameAction> actions) =>
        actions.Count > 0 && actions[^1].Type is ActionType.Done or ActionType.Ok or ActionType.Look or ActionType.Inventory or ActionType.Quit or ActionType.Lose or ActionType.Win or ActionType.Restart;

    /// <summary>The verb and noun word values of a trigger (one entry for each alternative verb or noun).</summary>
    private List<(int Verb, int Noun)> Patterns(Trigger t, RawEntry? original)
    {
        // "*" is any word; "_" (no noun) is how PAWS authors usually write a verb on its own.
        var verbs = (t.Verb ?? "*").Split('|').Select(v => v is "*" or "" ? (original is { Verb: WordAny or WordNone } ? original.Verb : WordAny) : VerbWord(v)).Distinct().ToList();
        var nouns = (t.Noun1 ?? "*").Split('|').Select(n => n is "*" or "" or "-" ? (original is { Noun: WordAny or WordNone } ? original.Noun : WordNone) : NounNumber(n)).Distinct().ToList();
        return verbs.SelectMany(v => nouns.Select(n => (v, n))).ToList();
    }

    /// <summary>A verb pattern word: a verb id, a direction, or a conversion noun (nouns under 20 act as verbs).</summary>
    private int VerbWord(string verb)
    {
        if (a.Vocabulary.Verbs.Any(v => v.Id.Equals(verb, StringComparison.OrdinalIgnoreCase))) return VerbNumber(verb);
        if (a.Vocabulary.Directions.Values.Contains(verb, StringComparer.OrdinalIgnoreCase)) return DirectionWord(verb);
        if (Lookup(verb, 0, 2) is { } n) return n;
        if (BuiltInLexicon.VerbTable.FirstOrDefault(row => row.Split('|')[0].Trim().TrimEnd('!') == verb) is { } builtIn)
        {
            var words = builtIn.Split('|')[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(w => !w.Contains(' ')).ToList();
            if (words.Select(w => Lookup(w, 0)).FirstOrDefault(x => x != null) is { } known) return known;
        }
        return VerbNumber(verb);
    }

    private int NounNumber(string noun)
    {
        if (noun == "$noun1" || noun == "*") return WordAny;
        if (a.FindItem(noun) is { } it)
        {
            if (itemNumber.TryGetValue(it.Id, out var o) && o < objWords.Count && objWords[o].Noun != WordNone) return objWords[o].Noun;
            if (it.Nouns.Count > 0) return WordFor(it.Nouns, 2);
        }
        return Lookup(noun, 2) ?? Lookup(noun, 0) ?? WordFor(new[] { noun }, 2);
    }

    private int FreeFlag()
    {
        for (int f = 255; f >= 64; f--)
            if (usedFlags.Add(f)) return f;
        throw new InvalidOperationException("There are no free PAWS flags left.");
    }

    private int Flag(string variable)
    {
        switch (variable)
        {
            case "@carried": return 1;
            case "@score": return 30;
            case "@turns": return 31;
            case "@room": return 38;
        }
        if (Regex.Match(variable, @"^f(\d+)$") is { Success: true } m) return int.Parse(m.Groups[1].Value);
        if (flagOf.TryGetValue(variable, out var f)) return f;
        f = flagOf[variable] = FreeFlag();
        if (a.FindVariable(variable) is { InitialValue: not 0 }) Warnings.Add("PAWS flags start at 0; variables with other starting values need setting in a process entry");
        return f;
    }

    private static byte[] Op(string name, params int[] args) =>
        new[] { (byte)PawsCondactInfo.Opcode(name) }.Concat(args.Select(x => (byte)Math.Clamp(x, 0, 255))).ToArray();

    private int ObjectArg(string? id)
    {
        if (id != null && itemNumber.TryGetValue(id, out var o)) return o;
        Warnings.Add("Conditions and actions on unknown objects were left out");
        return -1;
    }

    private IEnumerable<byte> CompileConditions(IEnumerable<Condition> conditions)
    {
        foreach (var c in conditions)
        {
            byte[]? code = Condition(c);
            if (code == null) Warnings.Add($"{c.Type}{(c.Negate ? " (negated)" : "")} conditions have no PAWS equivalent");
            else foreach (var b in code) yield return b;
        }
    }

    private byte[]? Condition(Condition c)
    {
        bool not = c.Negate;
        int o;
        switch (c.Type)
        {
            case ConditionType.PlayerIn when c.A != null && roomNumber.TryGetValue(c.A, out var r): return Op(not ? "NOTAT" : "AT", r);
            case ConditionType.ItemPresent when (o = ObjectArg(c.A)) >= 0: return Op(not ? "ABSENT" : "PRESENT", o);
            case ConditionType.ItemWorn when (o = ObjectArg(c.A)) >= 0: return Op(not ? "NOTWORN" : "WORN", o);
            case ConditionType.ItemCarried when (o = ObjectArg(c.A)) >= 0: return Op(not ? "NOTCARR" : "CARRIED", o);
            case ConditionType.ItemIn when c.B == Locations.Carried && (o = ObjectArg(c.A)) >= 0: return Op(not ? "NOTCARR" : "CARRIED", o);
            case ConditionType.ItemIn when (o = ObjectArg(c.A)) >= 0: return Op(not ? "ISNOTAT" : "ISAT", o, LocationNumber(c.B));
            case ConditionType.Chance when !not: return Op("CHANCE", c.N);
            case ConditionType.Chance: return Op("CHANCE", 100 - c.N);
            case ConditionType.VarGreater when c.A == "@room": return not ? Op("ATLT", c.N + 1) : Op("ATGT", c.N);
            case ConditionType.VarLess when c.A == "@room": return not ? Op("ATGT", c.N - 1) : Op("ATLT", c.N);
            case ConditionType.VarEquals when c.N == 0: return Op(not ? "NOTZERO" : "ZERO", Flag(c.A!));
            case ConditionType.VarEquals: return Op(not ? "NOTEQ" : "EQ", Flag(c.A!), c.N);
            case ConditionType.VarGreater: return not ? Op("LT", Flag(c.A!), c.N + 1) : Op("GT", Flag(c.A!), c.N);
            case ConditionType.VarLess: return not ? Op("GT", Flag(c.A!), c.N - 1) : Op("LT", Flag(c.A!), c.N);
            case ConditionType.VarEqualsVar when c.N == 0: return Op(not ? "NOTSAME" : "SAME", Flag(c.A!), Flag(c.B!));
            case ConditionType.AdjectiveUsed when !not: return Op("ADJECT1", Lookup(c.A ?? "", 3) ?? WordFor(new[] { c.A! }, 3));
            case ConditionType.AdjectiveUsed when c.A is "" or null: return Op("ADJECT1", WordNone);
            case ConditionType.AdverbUsed when !not: return Op("ADVERB", Lookup(c.A ?? "", 1) ?? WordFor(new[] { c.A! }, 1));
            case ConditionType.AdverbUsed when c.A is "" or null: return Op("ADVERB", WordNone);
            case ConditionType.PrepositionIs when !not: return Op("PREP", Lookup(c.A ?? "", 4) ?? WordFor(new[] { c.A! }, 4));
            case ConditionType.PrepositionIs when c.A == "*": return Op("PREP", WordNone);
            case ConditionType.Noun2Is when !not: return Op("NOUN2", c.A == "-" ? WordNone : NounNumber(c.A!));
            case ConditionType.Always when !not: return Array.Empty<byte>();
            default: return null;
        }
    }

    private IEnumerable<byte> CompileActions(List<GameAction> actions)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            var x = actions[i];
            // DESC and INVEN end the table themselves; the importer added a Done after them.
            if (x.Type == ActionType.Done && i > 0 && actions[i - 1].Type is ActionType.Look or ActionType.Inventory) continue;
            var code = Action(x);
            if (code == null) Warnings.Add($"{x.Type} actions have no PAWS equivalent");
            else foreach (var b in code) yield return b;
        }
    }

    private byte[]? Action(GameAction x)
    {
        int o;
        bool current = x.A == "$noun1";
        switch (x.Type)
        {
            case ActionType.TakeItem when current: return Op("AUTOG");
            case ActionType.DropItem when current: return Op("AUTOD");
            case ActionType.WearItem when current: return Op("AUTOW");
            case ActionType.UnwearItem when current: return Op("AUTOR");
            case ActionType.MoveItem when current && x.B == Locations.Carried: return Op("AUTOT");
            case ActionType.MoveItem when current: return Op("AUTOP", LocationNumber(x.B));
            case ActionType.TakeItem when (o = ObjectArg(x.A)) >= 0: return Op("GET", o);
            case ActionType.DropItem when (o = ObjectArg(x.A)) >= 0: return Op("DROP", o);
            case ActionType.WearItem when (o = ObjectArg(x.A)) >= 0: return Op("WEAR", o);
            case ActionType.UnwearItem when (o = ObjectArg(x.A)) >= 0: return Op("REMOVE", o);
            case ActionType.CreateItem when (o = ObjectArg(x.A)) >= 0: return x.B is null or "" or Locations.Here ? Op("CREATE", o) : Op("PLACE", o, LocationNumber(x.B));
            case ActionType.DestroyItem when (o = ObjectArg(x.A)) >= 0: return Op("DESTROY", o);
            case ActionType.SwapItems when (o = ObjectArg(x.A)) >= 0 && ObjectArg(x.B) is var o2 and >= 0: return Op("SWAP", o, o2);
            case ActionType.MoveItem when (o = ObjectArg(x.A)) >= 0: return x.B is null or "" ? Op("DESTROY", o) : x.B == Locations.Here ? Op("CREATE", o) : Op("PLACE", o, LocationNumber(x.B));
            case ActionType.DropAll: return Op("DROPALL");
            case ActionType.SetVar when x.N == 255: return Op("SET", Flag(x.A!));
            case ActionType.SetVar when x.N == 0: return Op("CLEAR", Flag(x.A!));
            case ActionType.SetVar: return Op("LET", Flag(x.A!), x.N);
            case ActionType.AddVar: return Op(x.N < 0 ? "MINUS" : "PLUS", Flag(x.A!), Math.Abs(x.N));
            case ActionType.AwardScore: return Op("PLUS", 30, x.N);
            case ActionType.CopyVar: return Op("COPYFF", Flag(x.B!), Flag(x.A!));
            case ActionType.RandomVar:
                if (x.N != 100) Warnings.Add("PAWS RANDOM always picks 1-100");
                return Op("RANDOM", Flag(x.A!));
            case ActionType.GoTo when x.A != null && roomNumber.TryGetValue(x.A, out var r): return Op("GOTO", r);
            case ActionType.Look: return Op("DESC");
            case ActionType.Inventory: return Op("INVEN");
            case ActionType.ShowTurns: return Op("TURNS");
            case ActionType.ShowScore: return Op("SCORE");
            case ActionType.ClearScreen: return Op("CLS");
            case ActionType.ShowPicture when x.A != null && Regex.Match(x.A, @"^p(\d+)$") is { Success: true } pm: return Op("PICTURE", int.Parse(pm.Groups[1].Value));
            case ActionType.Pause when x.N == 0: return Op("ANYKEY");
            case ActionType.Pause: return Op("PAUSE", Math.Clamp(x.N / 20, 1, 255));
            case ActionType.Beep: return Op("BEEP", 10, 50);
            case ActionType.Done: return Op("DONE");
            case ActionType.Ok: return Op("OK");
            case ActionType.RunTrigger when x.A != null: return Op("PROCESS", TableFor(x.A));
            case ActionType.Win or ActionType.Lose:
                if (x.Type == ActionType.Win) Warnings.Add("PAWS has no \"win\"; winning ends the game like losing (END)");
                return (string.IsNullOrWhiteSpace(x.Text) ? Array.Empty<byte>() : Message(x.Text!, true)).Concat(Op("END")).ToArray();
            case ActionType.Quit: return Op("QUIT");
            case ActionType.Save: return Op("SAVE");
            case ActionType.Restore: return Op("LOAD");
            case ActionType.Message: return Message(x.Text ?? "", x.N != 1);
            default: return null;
        }
    }

    // ---------------------------------------------------------------- messages

    /// <summary>MESSAGE (with a line break) or MES; {var:x} placeholders become PRINT, an empty line NEWLINE.</summary>
    private byte[] Message(string text, bool lineBreak)
    {
        if (text.Length == 0) return lineBreak ? Op("NEWLINE") : Array.Empty<byte>();
        var output = new List<byte>();
        var parts = Regex.Split(text, @"(\{var:[^{}]+\})");
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            bool last = i == parts.Length - 1;
            if (Regex.Match(part, @"^\{var:([^{}]+)\}$") is { Success: true } v)
            {
                output.AddRange(Op("PRINT", Flag(v.Groups[1].Value)));
                if (last && lineBreak) output.AddRange(Op("NEWLINE"));
                continue;
            }
            if (part.Length == 0) continue;
            // A system message with exactly this text is used as it is.
            int sys = !(last && lineBreak) ? SysMessageNumber(part) : -1;
            if (sys >= 0) { output.AddRange(Op("SYSMESS", sys)); continue; }
            output.AddRange(Op(last && lineBreak ? "MESSAGE" : "MES", MessageNumber(part)));
        }
        return output.ToArray();
    }

    private int SysMessageNumber(string text)
    {
        for (int n = 0; n < Math.Min(sysTexts.Count, db.NumSysMessages); n++)
            if (Decode(sysTexts[n]) == Unplaceholder(text)) return n;
        return -1;
    }

    private readonly Dictionary<string, int> messageCache = new();

    private int MessageNumber(string text)
    {
        var raw = Unplaceholder(text);
        if (messageCache.TryGetValue(raw, out var n)) return n;
        for (n = 0; n < messages.Count; n++)
            if (Decode(messages[n]) == raw) return messageCache[raw] = n;
        if (messages.Count >= 256) throw new InvalidOperationException("PAWS allows at most 256 messages.");
        messages.Add(Encode(raw));
        return messageCache[raw] = messages.Count - 1;
    }

    /// <summary>Engine placeholders back to PAWS: {noun1} is "_" (the current object); others are dropped.</summary>
    private string Unplaceholder(string text) =>
        Regex.Replace(text.Replace("{noun1}", "_").Replace("{the noun1}", "the _"), @"\{[^{}]*\}", m =>
        {
            Warnings.Add($"Text placeholders like {m.Value} have no PAWS equivalent");
            return "";
        });

    // ================================================================ text

    /// <summary>Decodes raw PAWS text the way the importer's light cleaning sees it.</summary>
    private string Decode(byte[] raw)
    {
        var sb = new StringBuilder();
        int skip = 0;
        foreach (var b0 in raw)
        {
            int c = b0 ^ 0xFF;
            if (c == 31) break;
            if (skip > 0) { skip--; continue; }
            switch (c)
            {
                case 6: sb.Append(' '); break;
                case 7 or 13: sb.Append('\n'); break;
                case >= 16 and <= 21: skip = 1; break;
                case 22 or 23: skip = 2; break;
                case 96: sb.Append('£'); break;
                case 127: sb.Append('©'); break;
                case >= 32 and < 127: sb.Append(db.Shown(c)); break;
                case >= 165 when tokens[c] != null:
                    foreach (var t in tokens[c]!) if (t >= 32 && t < 127) sb.Append(db.Shown(t)); else if (t is '\x07' or '\x0D') sb.Append('\n');
                    break;
            }
        }
        return PawsConversion.LightClean(sb.ToString());
    }

    /// <summary>A dictionary token as the game shows it (see <see cref="PawsDatabase.LineCharacters"/>).</summary>
    private string Shown(string token) => new(token.Select(ch => ch >= 32 && ch < 127 ? db.Shown(ch) : ch).ToArray());

    /// <summary>Encodes text: dictionary tokens where the database is compressed, XOR 255, ending with 31.</summary>
    private byte[] Encode(string text)
    {
        var codes = new List<int>();
        text = text.Replace("\r", "");
        for (int i = 0; i < text.Length;)
        {
            int best = -1, bestLength = 1;
            if (db.Compressed)
                for (int c = 165; c < 256; c++)
                    if (tokens[c] is { Length: > 1 } t && t.Length > bestLength && string.CompareOrdinal(text, i, Shown(t).Replace('\x07', '\n').Replace('\x0D', '\n'), 0, t.Length) == 0 && t.All(ch => ch >= 32 && ch < 127 || ch is '\x07' or '\x0D'))
                    {
                        best = c;
                        bestLength = t.Length;
                    }
            if (best >= 0) { codes.Add(best); i += bestLength; continue; }
            char ch = text[i++];
            if (db.Stored(ch) is var stored and >= 0) { codes.Add(stored); continue; }
            codes.Add(ch switch
            {
                '\n' => newline,
                '£' => 96,
                '©' => 127,
                >= ' ' and < (char)127 => ch,
                '\t' => 6,
                _ => '?',
            });
        }
        codes.Add(31);
        return codes.Select(c => (byte)(c ^ 0xFF)).ToArray();
    }

    // ================================================================ settings

    private void Settings()
    {
        var s = a.Settings;
        var b = baseline.Settings;
        if (s.MaxCarriedItems != b.MaxCarriedItems || s.MaxCarriedWeight != b.MaxCarriedWeight)
            Warnings.Add("PAWS sets carrying limits with ABILITY in a process entry; the new limits weren't written");
        if (a.Title != baseline.Title || a.Author != baseline.Author) Warnings.Add("PAWS databases have no title or author");
        if (s.TextColor != b.TextColor || s.BackgroundColor != b.BackgroundColor)
        {
            ink = Nearest(s.TextColor);
            paper = Nearest(s.BackgroundColor);
        }
        if (a.Pictures.Any(p => baseline.FindPicture(p.Id) is not { } bp || !Same(p, bp)))
            Warnings.Add("Edited or new pictures can't be written back to PAWS; the original pictures are kept");
    }

    private int? ink, paper;

    private static int? Nearest(string colour)
    {
        if (colour is not { Length: 7 } || colour[0] != '#') return null;
        uint rgb = Convert.ToUInt32(colour[1..], 16);
        int best = 0; long bestDistance = long.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            uint p = Palettes.Spectrum[i];
            long dr = ((p >> 16) & 255) - ((rgb >> 16) & 255), dg = ((p >> 8) & 255) - ((rgb >> 8) & 255), db = (p & 255) - (rgb & 255);
            long d = dr * dr + dg * dg + db * db;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    // ================================================================ layout

    /// <summary>A block of memory the database may use.</summary>
    private sealed class Region(byte[] mem, int start, int end)
    {
        public readonly byte[] Mem = mem;
        public int Next = start;
        public readonly int End = end;
        public int Put(IEnumerable<byte> bytes)
        {
            int at = Next;
            foreach (var b in bytes)
            {
                if (Next < End) Mem[Next] = b;
                Next++;
            }
            return at;
        }
        public int Reserve(int length) { int at = Next; Next += length; return at; }
        public void Word(int address, int value) { Mem[address & 0xFFFF] = (byte)value; Mem[(address + 1) & 0xFFFF] = (byte)(value >> 8); }
        public int Over => Math.Max(0, Next - End);
    }

    /// <summary>The span the original database tables and texts occupy on one memory view, up to the pictures.</summary>
    private (int Start, int End) Span(byte[] mem, bool main, IEnumerable<int> locations, IEnumerable<int> msgs)
    {
        int low = int.MaxValue, high = 0;
        void Add(int s, int e) { low = Math.Min(low, s); high = Math.Max(high, e); }
        void Text(int address) { int e = address; while ((mem[e & 0xFFFF] ^ 0xFF) != 31 && e - address < 8192) e++; Add(address, e + 1); }
        void Table(int pointer, IEnumerable<int> indices, int count)
        {
            int t = PawsDatabase.Word(mem, pointer);
            Add(t, t + 2 * count);
            foreach (var i in indices) Text(PawsDatabase.Word(mem, t + 2 * i));
        }
        var locs = locations.ToList();
        var ms = msgs.ToList();
        if (locs.Count > 0)
        {
            Table(PawsDatabase.PtrLocations, locs, locs.Count);
            int ct = PawsDatabase.Word(mem, PawsDatabase.PtrConnections);
            Add(ct, ct + 2 * locs.Count);
            foreach (var i in locs) { int c = PawsDatabase.Word(mem, ct + 2 * i), e = c; while (mem[e & 0xFFFF] != 255) e += 2; Add(c, e + 1); }
        }
        if (ms.Count > 0) Table(PawsDatabase.PtrMessages, ms, ms.Count);
        if (main)
        {
            var m = mem;
            Table(PawsDatabase.PtrObjects, Enumerable.Range(0, db.NumObjects), db.NumObjects);
            Table(PawsDatabase.PtrSysMessages, Enumerable.Range(0, db.NumSysMessages), db.NumSysMessages);
            int v = PawsDatabase.Word(m, PawsDatabase.PtrVocabulary), ve = v;
            while (m[ve] != 0) ve += 7;
            Add(v, ve + 7);
            Add(PawsDatabase.Word(m, PawsDatabase.PtrInitiallyAt), PawsDatabase.Word(m, PawsDatabase.PtrInitiallyAt) + db.NumObjects + 1);
            Add(PawsDatabase.Word(m, PawsDatabase.PtrObjectWords), PawsDatabase.Word(m, PawsDatabase.PtrObjectWords) + 2 * db.NumObjects);
            Add(PawsDatabase.Word(m, PawsDatabase.PtrObjectWeights), PawsDatabase.Word(m, PawsDatabase.PtrObjectWeights) + db.NumObjects);
            int pt = PawsDatabase.Word(m, PawsDatabase.PtrProcesses);
            Add(pt, pt + 2 * db.NumProcesses);
            for (int p = 0; p < db.NumProcesses; p++)
            {
                int e = PawsDatabase.Word(m, pt + 2 * p);
                for (; m[e] != 0; e += 4)
                {
                    int c = PawsDatabase.Word(m, e + 2), ce = c;
                    while (m[ce] != 255 && m[ce] < PawsCondactInfo.All.Length) ce += 1 + PawsCondactInfo.All[m[ce]].Params;
                    Add(c, ce + 1);
                }
                Add(e, e + 1);
            }
        }
        // The database may grow up to the picture data that follows it (pointer 65519).
        int pictures = PawsDatabase.Word(mem, 65519);
        if (pictures >= high && pictures < PawsDatabase.PtrProcesses) high = pictures;
        return (low, high);
    }

    private byte[] Texts(Region r, List<byte[]> texts, IEnumerable<int> indices)
    {
        var list = indices.ToList();
        var addresses = new int[list.Count];
        var shared = new Dictionary<string, int>();
        for (int k = 0; k < list.Count; k++)
        {
            var t = texts[list[k]];
            var key = Convert.ToBase64String(t);
            addresses[k] = share && shared.TryGetValue(key, out var at) ? at : shared[key] = r.Put(t);
        }
        return addresses.SelectMany(x => new[] { (byte)x, (byte)(x >> 8) }).ToArray();
    }

    private void WriteMain(Region r, byte[] m, bool paged, List<int> locs, List<int> msgs)
    {
        // As the PAWS editor lays it out: each table's condact lists, an empty list, then the entries and an end
        // marker pointing at the empty list. When space is short, identical condact lists are shared.
        var condacts = new Dictionary<string, int>();
        int Condacts(byte[] body)
        {
            var key = Convert.ToBase64String(body);
            if (share && condacts.TryGetValue(key, out var at)) return at;
            return condacts[key] = r.Put(body.Append((byte)255));
        }
        var tableAddresses = new List<int>();
        foreach (var t in tables)
        {
            var bodies = t.Select(e => Condacts(e.Condacts)).ToList();
            int empty = r.Put(new byte[] { 255 });
            var bytes = new List<byte>();
            for (int i = 0; i < t.Count; i++) bytes.AddRange(new[] { (byte)t[i].Verb, (byte)t[i].Noun, (byte)bodies[i], (byte)(bodies[i] >> 8) });
            bytes.AddRange(new[] { (byte)0, (byte)0, (byte)empty, (byte)(empty >> 8) });
            tableAddresses.Add(r.Put(bytes));
        }
        r.Word(PawsDatabase.PtrProcesses, r.Put(tableAddresses.SelectMany(x => new[] { (byte)x, (byte)(x >> 8) })));
        r.Word(PawsDatabase.PtrObjects, r.Put(Texts(r, objTexts, Enumerable.Range(0, objTexts.Count))));
        if (!paged) WritePaged(r, locs, msgs, connectionsLast: true);
        r.Word(PawsDatabase.PtrSysMessages, r.Put(Texts(r, sysTexts, Enumerable.Range(0, sysTexts.Count))));
        if (!paged) WriteConnections(r, locs);
        r.Word(PawsDatabase.PtrVocabulary, r.Put(vocab.SelectMany(v => v.Text.Append((byte)v.Number).Append((byte)v.Type)).Concat(new byte[7])));
        r.Word(PawsDatabase.PtrInitiallyAt, r.Put(initiallyAt.Select(x => (byte)x).Append((byte)255)));
        r.Word(PawsDatabase.PtrObjectWords, r.Put(objWords.SelectMany(w => new[] { (byte)w.Noun, (byte)w.Adjective }).Concat(new byte[2])));
        r.Word(PawsDatabase.PtrObjectWeights, r.Put(objAttributes.Select(x => (byte)x)));
        r.Word(65517, r.Next);
    }

    /// <summary>Locations, connections, messages and the location pictures' tables on one memory view.</summary>
    private void WritePaged(Region r, List<int> locs, List<int> msgs, bool connectionsLast = false)
    {
        if (locs.Count > 0)
        {
            r.Word(PawsDatabase.PtrLocations, r.Put(Texts(r, locTexts, locs)));
            if (!connectionsLast) WriteConnections(r, locs);
            // Picture tables: rewritten only when locations were added (new ones get an empty picture).
            if (locs.Any(n => pictures[n].Address < 0) && PawsDatabase.Word(r.Mem, PawsDatabase.PtrGraphics) != 0)
            {
                int empty = r.Put(new byte[] { 7 });
                r.Word(PawsDatabase.PtrGraphics, r.Put(locs.SelectMany(n => { int x = pictures[n].Address < 0 ? empty : pictures[n].Address; return new[] { (byte)x, (byte)(x >> 8) }; })));
                r.Word(PawsDatabase.PtrGraphicAttributes, r.Put(locs.Select(n => (byte)(pictures[n].Address < 0 ? 0x07 : pictures[n].Attribute))));
            }
        }
        if (msgs.Count > 0) r.Word(PawsDatabase.PtrMessages, r.Put(Texts(r, messages, msgs)));
    }

    private void WriteConnections(Region r, List<int> locs)
    {
        if (locs.Count == 0) return;
        var conn = locs.Select(n => r.Put(connections[n])).ToList();
        r.Word(PawsDatabase.PtrConnections, r.Put(conn.SelectMany(x => new[] { (byte)x, (byte)(x >> 8) })));
    }

    private bool share;

    /// <summary>Writes the database into the snapshot's memory; if it doesn't fit, again sharing identical texts and condact lists.</summary>
    public void CopyBack(SpectrumSnapshot snap)
    {
        var views = db.Pages.ToDictionary(p => p.Key, p => (byte[])p.Value.Clone());
        try
        {
            Write(snap);
        }
        catch (InvalidOperationException) when (!share)
        {
            foreach (var (page, view) in views) Array.Copy(view, db.Pages[page], view.Length);
            share = true;
            Write(snap);
        }
    }

    private void Write(SpectrumSnapshot snap)
    {
        var m = db.Main;
        int top = db.MainTop;
        if (!db.Is128K)
        {
            var (start, end) = Span(m, true, Enumerable.Range(0, db.NumLocations), Enumerable.Range(0, db.NumMessages));
            var r = new Region(m, start, end);
            WriteMain(r, m, false, Enumerable.Range(0, locTexts.Count).ToList(), Enumerable.Range(0, messages.Count).ToList());
            if (r.Over > 0) throw new InvalidOperationException($"The game is {r.Over} bytes too big for the Spectrum's memory.");
            Counts(m, top);
            Array.Copy(m, 0x4000, snap.Memory, 0x4000, 0xC000);
            return;
        }

        // 128K: locations, connections and messages live on the RAM pages; new ones go on the last page.
        var locPages = db.LocationPages.ToList();
        var msgPages = db.MessagePages.ToList();
        if (locTexts.Count > db.NumLocations) locPages[^1] = (locPages[^1].Page, locPages[^1].Start, locTexts.Count);
        if (messages.Count > db.NumMessages) msgPages[^1] = (msgPages[^1].Page, msgPages[^1].Start, messages.Count);
        List<int> On(List<(int Page, int Start, int End)> pages, int page) => pages.Where(p => p.Page == page).SelectMany(p => Enumerable.Range(p.Start, p.End - p.Start)).ToList();
        int Count(List<(int Page, int Start, int End)> pages, int page) => pages.Where(p => p.Page == page).Sum(p => p.End - p.Start);

        // Page 0 holds the main tables, with its own locations and messages in their usual place.
        {
            var mem = db.Pages[0];
            var (start, end) = Span(mem, true, Enumerable.Range(0, Count(db.LocationPages, 0)), Enumerable.Range(0, Count(db.MessagePages, 0)));
            var r = new Region(mem, start, end);
            WriteMain(r, mem, false, On(locPages, 0), On(msgPages, 0));
            if (r.Over > 0) throw new InvalidOperationException($"The game is {r.Over} bytes too big for the Spectrum's memory.");
        }
        foreach (var page in locPages.Concat(msgPages).Select(p => p.Page).Where(p => p != 0).Distinct())
        {
            var locs = On(locPages, page);
            var msgs = On(msgPages, page);
            bool changed = locs.Any(n => n >= db.NumLocations || !locTexts[n].SequenceEqual(originalLocs[n]) || !connections[n].SequenceEqual(originalConnections[n]))
                           || msgs.Any(n => n >= db.NumMessages || !messages[n].SequenceEqual(originalMessages[n]));
            if (changed) WritePage(db.Pages[page], page, locs, msgs, Count(db.LocationPages, page), Count(db.MessagePages, page));
        }
        Counts(db.Pages[0], top);
        PageTable(db.Pages[0], top + 297, locPages);
        PageTable(db.Pages[0], top + 283, msgPages);
        // Back into the banks: page 0's view holds banks 5 and 2 below 0xC000.
        var main = db.Pages[0];
        Array.Copy(main, 0x4000, snap.Banks128![5], 0, 16384);
        Array.Copy(main, 0x8000, snap.Banks128[2], 0, 16384);
        foreach (var (page, view) in db.Pages) Array.Copy(view, 0xC000, snap.Banks128[page], 0, 16384);
    }

    /// <summary>
    /// A 128K RAM page, laid out as the PAWS editor does: location texts and pointers, message texts and pointers,
    /// the page's placeholder system message table, connections and their pointers, then its placeholder vocabulary
    /// and object tables, with the free space before the pictures.
    /// </summary>
    private void WritePage(byte[] mem, int page, List<int> locs, List<int> msgs, int oldLocs, int oldMsgs)
    {
        int W(int address) => PawsDatabase.Word(mem, address);
        var block = Enumerable.Range(0, 11).Select(i => W(PawsDatabase.PtrProcesses + 2 * i)).ToArray();   // 65497..65517
        int pics = W(65519);
        int low = int.MaxValue, firstConnection = int.MaxValue;
        for (int i = 0; i < oldLocs; i++) { low = Math.Min(low, W(W(PawsDatabase.PtrLocations) + 2 * i)); firstConnection = Math.Min(firstConnection, W(W(PawsDatabase.PtrConnections) + 2 * i)); }
        for (int i = 0; i < oldMsgs; i++) low = Math.Min(low, W(W(PawsDatabase.PtrMessages) + 2 * i));
        int aStart = W(PawsDatabase.PtrMessages) + 2 * oldMsgs, aEnd = firstConnection;
        int bStart = W(PawsDatabase.PtrConnections) + 2 * oldLocs, bEnd = W(65517);
        if (oldLocs == 0 || oldMsgs == 0 || aStart > aEnd || bStart > bEnd || bEnd > pics || low > aStart)
            throw new InvalidOperationException($"RAM page {page} of this 128K game isn't laid out the way the PAWS editor does it, so it can't be rewritten.");
        var blockA = mem.AsSpan(aStart, aEnd - aStart).ToArray();
        var blockB = mem.AsSpan(bStart, bEnd - bStart).ToArray();

        var r = new Region(mem, low, pics);
        r.Word(PawsDatabase.PtrLocations, r.Put(Texts(r, locTexts, locs)));
        r.Word(PawsDatabase.PtrMessages, r.Put(Texts(r, messages, msgs)));
        int newA = r.Put(blockA);
        var conn = locs.Select(n => r.Put(connections[n])).ToList();
        r.Word(PawsDatabase.PtrConnections, r.Put(conn.SelectMany(x => new[] { (byte)x, (byte)(x >> 8) })));
        int newB = r.Put(blockB);
        if (r.Over > 0) throw new InvalidOperationException($"The game is {r.Over} bytes too big for RAM page {page}.");
        // Move the pointers into the placeholder tables with them (and the placeholder system message table's own pointers).
        for (int i = 0; i < block.Length; i++)
        {
            int ptr = PawsDatabase.PtrProcesses + 2 * i;
            if (ptr is PawsDatabase.PtrLocations or PawsDatabase.PtrMessages or PawsDatabase.PtrConnections) continue;
            if (block[i] >= aStart && block[i] <= aEnd && aEnd > aStart) r.Word(ptr, block[i] + newA - aStart);
            else if (block[i] >= bStart && block[i] <= bEnd) r.Word(ptr, block[i] + newB - bStart);
        }
        int sys = block[4];
        if (sys >= aStart && sys < aEnd)
            for (int k = sys; k + 1 < aEnd; k += 2)
            {
                int target = PawsDatabase.Word(blockA, k - aStart);
                if (target >= aStart && target < aEnd) r.Word(k + newA - aStart, target + newA - aStart);
            }
        if (locs.Any(n => pictures[n].Address < 0) && W(PawsDatabase.PtrGraphics) != 0)
        {
            var g = new Region(mem, r.Next, pics);
            int empty = g.Put(new byte[] { 7 });
            g.Word(PawsDatabase.PtrGraphics, g.Put(locs.SelectMany(n => { int x = pictures[n].Address < 0 ? empty : pictures[n].Address; return new[] { (byte)x, (byte)(x >> 8) }; })));
            g.Word(PawsDatabase.PtrGraphicAttributes, g.Put(locs.Select(n => (byte)(pictures[n].Address < 0 ? 0x07 : pictures[n].Attribute))));
            if (g.Over > 0) throw new InvalidOperationException($"The game is {g.Over} bytes too big for RAM page {page}.");
        }
    }

    private void Counts(byte[] m, int top)
    {
        m[top + 324] = (byte)objTexts.Count;
        m[top + 325] = (byte)locTexts.Count;
        m[top + 326] = (byte)messages.Count;
        m[top + 327] = (byte)sysTexts.Count;
        m[top + 328] = (byte)tables.Count;
        if (ink is { } i) { m[top + 312] = (byte)(i & 7); m[top + 318] = (byte)(i >= 8 ? 1 : 0); }
        if (paper is { } p) m[top + 314] = (byte)(p & 7);
        if (locTexts.Count > 252) throw new InvalidOperationException("PAWS allows at most 252 locations.");
    }

    /// <summary>128K page distribution: pairs of (page, first number not on it), 255 = all the rest. Only the last page's end can change.</summary>
    private static void PageTable(byte[] m, int table, List<(int Page, int Start, int End)> pages)
    {
        var last = pages[^1];
        for (int i = 0; i < 7; i++)
            if (m[table + 2 * i] == last.Page)
            {
                if (m[table + 2 * i + 1] != 255) m[table + 2 * i + 1] = (byte)last.End;
                return;
            }
    }
}
