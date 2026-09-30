using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Snapshots;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Quill;

/// <summary>
/// Writes a game imported from a ZX Spectrum Quill snapshot back into that snapshot (.sna or .z80, as it came).
/// <para>
/// The original snapshot is re-imported as a baseline: everything the author hasn't changed keeps its original
/// bytes (so table entries the importer could only approximate still work exactly as before), and only edited or
/// new rooms, objects, texts, words and table entries are compiled back into Quill form. The database is then
/// rebuilt in the memory it occupied, with every table pointer recomputed.
/// </para>
/// </summary>
public sealed class QuillExporter : IAdventureExporter
{
    public string Id => "quill";
    public string Name => "The Quill (ZX Spectrum snapshot)";

    /// <summary>Rebuild the database even when nothing has changed (otherwise the original file is returned).</summary>
    internal bool AlwaysRebuild { get; set; }

    public string Extension(Adventure a)
    {
        var ext = Path.GetExtension(a.Origin?.FileName ?? "").TrimStart('.').ToLowerInvariant();
        return ext is "sna" or "z80" ? ext : "sna";
    }

    public string? CannotExport(Adventure a)
    {
        if (a.IsStory) return "Z-code stories can't be converted.";
        if (Original(a) is not { } data) return "Needs the original Quill snapshot; this game wasn't imported from one.";
        var db = QuillImporter.Locate(data, a.Origin!.FileName);
        if (db == null) return "Needs the original Quill snapshot; this game wasn't imported from one.";
        if (db.Platform != QuillPlatform.Spectrum) return "Only ZX Spectrum Quill games can be exported.";
        return null;
    }

    public ExportResult Export(Adventure a)
    {
        if (CannotExport(a) is { } why) throw new InvalidOperationException(why);
        return new QuillWriter(a, Original(a)!, Extension(a), AlwaysRebuild).Run();
    }

    private static byte[]? Original(Adventure a) =>
        a.Origin is { System: "quill", OriginalAsset: { } asset } && a.Assets.TryGetValue(asset, out var data) ? data : null;
}

/// <summary>Rebuilds a Quill database from an edited game and its original snapshot.</summary>
internal sealed class QuillWriter
{
    private readonly Adventure a;
    private readonly Adventure baseline;
    private readonly byte[] original;
    private readonly string ext;
    private readonly bool alwaysRebuild;
    private readonly QuillDatabase db;
    private readonly ExportWarnings warnings = new();
    private readonly bool late;
    private readonly int h;   // start of the pointer block

    // The database as it will be written.
    private readonly List<byte[]> locTexts = new(), objTexts = new(), msgTexts = new(), sysTexts = new();
    private readonly List<byte[]> connections = new();
    private readonly List<(string Word, int Number)> words = new();
    private readonly List<byte> objStart = new(), objWords = new();
    private readonly List<(int Verb, int Noun, byte[] Handler)> responses = new(), processes = new();

    private readonly Dictionary<string, int> roomNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> objNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> newMessages = new();
    private readonly Dictionary<string, int> flagOfVariable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> tokens = new();   // compression dictionary: index 0 = token 165
    private bool compressed;

    public QuillWriter(Adventure a, byte[] original, string ext, bool alwaysRebuild)
    {
        this.a = a;
        this.original = original;
        this.ext = ext;
        this.alwaysRebuild = alwaysRebuild;
        db = QuillImporter.Locate(original, a.Origin!.FileName)!;
        baseline = ImporterRegistry.Run(new QuillImporter(), original, a.Origin.FileName).Adventure;
        late = db.Version > 0;
        h = db.HeaderAddress + (late ? 1 : 0);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, AdventurePackage.JsonOptions);

    public ExportResult Run()
    {
        if (!alwaysRebuild && Unchanged())
            return new ExportResult((byte[])original.Clone()) { Summary = $"{db.LocationCount} locations, {db.ObjectCount} objects, {db.MessageCount} messages (unchanged)" };

        LoadDictionary();
        CheckGame();
        Vocabulary();
        Rooms();
        Items();
        Texts();
        Tables();
        var memory = Layout();

        var snap = SpectrumSnapshot.Load(original, ext);
        Array.Copy(memory, 0x4000, snap.Memory, 0x4000, 0xC000);
        CopyToBanks(snap);
        var result = new ExportResult(snap.Save(original))
        {
            Summary = $"{locTexts.Count} locations, {objTexts.Count} objects, {msgTexts.Count} messages, {responses.Count + processes.Count} table entries",
        };
        warnings.CopyTo(result.Warnings);
        return result;
    }

    /// <summary>True when nothing that is stored in a Quill database has changed.</summary>
    private bool Unchanged()
    {
        string Strip(Adventure x)
        {
            var json = Json(x);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
            doc.Remove("origin"); doc.Remove("Origin"); doc.Remove("notes"); doc.Remove("Notes");
            return JsonSerializer.Serialize(doc);
        }
        return Strip(a) == Strip(baseline);
    }

    // ================================================================ whole game

    private void CheckGame()
    {
        if (a.Title != baseline.Title || a.Author != baseline.Author || a.Description != baseline.Description || a.Introduction != baseline.Introduction)
            warnings.Add("The title, author, description and introduction aren't stored in Quill databases");
        if (a.Puzzles.Count > 0 || a.RandomEvents.Count > 0) warnings.Add("Puzzles and random events have no Quill equivalent");
        if (a.Sounds.Count > 0) warnings.Add("Sounds have no Quill equivalent");
        if (!string.Equals(a.StartRoomId, "r0", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Quill games always start at location 0 (the first room)");

        // Settings: the carrying limit and colours are stored; nothing else is.
        string Other(GameSettings x)
        {
            var c = JsonSerializer.Deserialize<GameSettings>(Json(x), AdventurePackage.JsonOptions)!;
            c.MaxCarriedItems = 0; c.TextColor = ""; c.BackgroundColor = "";
            return Json(c);
        }
        if (Other(a.Settings) != Other(baseline.Settings)) warnings.Add("Game settings other than the carrying limit and colours have no Quill equivalent");

        foreach (var v in a.Variables)
            if (v.InitialValue != 0 && baseline.FindVariable(v.Name)?.InitialValue != v.InitialValue)
                warnings.Add("Quill flags always start at 0; variables' starting values were ignored");

        // Pictures stay as the Illustrator drew them.
        var pics = a.Pictures.ToDictionary(p => p.Id, Json, StringComparer.OrdinalIgnoreCase);
        var basePics = baseline.Pictures.ToDictionary(p => p.Id, Json, StringComparer.OrdinalIgnoreCase);
        if (pics.Count != basePics.Count || pics.Any(p => !basePics.TryGetValue(p.Key, out var b) || b != p.Value))
            warnings.Add("Edited or new pictures can't be written back as Illustrator drawings yet; the original pictures were kept");
    }

    // ================================================================ text

    private void LoadDictionary()
    {
        // Compressed databases use bytes 165-255 as dictionary tokens; new text uses the same dictionary.
        var raw = new List<byte[]>();
        for (int i = 0; i < db.LocationCount; i++) raw.Add(db.RawText(db.Word(db.LocationTextTable + 2 * i)));
        for (int i = 0; i < db.MessageCount; i++) raw.Add(db.RawText(db.Word(db.MessageTextTable + 2 * i)));
        compressed = late && db.Dictionary >= 0 && db[db.Dictionary] == 0x80 && raw.Any(t => t.Any(b => 0xFF - b > 164));
        if (!compressed) return;
        int d = db.Dictionary;
        for (int skip = 1; skip > 0; d++) if ((db[d] & 0x80) != 0) skip--;   // entry 0: the 80h marker
        for (int t = 165; t <= 255; t++)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            for (int guard = 0; guard < 64; guard++)
            {
                int b = db[d++];
                int ch = b & 0x7F;
                if (ch < 32 || ch > 126) ok = false;
                sb.Append((char)ch);
                if ((b & 0x80) != 0) break;
            }
            tokens.Add(ok && sb.Length >= 2 ? sb.ToString() : "");
        }
    }

    /// <summary>
    /// Encodes text as the Quill stores it: laid out for the 32 column screen (words that don't fit are moved to
    /// the next line with padding, as Quill authors did), compressed with the game's dictionary, complemented and
    /// terminated by 1Fh.
    /// </summary>
    internal byte[] Encode(string text)
    {
        var laid = new System.Text.StringBuilder();
        foreach (var (line, index) in text.Replace("\r", "").Split('\n').Select((l, i) => (l, i)))
        {
            if (index > 0) laid.Append('\r');
            int col = 0;
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (col > 0 && col + 1 + word.Length > 32) { laid.Append(' ', 32 - col); col = 0; }
                else if (col > 0) { laid.Append(' '); col++; }
                laid.Append(word);
                col = (col + word.Length) % 32;
            }
        }
        var s = laid.ToString();
        var bytes = new List<byte>();
        for (int i = 0; i < s.Length;)
        {
            if (compressed)
            {
                int best = -1, bestLen = 0;
                for (int t = 0; t < tokens.Count; t++)
                    if (tokens[t].Length > bestLen && string.CompareOrdinal(s, i, tokens[t], 0, tokens[t].Length) == 0) { best = t; bestLen = tokens[t].Length; }
                if (best >= 0) { bytes.Add((byte)(0xFF - (165 + best))); i += bestLen; continue; }
            }
            char c = s[i++];
            int code = c switch { '\r' => 0x0D, '£' => 96, '©' => 127, >= ' ' and <= '~' => c, _ => '?' };
            bytes.Add((byte)(0xFF - code));
        }
        bytes.Add(0xFF - 0x1F);
        return bytes.ToArray();
    }

    private static string Clean(string s) => QuillDatabase.Normalise(s);

    /// <summary>An empty text: just the terminator.</summary>
    private static byte[] Empty() => new byte[] { 0xFF - 0x1F };

    // ================================================================ vocabulary

    private static string Key(string word) => (word.Length > 4 ? word[..4] : word).ToUpperInvariant();

    private int? Number(string word)
    {
        var key = Key(word);
        foreach (var (w, n) in words) if (w == key) return n;
        return null;
    }

    private int NewNumber()
    {
        var used = words.Select(w => w.Number).ToHashSet();
        for (int n = 2; n < 255; n++) if (!used.Contains(n)) return n;
        throw new InvalidOperationException("The Quill vocabulary is full (254 word numbers are in use).");
    }

    /// <summary>The word number for a word, adding it to the vocabulary (as a new word, or as a synonym of <paramref name="synonymOf"/>).</summary>
    private int WordNumber(string word, int? synonymOf = null)
    {
        if (Number(word) is { } n) return n;
        int number = synonymOf ?? NewNumber();
        if (Key(word).Any(c => c < 32 || c > 126) || Key(word).Length == 0) throw new InvalidOperationException($"\"{word}\" can't be a Quill word.");
        words.Add((Key(word), number));
        return number;
    }

    private void Vocabulary()
    {
        foreach (var (w, n) in db.Words) words.Add((w.ToUpperInvariant(), n));
        var voc = a.Vocabulary;
        var bvoc = baseline.Vocabulary;

        // Words the author removed from every word list.
        var present = new HashSet<string>(voc.Nouns.Concat(voc.Verbs.SelectMany(v => v.Words.Append(v.Id))).Concat(voc.Directions.Keys)
            .Concat(voc.Adjectives).Concat(voc.Adverbs).Concat(voc.Prepositions).Select(Key));
        var basePresent = new HashSet<string>(bvoc.Nouns.Concat(bvoc.Verbs.SelectMany(v => v.Words.Append(v.Id))).Concat(bvoc.Directions.Keys)
            .Concat(bvoc.Adjectives).Concat(bvoc.Adverbs).Concat(bvoc.Prepositions).Select(Key));
        words.RemoveAll(w => basePresent.Contains(w.Word) && !present.Contains(w.Word));

        // New verb words join their verb's number (or a new one for a new verb).
        foreach (var v in voc.Verbs)
        {
            var all = v.Words.Append(v.Id).ToList();
            int? number = all.Select(Number).FirstOrDefault(n => n != null);
            foreach (var w in all.Where(w => Regex.IsMatch(w, "^[A-Za-z0-9]+$")))
                number = WordNumber(w, number);
        }
        // New direction words join their direction.
        foreach (var (w, dir) in voc.Directions)
        {
            if (Number(w) != null || !Regex.IsMatch(w, "^[A-Za-z0-9]+$")) continue;
            var same = voc.Directions.Where(d => d.Value == dir).Select(d => Number(d.Key)).FirstOrDefault(n => n != null);
            WordNumber(w, same);
        }
        // Item nouns share a number per item; other new nouns get their own.
        foreach (var item in a.Items)
        {
            int? number = item.Nouns.Select(Number).FirstOrDefault(n => n != null);
            foreach (var w in item.Nouns.Where(w => Regex.IsMatch(w, "^[A-Za-z0-9]+$"))) number = WordNumber(w, number);
        }
        foreach (var w in voc.Nouns.Where(w => Regex.IsMatch(w, "^[A-Za-z0-9]+$"))) WordNumber(w);
        if (voc.Adjectives.Concat(voc.Adverbs).Concat(voc.Prepositions).Any(w => Number(w) == null))
            warnings.Add("Adjectives, adverbs and prepositions aren't used by the Quill; new ones were left out");
    }

    /// <summary>Word number for a direction name.</summary>
    private int DirectionNumber(string direction)
    {
        foreach (var (w, dir) in a.Vocabulary.Directions)
            if (string.Equals(dir, direction, StringComparison.OrdinalIgnoreCase) && Number(w) is { } n) return n;
        return WordNumber(direction);
    }

    // ================================================================ rooms and objects

    private void Rooms()
    {
        for (int i = 0; i < db.LocationCount; i++) roomNumbers[$"r{i}"] = i;
        var kept = a.Rooms.Where(r => roomNumbers.ContainsKey(r.Id) && baseline.FindRoom(r.Id) != null).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in roomNumbers.Keys.Where(k => !kept.Contains(k)).ToList()) roomNumbers.Remove(id);
        int next = db.LocationCount;
        foreach (var r in a.Rooms.Where(r => !roomNumbers.ContainsKey(r.Id))) roomNumbers[r.Id] = next++;
        if (next > 252) throw new InvalidOperationException($"The game has {next} rooms; the Quill allows 252.");

        for (int i = 0; i < next; i++) { locTexts.Add(Empty()); connections.Add(new byte[] { 0xFF }); }
        for (int i = 0; i < db.LocationCount; i++)
            if (!kept.Contains($"r{i}")) warnings.Add("Deleted rooms keep their number in the Quill database, as empty locations");

        foreach (var r in a.Rooms)
        {
            int n = roomNumbers[r.Id];
            var b = n < db.LocationCount ? baseline.FindRoom(r.Id) : null;
            locTexts[n] = b != null && b.Description == r.Description ? db.RawText(db.Word(db.LocationTextTable + 2 * n)) : Encode(r.Description);
            if (b != null && b.Name != r.Name && b.Description == r.Description)
                warnings.Add("Quill locations have no separate name (the name comes from the description); renamed rooms kept their description");

            if (b != null && Json(b.Exits) == Json(r.Exits))
                connections[n] = db.Connections[n].SelectMany(c => new[] { (byte)c.Word, (byte)c.Target }).Append((byte)0xFF).ToArray();
            else
            {
                var list = new List<byte>();
                foreach (var e in r.Exits)
                {
                    if (!roomNumbers.TryGetValue(e.TargetRoomId, out int target)) { warnings.Add("Exits to missing rooms were left out"); continue; }
                    if (e.Conditions.Count > 0 || e.DoorItemId != null || e.BlockedMessage.Length > 0 || e.TravelMessage.Length > 0)
                        warnings.Add("Quill connections are always open; exit conditions, doors and messages need table entries instead");
                    list.Add((byte)DirectionNumber(e.Direction));
                    list.Add((byte)target);
                }
                list.Add(0xFF);
                connections[n] = list.ToArray();
            }
            if (b != null)
            {
                string Other(Room x) => Json(new Room { ShortDescription = x.ShortDescription, IsDark = x.IsDark, SoundId = x.SoundId, LoopSound = x.LoopSound, ScoreOnFirstVisit = x.ScoreOnFirstVisit });
                if (Other(b) != Other(r)) warnings.Add("Room darkness, sounds, short descriptions and scores have no Quill equivalent (darkness is flag 0)");
                if (b.PictureId != r.PictureId) warnings.Add("Room pictures can't be changed; each location keeps its Illustrator picture");
            }
            else if (r.PictureId != null || r.IsDark || r.SoundId != null || r.ScoreOnFirstVisit != 0)
                warnings.Add("Room darkness, sounds, short descriptions and scores have no Quill equivalent (darkness is flag 0)");
        }
    }

    private int LocationNumber(string? loc, string what)
    {
        switch (loc)
        {
            case null or "": return 252;
            case Locations.Worn: return 253;
            case Locations.Carried: return 254;
            case Locations.Here: return 255;
        }
        if (roomNumbers.TryGetValue(loc, out int n)) return n;
        warnings.Add($"{what}: the Quill has no containers or special places; the object was left out of play");
        return 252;
    }

    private void Items()
    {
        for (int i = 0; i < db.ObjectCount; i++) objNumbers[$"o{i}"] = i;
        var kept = a.Items.Where(x => objNumbers.ContainsKey(x.Id) && baseline.FindItem(x.Id) != null).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in objNumbers.Keys.Where(k => !kept.Contains(k)).ToList()) objNumbers.Remove(id);
        int next = db.ObjectCount;
        foreach (var x in a.Items.Where(x => !objNumbers.ContainsKey(x.Id))) objNumbers[x.Id] = next++;
        if (next > 255) throw new InvalidOperationException($"The game has {next} objects; the Quill allows 255.");

        for (int i = 0; i < next; i++) { objTexts.Add(Empty()); objStart.Add(252); objWords.Add(255); }
        for (int i = 0; i < db.ObjectCount; i++)
            if (!kept.Contains($"o{i}")) warnings.Add("Deleted objects keep their number in the Quill database, as objects that are never created");

        foreach (var x in a.Items)
        {
            int n = objNumbers[x.Id];
            var b = n < db.ObjectCount ? baseline.FindItem(x.Id) : null;
            if (b != null && b.Name == x.Name && b.Article == x.Article && b.Description == x.Description)
                objTexts[n] = db.RawText(db.Word(db.ObjectTextTable + 2 * n));
            else if (b != null && b.Description != x.Description && x.Description.Length > 0 && b.Name == x.Name && b.Article == x.Article)
                objTexts[n] = Encode(x.Description);
            else
            {
                var text = (x.Article.Length > 0 ? x.Article + " " : "") + x.Name;
                if (b != null && b.Description.TrimEnd().EndsWith('.') && !text.EndsWith('.')) text += ".";
                objTexts[n] = Encode(text);
                if (b == null && x.Description.Length > 0)
                    warnings.Add("Quill objects are described only by their name; new objects' descriptions need a table entry for EXAMINE");
            }

            objStart[n] = b != null && b.Location == x.Location ? db.ObjectStart[n] : (byte)LocationNumber(x.Location, x.Name);
            if (db.ObjectWords is { } ow)
                objWords[n] = b != null && Json(b.Nouns) == Json(x.Nouns) ? ow[n] : x.Nouns.Select(Number).FirstOrDefault(v => v != null) is { } w ? (byte)w : (byte)255;
            else if (b == null || Json(b.Nouns) != Json(x.Nouns))
                warnings.Add("Early Quill games have no object words; object nouns only work through table entries");

            string Other(Item i) => Json(new Item { Wearable = i.Wearable, Portable = i.Portable, Scenery = i.Scenery, Weight = i.Weight, Container = i.Container, Edible = i.Edible, LightSource = i.LightSource, Openable = i.Openable, Lockable = i.Lockable, ReadText = i.ReadText, RoomDescription = i.RoomDescription });
            if (b != null ? Other(b) != Other(x) : Other(x) != Other(new Item { Portable = true, Wearable = x.Wearable, LightSource = n == 0 }))
                warnings.Add("Object properties (wearable, weight, containers, light…) have no Quill equivalent; table entries decide what objects do");
        }
    }

    // ================================================================ messages

    private void Texts()
    {
        for (int i = 0; i < db.MessageCount; i++) msgTexts.Add(db.RawText(db.Word(db.MessageTextTable + 2 * i)));

        // System messages: sysN, or an edit to a message the importer mapped from a single system message.
        var mapped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Msg.Dark] = 0, [Msg.DontUnderstand] = 6, [Msg.UnknownWord] = 6, [Msg.CantGo] = 7, [Msg.CantDoThat] = 8, [Msg.Ok] = 15,
        };
        foreach (var (k, n) in late
                     ? new[] { (Msg.NotWorn, 23), (Msg.AlreadyCarrying, 25), (Msg.NotHere, 26), (Msg.TooMany, 27), (Msg.NotCarrying, 28) }
                     : new[] { (Msg.NotWorn, 20), (Msg.AlreadyCarrying, 22), (Msg.NotHere, 23), (Msg.TooMany, 24), (Msg.NotCarrying, 25) })
            mapped[k] = n;

        var sys = Enumerable.Range(0, db.SystemMessageCount).Select(i => (string?)null).ToArray();
        foreach (var (key, value) in a.Messages)
        {
            baseline.Messages.TryGetValue(key, out var old);
            if (old == value) continue;
            var m = Regex.Match(key, @"^sys(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) < sys.Length) sys[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = value;
            else if (mapped.TryGetValue(key, out int n) && n < sys.Length) sys[n] ??= value;
            else warnings.Add($"The \"{key}\" message isn't a single Quill system message; edit the sys messages instead");
        }
        foreach (var key in baseline.Messages.Keys.Where(k => !a.Messages.ContainsKey(k)))
            warnings.Add("Removed system messages were kept (the Quill needs all of them)");

        if (late)
        {
            for (int i = 0; i < db.SystemMessageCount; i++)
                sysTexts.Add(sys[i] is { } text ? Encode(text) : db.RawText(db.Word(db.SystemMessageBase + 2 * i)));
        }
        else if (sys.Any(s => s != null))
        {
            // Early games keep their system messages inside the runtime: rewrite them in place if they fit.
            int start = db.SystemMessageBase, end = start;
            var all = new List<byte>();
            for (int i = 0; i < db.SystemMessageCount; i++)
            {
                var raw = db.RawText(end);
                end += raw.Length;
                all.AddRange(sys[i] is { } text ? Encode(text) : raw);
            }
            if (all.Count <= end - start) earlySystemMessages = (start, all.ToArray());
            else warnings.Add($"The edited system messages are {all.Count - (end - start)} bytes too long for an early Quill runtime; the original ones were kept");
        }
    }

    private (int Address, byte[] Bytes)? earlySystemMessages;

    private int MessageNumber(string text)
    {
        var clean = Clean(text);
        for (int i = 0; i < db.MessageCount; i++) if (db.Messages[i] == clean) return i;
        if (newMessages.TryGetValue(clean, out int n)) return n;
        msgTexts.Add(Encode(text));
        if (msgTexts.Count > 256) throw new InvalidOperationException("The game has more than 256 messages; the Quill allows 256.");
        return newMessages[clean] = msgTexts.Count - 1;
    }

    // ================================================================ tables

    private void Tables()
    {
        var baseTriggers = baseline.Triggers.ToDictionary(t => t.Id, Json, StringComparer.OrdinalIgnoreCase);
        foreach (var t in a.Triggers)
        {
            bool same = baseTriggers.TryGetValue(t.Id, out var b) && b == Json(t);
            if (Regex.IsMatch(t.Id, @"^flagtimer\d+$"))
            {
                if (!same) warnings.Add("The Quill decrements flags 2-10 itself; changes to the flag timer triggers were ignored");
                continue;
            }
            var m = Regex.Match(t.Id, @"^(resp|proc)(\d+)$");
            if (same && m.Success)
            {
                var e = (m.Groups[1].Value == "resp" ? db.Responses : db.Processes)[int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)];
                (m.Groups[1].Value == "resp" ? responses : processes).Add((e.Verb, e.Noun, db.Memory.AsSpan(e.Handler, e.HandlerLength).ToArray()));
                continue;
            }
            Compile(t);
        }
        foreach (var id in baseTriggers.Keys.Where(k => Regex.IsMatch(k, @"^flagtimer\d+$") && a.FindTrigger(k) == null))
            warnings.Add("The Quill decrements flags 2-10 itself; changes to the flag timer triggers were ignored");
    }

    private void Compile(Trigger t)
    {
        List<(int Verb, int Noun, byte[] Handler)> table;
        switch (t.Event)
        {
            case TriggerEvent.BeforeCommand: table = responses; break;
            case TriggerEvent.EveryTurn: table = processes; break;
            case TriggerEvent.AfterCommand:
                table = processes;
                warnings.Add("After-command triggers became process table entries, which run after every turn");
                break;
            default:
                warnings.Add($"{t.Event} triggers have no Quill equivalent (only the response and process tables)");
                return;
        }
        if (!t.Enabled) { warnings.Add("Disabled triggers were left out"); return; }
        if (t.OnceOnly) warnings.Add("Once-only triggers need a flag in the Quill; they run every time");
        if (!string.IsNullOrEmpty(t.Noun2) && t.Noun2 != "*" || !string.IsNullOrEmpty(t.Adverb) || !string.IsNullOrEmpty(t.Preposition))
            warnings.Add("The Quill matches only a verb and a noun; second nouns, adverbs and prepositions were ignored");

        var cond = new List<byte>();
        if (t.RoomId != null)
        {
            if (roomNumbers.TryGetValue(t.RoomId, out int room)) cond.AddRange(new byte[] { 0, (byte)room });
            else warnings.Add("Triggers for missing rooms were left out");
        }
        foreach (var c in t.Conditions) Condition(c, cond);
        var act = new List<byte>();
        for (int i = 0; i < t.Actions.Count; i++)
        {
            var x = t.Actions[i];
            // INVEN and DESC end the table themselves (the importer added a Done after them).
            if (x.Type is ActionType.Inventory or ActionType.Look && i + 1 < t.Actions.Count && t.Actions[i + 1].Type == ActionType.Done) i++;
            Action(x, act);
        }
        var handler = cond.Append((byte)0xFF).Concat(act).Append((byte)0xFF).ToArray();

        var verbs = table == processes ? new List<int> { 255 } : Words(t.Verb, true);
        var nouns = table == processes ? new List<int> { 255 } : Words(t.Noun1, false);
        foreach (var v in verbs)
            foreach (var n in nouns)
                table.Add((v, n, handler));
    }

    /// <summary>Word numbers for a verb or noun pattern ("*" = any, alternatives separated by |, item ids allowed).</summary>
    private List<int> Words(string? pattern, bool verb)
    {
        if (string.IsNullOrEmpty(pattern) || pattern is "*" or "-")
        {
            if (pattern == "-") warnings.Add("The Quill can't require that no noun was typed; \"-\" matches any noun");
            return new List<int> { 255 };
        }
        var result = new List<int>();
        foreach (var p in pattern.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int n;
            if (!verb && a.FindItem(p) is { } item)
                n = item.Nouns.Select(Number).FirstOrDefault(x => x != null) ?? WordNumber(item.Name.Split(' ')[^1]);
            else if (verb && a.Vocabulary.Verbs.FirstOrDefault(d => string.Equals(d.Id, p, StringComparison.OrdinalIgnoreCase)) is { } def)
                n = def.Words.Append(def.Id).Select(Number).FirstOrDefault(x => x != null) ?? WordNumber(def.Id);
            else if (a.Vocabulary.Directions.Values.Contains(p, StringComparer.OrdinalIgnoreCase) && Number(p) == null)
                n = DirectionNumber(p);
            else n = WordNumber(p);
            if (!result.Contains(n)) result.Add(n);
        }
        return result;
    }

    /// <summary>The Quill flag for a variable: fN, a system flag, or a free flag (11-27) for a new variable; null if none is free.</summary>
    private int? Flag(string variable)
    {
        switch (variable)
        {
            case "@carried": return 1;
            case "@score": return 30;
            case "@turns": return 31;
            case "@room": return 35;
        }
        var m = Regex.Match(variable, @"^f(\d+)$");
        if (m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var f and < 256) return f;
        if (flagOfVariable.TryGetValue(variable, out int flag)) return flag;
        var used = a.Variables.Concat(baseline.Variables).Select(v => Regex.Match(v.Name, @"^f(\d+)$")).Where(x => x.Success)
            .Select(x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)).Concat(flagOfVariable.Values).ToHashSet();
        for (int n = 11; n <= 27; n++)
            if (!used.Contains(n)) return flagOfVariable[variable] = n;
        warnings.Add("There were no free Quill flags (11-27) left for new variables; their conditions and actions were left out");
        return null;
    }

    private void Condition(Condition c, List<byte> o)
    {
        void Op(int op, params int[] args) { o.Add((byte)op); foreach (var x in args) o.Add((byte)x); }
        int Obj(string? id)
        {
            if (id != null && objNumbers.TryGetValue(id, out int n)) return n;
            warnings.Add("Conditions about the typed noun or missing objects have no Quill equivalent");
            return 0;
        }
        bool neg = c.Negate;
        switch (c.Type)
        {
            case ConditionType.PlayerIn when roomNumbers.TryGetValue(c.A ?? "", out int r): Op(neg ? 1 : 0, r); return;
            case ConditionType.ItemPresent: Op(neg ? 5 : 4, Obj(c.A)); return;
            case ConditionType.ItemWorn: Op(neg ? 7 : 6, Obj(c.A)); return;
            case ConditionType.ItemCarried:
            case ConditionType.ItemIn when c.B == Locations.Carried:
                Op(neg ? 9 : 8, Obj(c.A)); return;
            case ConditionType.Chance when !neg: Op(10, Math.Clamp(c.N, 0, 255)); return;
            case ConditionType.VarGreater when c.A == "@room" && !neg: Op(2, c.N); return;
            case ConditionType.VarLess when c.A == "@room" && !neg: Op(3, c.N); return;
            case ConditionType.VarEquals or ConditionType.VarGreater or ConditionType.VarLess when c.A != null && Flag(c.A) is not { } _: return;
            case ConditionType.VarEquals when c.N == 0: Op(neg ? 12 : 11, Flag(c.A!)!.Value); return;
            case ConditionType.VarEquals when !neg && c.N is > 0 and < 256: Op(13, Flag(c.A!)!.Value, c.N); return;
            case ConditionType.VarGreater when !neg && c.N is >= 0 and < 255: Op(14, Flag(c.A!)!.Value, c.N); return;
            case ConditionType.VarGreater when neg && c.N is >= 0 and < 255: Op(15, Flag(c.A!)!.Value, c.N + 1); return;
            case ConditionType.VarLess when !neg && c.N is > 0 and < 256: Op(15, Flag(c.A!)!.Value, c.N); return;
            case ConditionType.VarLess when neg && c.N is > 0 and < 256: Op(14, Flag(c.A!)!.Value, c.N - 1); return;
            case ConditionType.Always when !neg: return;
        }
        warnings.Add($"{(neg ? "Negated " : "")}{c.Type} conditions have no Quill equivalent and were left out");
    }

    private void Action(GameAction x, List<byte> o)
    {
        bool Op(int op, params int[] args)
        {
            int raw = db.DenormaliseAction(op);
            if (raw < 0)
            {
                warnings.Add($"{QuillDatabase.ActionNames[op]} doesn't exist in this Quill version");
                return false;
            }
            o.Add((byte)raw);
            foreach (var v in args) o.Add((byte)Math.Clamp(v, 0, 255));
            return true;
        }
        int? Obj(string? id)
        {
            if (id != null && objNumbers.TryGetValue(id, out int n)) return n;
            if (id != null && !id.StartsWith('$')) warnings.Add("Actions on missing objects were left out");
            return null;
        }
        void Auto(string? id, int auto, int op)
        {
            if (id == "$noun1") Op(auto);
            else if (Obj(id) is { } n) Op(op, n);
        }

        switch (x.Type)
        {
            case ActionType.Inventory: Op(QuillDatabase.A_INVEN); return;
            case ActionType.Look: Op(QuillDatabase.A_DESC); return;
            case ActionType.Quit: Op(QuillDatabase.A_QUIT); return;
            case ActionType.Lose:
            case ActionType.Win:
                if (!string.IsNullOrWhiteSpace(x.Text) && Clean(x.Text) != Clean(baseline.Messages.GetValueOrDefault("sys13", "")))
                    Op(QuillDatabase.A_MESSAGE, MessageNumber(x.Text));
                if (x.Type == ActionType.Win) warnings.Add("The Quill has no winning ending; Win became END");
                Op(QuillDatabase.A_END); return;
            case ActionType.Done: Op(QuillDatabase.A_DONE); return;
            case ActionType.Ok: Op(QuillDatabase.A_OK); return;
            case ActionType.Pause when x.N == 0: Op(QuillDatabase.A_ANYKEY); return;
            case ActionType.Pause: Op(QuillDatabase.A_PAUSE, x.N >= 256 * 20 ? 0 : Math.Max(1, x.N / 20)); return;
            case ActionType.Save: Op(QuillDatabase.A_SAVE); return;
            case ActionType.Restore: Op(QuillDatabase.A_LOAD); return;
            case ActionType.ShowTurns: Op(QuillDatabase.A_TURNS); return;
            case ActionType.ShowScore: Op(QuillDatabase.A_SCORE); return;
            case ActionType.ClearScreen: Op(QuillDatabase.A_CLS); return;
            case ActionType.DropAll: Op(QuillDatabase.A_DROPALL); return;
            case ActionType.TakeItem: Auto(x.A, QuillDatabase.A_AUTOG, QuillDatabase.A_GET); return;
            case ActionType.DropItem: Auto(x.A, QuillDatabase.A_AUTOD, QuillDatabase.A_DROP); return;
            case ActionType.WearItem: Auto(x.A, QuillDatabase.A_AUTOW, QuillDatabase.A_WEAR); return;
            case ActionType.UnwearItem: Auto(x.A, QuillDatabase.A_AUTOR, QuillDatabase.A_REMOVE); return;
            case ActionType.DestroyItem when Obj(x.A) is { } d: Op(QuillDatabase.A_DESTROY, d); return;
            case ActionType.CreateItem when Obj(x.A) is { } cr:
                if (string.IsNullOrEmpty(x.B) || x.B == Locations.Here) Op(QuillDatabase.A_CREATE, cr);
                else Op(QuillDatabase.A_PLACE, cr, LocationNumber(x.B, "CREATE"));
                return;
            case ActionType.SwapItems when Obj(x.A) is { } s1 && Obj(x.B) is { } s2: Op(QuillDatabase.A_SWAP, s1, s2); return;
            case ActionType.MoveItem when Obj(x.A) is { } mv: Op(QuillDatabase.A_PLACE, mv, LocationNumber(x.B, "PLACE")); return;
            case ActionType.GoTo when roomNumbers.TryGetValue(x.A ?? "", out int room): Op(QuillDatabase.A_GOTO, room); return;
            case ActionType.Message:
                if (x.N == 1) warnings.Add("Quill messages always end with a new line");
                Op(QuillDatabase.A_MESSAGE, MessageNumber(x.Text ?? "")); return;
            case ActionType.SetVar or ActionType.AddVar when x.A != null && Flag(x.A) is not { } _: return;
            case ActionType.SetVar when x.A != null:
            {
                int f = Flag(x.A)!.Value;
                if (x.N == 0) Op(QuillDatabase.A_CLEAR, f);
                else if (x.N == 255) Op(QuillDatabase.A_SET, f);
                else Op(QuillDatabase.A_LET, f, x.N);
                return;
            }
            case ActionType.AddVar when x.A != null:
                Op(x.N < 0 ? QuillDatabase.A_MINUS : QuillDatabase.A_PLUS, Flag(x.A)!.Value, Math.Abs(x.N)); return;
            case ActionType.AwardScore:
                Op(x.N < 0 ? QuillDatabase.A_MINUS : QuillDatabase.A_PLUS, 30, Math.Abs(x.N)); return;
            case ActionType.Beep:
                Op(QuillDatabase.A_BEEP, x.N, int.TryParse(x.B, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pitch) ? pitch : 0); return;
            case ActionType.Restart when late:
                Op(QuillDatabase.A_LET, 28, 12); Op(QuillDatabase.A_PAUSE, 0); return;
        }
        warnings.Add($"{x.Type} actions have no Quill equivalent and were left out");
    }

    // ================================================================ memory

    /// <summary>The address range the original database used: [lowest, highest) and how far it may grow.</summary>
    private (int Low, int High, (int Start, int End) Spare) Region()
    {
        int low = int.MaxValue, high = 0;
        void Span(int start, int length) { low = Math.Min(low, start); high = Math.Max(high, start + length); }
        void Texts(int table, int count)
        {
            Span(table, 2 * count);
            for (int i = 0; i < count; i++) { int p = db.Word(table + 2 * i); Span(p, db.RawText(p).Length); }
        }
        Span(db.ResponseTable, 4 * db.Responses.Count + 1);
        Span(db.ProcessTable, 4 * db.Processes.Count + 1);
        foreach (var e in db.Responses.Concat(db.Processes)) Span(e.Handler, e.HandlerLength);
        Texts(db.ObjectTextTable, db.ObjectCount);
        Texts(db.LocationTextTable, db.LocationCount);
        Texts(db.MessageTextTable, db.MessageCount);
        if (late) Texts(db.SystemMessageBase, db.SystemMessageCount);
        Span(db.ConnectionTable, 2 * db.LocationCount);
        for (int i = 0; i < db.LocationCount; i++) Span(db.Word(db.ConnectionTable + 2 * i), 2 * db.Connections[i].Count + 1);
        Span(db.VocabularyTable, 5 * db.Words.Count + 5);
        Span(db.ObjectStartTable, db.ObjectCount + 1);
        if (db.ObjectWordTable >= 0) Span(db.ObjectWordTable, db.ObjectCount + 1);
        int free = db.Word(late ? h + 24 : h + 20);
        if (free > high && free < high + 16) high = free;

        return (low, high, SpareMemory(high));
    }

    /// <summary>
    /// A block of memory above the database that is clearly unused, for text that no longer fits in the database's
    /// own space. What follows the database is left alone (custom fonts and the runtime's workspace often live
    /// there), and so are the UDGs, the pictures and the machine stack; the block must be all zeros, with a margin.
    /// </summary>
    private (int Start, int End) SpareMemory(int high)
    {
        int limit = 0x10000;
        int font = db.Word(23606) + 256, udg = db.Word(23675), sp = StackPointer();
        if (udg >= high) limit = Math.Min(limit, udg);
        if (sp > high) limit = Math.Min(limit, sp - 512);
        if (IllustratorGraphics.TryLoad(db) != null)
        {
            int gfxLow = Math.Min(db.Word(IllustratorGraphics.ControlBlock), db.Word(IllustratorGraphics.ControlBlock + 2));
            int ptrs = db.Word(IllustratorGraphics.ControlBlock);
            for (int n = 0; n < db[IllustratorGraphics.ControlBlock + 6]; n++) gfxLow = Math.Min(gfxLow, db.Word(ptrs + 2 * n));
            if (gfxLow < high) throw new InvalidOperationException("The pictures share memory with the game's text, so the database can't be rebuilt safely.");
            limit = Math.Min(limit, Math.Min(gfxLow, IllustratorGraphics.ControlBlock));
        }
        (int Start, int End) best = (0, 0);
        for (int a = high + 1024; a < limit;)
        {
            if (db[a] != 0 || a >= font && a < font + 768) { a++; continue; }
            int start = a;
            while (a < limit && db[a] == 0 && !(a >= font && a < font + 768)) a++;
            if (a - start > best.End - best.Start) best = (start, a);
        }
        const int margin = 128;
        return best.End - best.Start > 2 * margin ? (best.Start + margin, best.End - margin) : (0, 0);
    }

    /// <summary>The Z80's stack pointer in the snapshot (0 counts as the top of memory).</summary>
    private int StackPointer()
    {
        int sp = ext == "sna" ? original[23] | original[24] << 8 : original[8] | original[9] << 8;
        return sp == 0 ? 0x10000 : sp;
    }

    private byte[] Layout()
    {
        var mem = (byte[])db.Memory.Clone();
        var (low, high, spare) = Region();
        // Below the database: the header and (compressed games) the dictionary of 91 words after its 80h marker.
        int dictionaryEnd = late ? h + 28 : h + 24;
        if (compressed)
        {
            dictionaryEnd = db.Dictionary;
            for (int entries = 0; entries < 92 && dictionaryEnd < low; dictionaryEnd++) if ((db[dictionaryEnd] & 0x80) != 0) entries++;
        }
        if (low < dictionaryEnd) throw new InvalidOperationException($"The Quill database layout wasn't recognised (it starts at {low:X4}), so it can't be rebuilt safely.");

        // Everything goes in the database's own space; if it no longer fits, the longest texts move to spare memory.
        var allTexts = objTexts.Concat(locTexts).Concat(msgTexts).Concat(sysTexts).ToList();
        int size = responses.Concat(processes).Sum(e => e.Handler.Length + 4) + 2
                   + allTexts.Sum(t => t.Length + 2) + connections.Sum(c => c.Length + 2)
                   + 5 * words.Count + 5 + objStart.Count + 1 + (db.ObjectWords != null ? objWords.Count + 1 : 0);
        var spilled = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        int over = size - (high - low), moved = 0;
        foreach (var t in allTexts.OrderByDescending(t => t.Length))
        {
            if (moved >= over) break;
            if (moved + t.Length > spare.End - spare.Start) continue;
            spilled.Add(t);
            moved += t.Length;
        }
        if (moved < over) throw new InvalidOperationException($"The game is {over - moved} bytes too big for the Spectrum's memory.");
        if (moved > 0)
            warnings.Add($"The game no longer fits in its original space, so {spilled.Count} text{(spilled.Count == 1 ? "" : "s")} ({moved} bytes) were moved to unused memory at {spare.Start:X4}h; check the game in an emulator");

        int at = low, spareAt = spare.Start;
        int Put(byte[] bytes)
        {
            if (spilled.Contains(bytes))
            {
                bytes.CopyTo(mem, spareAt);
                spareAt += bytes.Length;
                return spareAt - bytes.Length;
            }
            int start = at;
            if (at + bytes.Length <= 0x10000) bytes.CopyTo(mem, at);
            at += bytes.Length;
            return start;
        }
        byte[] Words16(IEnumerable<int> values) => values.SelectMany(v => new[] { (byte)v, (byte)(v >> 8) }).ToArray();
        int Table(List<(int Verb, int Noun, byte[] Handler)> entries)
        {
            var handlers = entries.Select(e => Put(e.Handler)).ToList();
            int table = at;
            for (int i = 0; i < entries.Count; i++) Put(new[] { (byte)entries[i].Verb, (byte)entries[i].Noun, (byte)handlers[i], (byte)(handlers[i] >> 8) });
            Put(new byte[] { 0 });
            return table;
        }
        int TextTable(List<byte[]> texts)
        {
            var addresses = texts.Select(Put).ToList();
            return Put(Words16(addresses));
        }

        int resp = Table(responses);
        int proc = Table(processes);
        int objs = TextTable(objTexts);
        int locs = TextTable(locTexts);
        int msgs = TextTable(msgTexts);
        int sys = late ? TextTable(sysTexts) : 0;
        var connAddresses = connections.Select(Put).ToList();
        int conn = Put(Words16(connAddresses));
        int voc = Put(words.SelectMany(w => w.Word.PadRight(4).Select(c => (byte)(0xFF - c)).Append((byte)w.Number)).Concat(new byte[5]).ToArray());
        int pos = Put(objStart.Append((byte)0xFF).ToArray());
        int objw = db.ObjectWords != null ? Put(objWords.Append((byte)0xFF).ToArray()) : 0;
        int end = at;
        if (end > high) throw new InvalidOperationException($"The game is {end - high} bytes too big for the Spectrum's memory.");
        // The end-of-database pointer stays put: runtimes find fonts and their workspace after it.
        int oldEnd = db.Word(late ? h + 24 : h + 20);
        int endPointer = oldEnd == high ? high : end;
        for (int i = end; i < high; i++) mem[i] = 0;

        void Poke16(int address, int value) { mem[address] = (byte)value; mem[address + 1] = (byte)(value >> 8); }
        int hdr = db.HeaderAddress;
        mem[hdr] = (byte)Math.Clamp(a.Settings.MaxCarriedItems == baseline.Settings.MaxCarriedItems ? db.MaxCarried : a.Settings.MaxCarriedItems, 0, 255);
        mem[hdr + 1] = (byte)objTexts.Count;
        mem[hdr + 2] = (byte)locTexts.Count;
        mem[hdr + 3] = (byte)(msgTexts.Count & 0xFF);
        if (msgTexts.Count == 256) warnings.Add("256 messages is the most the Quill can count");
        Poke16(h + 4, resp);
        Poke16(h + 6, proc);
        Poke16(h + 8, objs);
        Poke16(h + 10, locs);
        Poke16(h + 12, msgs);
        if (late)
        {
            Poke16(h + 14, sys);
            Poke16(h + 16, conn);
            Poke16(h + 18, voc);
            Poke16(h + 20, pos);
            Poke16(h + 22, objw);
            Poke16(h + 24, endPointer);
        }
        else
        {
            Poke16(h + 14, conn);
            Poke16(h + 16, voc);
            Poke16(h + 18, pos);
            Poke16(h + 20, endPointer);
        }
        if (earlySystemMessages is { } es) es.Bytes.CopyTo(mem, es.Address);
        Colours(mem);
        return mem;
    }

    private void Colours(byte[] mem)
    {
        var s = a.Settings;
        if (s.TextColor == baseline.Settings.TextColor && s.BackgroundColor == baseline.Settings.BackgroundColor) return;
        int Index(string colour)
        {
            if (colour.Length != 7 || colour[0] != '#' || !uint.TryParse(colour[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return -1;
            return Array.IndexOf(Palettes.Spectrum, 0xFF000000 | rgb);
        }
        int ink = Index(s.TextColor), paper = Index(s.BackgroundColor);
        if (ink < 0 || paper < 0 || (ink >= 8) != (paper >= 8) && ink % 8 != 0 && paper % 8 != 0)
        {
            warnings.Add("The text and background colours must be ZX Spectrum colours (both bright or both normal); the original colours were kept");
            return;
        }
        int sig = db.SignatureAddress;
        mem[sig + 1] = (byte)(ink % 8);
        mem[sig + 3] = (byte)(paper % 8);
        mem[sig + 7] = (byte)(ink >= 8 || paper >= 8 ? 1 : 0);
    }

    /// <summary>128K snapshots keep their RAM in banks: copy the rebuilt 48K view into banks 5, 2 and the paged one.</summary>
    private void CopyToBanks(SpectrumSnapshot snap)
    {
        if (snap.Banks128 is not { } banks) return;
        int paged = snap.Format == "sna128" ? original[49181] & 7 : original[35] & 7;
        void Copy(int bank, int address) { if (banks[bank] != null) Array.Copy(snap.Memory, address, banks[bank], 0, 16384); }
        Copy(5, 0x4000);
        Copy(2, 0x8000);
        Copy(paged, 0xC000);
    }
}

