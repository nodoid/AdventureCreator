using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Snapshots;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Quill;

/// <summary>
/// Imports games written with Gilsoft's The Quill (ZX Spectrum early "A" and later "C" databases; experimental
/// Amstrad CPC and Commodore 64 support) including pictures drawn with The Illustrator.
/// </summary>
public sealed class QuillImporter : IAdventureImporter
{
    public string Name => "The Quill / Illustrator (ZX Spectrum, Amstrad CPC, C64)";
    public IReadOnlyList<string> Extensions => new[] { "sna", "z80", "vsf" };

    public bool CanImport(byte[] data, string fileName)
    {
        try { return Locate(data, fileName) is not null; }
        catch { return false; }
    }

    public ImportResult Import(byte[] data, string fileName)
    {
        var db = Locate(data, fileName)
            ?? throw new InvalidDataException("No Quill database was found in this file.");
        return new QuillConverter(db, fileName).Convert();
    }

    private static readonly byte[] CpcSignature = Encoding.ASCII.GetBytes("MV - SNA");
    private static readonly byte[] ViceSignature = Encoding.ASCII.GetBytes("VICE Snapshot File\x1a");

    /// <summary>Finds and decodes the Quill database in a snapshot, or returns null.</summary>
    internal static QuillDatabase? Locate(byte[] data, string fileName)
    {
        if (StartsWith(data, CpcSignature))
        {
            // CPCEMU snapshot: 256 byte header followed by RAM from address 0. The database starts at 1BD1h.
            if (data.Length < 0x100 + 0xC000) return null;
            var mem = new byte[65536];
            Array.Copy(data, 0x100, mem, 0, Math.Min(65536, data.Length - 0x100));
            return QuillDatabase.TryLoadOther(mem, QuillPlatform.AmstradCpc, 0x1BD1);
        }
        if (StartsWith(data, ViceSignature))
        {
            // VICE snapshot: RAM follows the C64MEM module header (+1Ah). The database starts at 0804h.
            int n = IndexOf(data, Encoding.ASCII.GetBytes("C64MEM"), 0, Math.Min(data.Length, 4096));
            if (n < 0) return null;
            int offset = n + 0x1A;
            if (data.Length < offset + 0xC000) return null;
            var mem = new byte[65536];
            Array.Copy(data, offset, mem, 0, Math.Min(65536, data.Length - offset));
            return QuillDatabase.TryLoadOther(mem, QuillPlatform.Commodore64, 0x804);
        }

        var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        SpectrumSnapshot snap;
        try { snap = SpectrumSnapshot.Load(data, ext); }
        catch (InvalidDataException) { return null; }
        return QuillDatabase.FindSpectrum(snap.Memory);
    }

    private static bool StartsWith(byte[] data, byte[] prefix) =>
        data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static int IndexOf(byte[] data, byte[] pattern, int start, int end)
    {
        for (int i = start; i <= end - pattern.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) return i;
        return -1;
    }
}

/// <summary>Converts a decoded <see cref="QuillDatabase"/> into an <see cref="Adventure"/>.</summary>
internal sealed class QuillConverter
{
    private readonly QuillDatabase db;
    private readonly string fileName;
    private readonly Adventure adv = new();
    private readonly ImportResult result;
    private readonly HashSet<string> warned = new();

    private readonly HashSet<int> verbNumbers = new();
    private readonly HashSet<int> nounNumbers = new();
    private readonly Dictionary<int, string> verbIds = new();
    private readonly SortedSet<int> usedFlags = new();
    private readonly HashSet<int> wearable = new();
    private readonly HashSet<int> wearableWords = new();

    public QuillConverter(QuillDatabase db, string fileName)
    {
        this.db = db;
        this.fileName = fileName ?? "";
        result = new ImportResult(adv);
    }

    private static readonly Dictionary<string, string> ObviousDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "north", ["nort"] = "north", ["north"] = "north",
        ["s"] = "south", ["sout"] = "south", ["south"] = "south",
        ["e"] = "east", ["east"] = "east",
        ["w"] = "west", ["west"] = "west",
        ["ne"] = "northeast", ["nw"] = "northwest", ["se"] = "southeast", ["sw"] = "southwest",
        ["u"] = "up", ["up"] = "up",
        ["d"] = "down", ["down"] = "down",
        ["in"] = "in", ["insi"] = "in",
        ["out"] = "out", ["outs"] = "out",
    };

    private static readonly Dictionary<int, string> FlagDescriptions = new()
    {
        [0] = "Darkness: non-zero means it is dark (object 0 is the light source).",
        [1] = "Number of objects carried (maintained by the runtime).",
        [2] = "Decremented each time a location is described.",
        [3] = "Decremented each time a location is described while dark.",
        [4] = "Decremented each time a location is described while dark and object 0 is absent.",
        [5] = "Decremented every turn.",
        [6] = "Decremented every turn.",
        [7] = "Decremented every turn.",
        [8] = "Decremented every turn.",
        [9] = "Decremented every turn while dark.",
        [10] = "Decremented every turn while dark and object 0 is absent.",
        [28] = "Selects the special function performed by PAUSE (later Quill versions).",
        [29] = "Picture display control (Illustrator).",
        [30] = "Score.",
        [31] = "Turns taken (low byte).",
        [32] = "Turns taken (high byte).",
        [33] = "Word number of the verb typed.",
        [34] = "Word number of the noun typed.",
        [35] = "Current location.",
    };

    public ImportResult Convert()
    {
        var gfx = IllustratorGraphics.TryLoad(db);
        result.DetectedFormat = db.VersionName + (gfx is not null ? " + Illustrator" : "");

        adv.Title = TitleFromFileName(fileName);
        adv.Description = $"Imported from {result.DetectedFormat}.";
        adv.StartRoomId = "r0";
        var s = adv.Settings;
        s.LegacyTableSemantics = true;
        s.DarknessVariable = "f0";
        s.AutoListExits = false;
        s.SignificantLetters = 4;
        s.MaxCarriedItems = db.MaxCarried;
        s.Verbose = true;
        if (db.Platform == QuillPlatform.Spectrum)
        {
            int ink = Math.Clamp(db.Ink, 0, 7) + (db.Bright == 1 ? 8 : 0);
            int paper = Math.Clamp(db.Paper, 0, 7) + (db.Bright == 1 ? 8 : 0);
            if (db.Ink <= 7 && db.Paper <= 7 && db.Ink != db.Paper)
            {
                s.TextColor = Hex(Palettes.Spectrum[ink]);
                s.BackgroundColor = Hex(Palettes.Spectrum[paper]);
            }
        }
        if (db.Platform != QuillPlatform.Spectrum)
            Warn($"{db.VersionName} support is experimental (database position assumed at 0x{db.HeaderAddress:X4}).");

        ClassifyWords();
        BuildVocabulary();
        BuildRooms();
        BuildItems();
        BuildTriggers();
        BuildFlagTimers();
        BuildVariables();
        BuildMessages();
        if (gfx is not null) BuildPictures(gfx);

        Note("Quill tries the current location's connections before the response table; imported exits are handled by the engine's movement rules.");
        Note("The Quill process table runs before each input; it is imported as EveryTurn triggers.");
        return result;
    }

    // ---------------------------------------------------------------- words

    private void ClassifyWords()
    {
        foreach (var e in db.Responses.Concat(db.Processes))
        {
            if (e.Verb != 255) verbNumbers.Add(e.Verb);
            if (e.Noun != 255) nounNumbers.Add(e.Noun);
        }
        if (db.ObjectWords is { } ow)
            foreach (var w in ow) if (w != 255) nounNumbers.Add(w);
    }

    private string? DirectionForNumber(int number)
    {
        foreach (var w in db.Synonyms(number))
            if (ObviousDirections.TryGetValue(w, out var d)) return d;
        return null;
    }

    private void BuildVocabulary()
    {
        var voc = adv.Vocabulary;
        foreach (var (word, number) in db.Words)
        {
            var w = word.ToLowerInvariant();
            voc.LegacyWordNumbers.TryAdd(w, number);
        }

        // Verbs: every word number used in the verb slot of a table.
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var number in verbNumbers.OrderBy(n => n))
        {
            var words = db.Synonyms(number).ToList();
            if (words.Count == 0)
            {
                Warn($"Word number {number} is used in the tables but is not in the vocabulary.");
                words.Add($"word{number}");
            }
            var id = words[0];
            for (int k = 2; !usedIds.Add(id); k++) id = $"{words[0]}{k}";
            verbIds[number] = id;
            voc.Verbs.Add(new VerbDefinition { Id = id, Words = words });
        }

        // Directions: every word group containing an obvious direction word, plus any other connection word.
        var connectionWords = db.Connections.SelectMany(c => c).Select(c => c.Word).ToHashSet();
        foreach (var number in db.Words.Select(w => w.Number).Distinct())
        {
            var dir = DirectionForNumber(number);
            if (dir is null && !connectionWords.Contains(number)) continue;
            var canonical = dir ?? db.FirstWord(number)!;
            foreach (var w in db.Synonyms(number)) voc.Directions.TryAdd(w, canonical);
        }

        // Nouns: words used as nouns, object words and every word that is neither a verb nor a movement word.
        foreach (var (word, number) in db.Words)
        {
            bool noun = nounNumbers.Contains(number) || (!verbNumbers.Contains(number) && !connectionWords.Contains(number) && DirectionForNumber(number) is null);
            var w = word.ToLowerInvariant();
            if (noun && !voc.Nouns.Contains(w)) voc.Nouns.Add(w);
        }
    }

    private string VerbId(int number) => number == 255 ? "*" : verbIds.TryGetValue(number, out var id) ? id : db.FirstWord(number) ?? $"word{number}";
    private string NounId(int number) => number == 255 ? "*" : db.FirstWord(number) ?? $"word{number}";
    private string WordLabel(int number) => number == 255 ? "_" : (db.FirstWord(number) ?? number.ToString()).ToUpperInvariant();

    // ---------------------------------------------------------------- rooms & items

    private void BuildRooms()
    {
        for (int i = 0; i < db.LocationCount; i++)
        {
            var text = db.Locations[i];
            var room = new Room { Id = $"r{i}", Name = RoomName(text, i), Description = text };
            foreach (var (word, target) in db.Connections[i])
            {
                var dir = DirectionForNumber(word) ?? db.FirstWord(word) ?? $"word{word}";
                if (room.Exits.Any(x => string.Equals(x.Direction, dir, StringComparison.OrdinalIgnoreCase))) continue;
                room.Exits.Add(new Exit { Direction = dir, TargetRoomId = $"r{target}" });
            }
            adv.Rooms.Add(room);
        }
    }

    private static readonly string[] RoomPrefixes =
    {
        "you are standing at ", "you are standing in ", "you are standing on ", "i am standing at ", "i'm standing at ",
        "i am in ", "i'm in ", "you are in ", "i am on ", "i'm on ", "you are on ", "i am at ", "i'm at ", "you are at ",
        "i am standing in ", "i'm standing in ", "you are standing in ", "you're in ", "i am ", "i'm ", "you are ",
    };

    internal static string RoomName(string text, int index)
    {
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        int stop = first.IndexOfAny(new[] { '.', '!', '?', ';' });
        if (stop > 0) first = first[..stop];
        first = first.Trim().TrimEnd(',', ':', '-').Trim();
        foreach (var p in RoomPrefixes)
            if (first.StartsWith(p, StringComparison.OrdinalIgnoreCase) && first.Length > p.Length + 2) { first = first[p.Length..]; break; }
        if (first.Length > 40)
        {
            int cut = first.LastIndexOf(' ', 40);
            first = (cut > 15 ? first[..cut] : first[..40]).TrimEnd(',', ' ') + "…";
        }
        if (first.Length == 0) return $"Location {index}";
        return char.ToUpperInvariant(first[0]) + first[1..];
    }

    private static string MapLocation(int loc) => loc switch
    {
        252 => Locations.Nowhere,
        253 => Locations.Worn,
        254 => Locations.Carried,
        255 => Locations.Here,
        _ => $"r{loc}",
    };

    private void BuildItems()
    {
        // Objects that are worn/removed explicitly by the tables are wearable (all of them if WEAR _ uses AUTOW).
        bool anyWearable = false;
        foreach (var e in db.Responses.Concat(db.Processes))
            foreach (var a in e.Actions)
            {
                if (a.Op is QuillDatabase.A_WEAR or QuillDatabase.A_REMOVE) wearable.Add(a.Args[0]);
                if (a.Op is QuillDatabase.A_AUTOW or QuillDatabase.A_AUTOR)
                {
                    if (e.Noun != 255) wearableWords.Add(e.Noun);
                    else anyWearable = true;
                }
            }

        for (int i = 0; i < db.ObjectCount; i++)
        {
            var text = db.Objects[i];
            var (article, name) = SplitArticle(text);
            var item = new Item
            {
                Id = $"o{i}",
                Article = article,
                Name = name,
                Description = text,
                Location = MapLocation(db.ObjectStart[i]),
                Portable = true,
            };
            if (db.ObjectStart[i] == 255) item.Location = Locations.Nowhere;

            int number = db.ObjectWords is { } ow && ow[i] != 255 ? ow[i] : -1;
            if (number < 0) number = GuessNounNumber(text);
            if (number >= 0) item.Nouns.AddRange(NounsFor(text, number));
            if (anyWearable || db.ObjectStart[i] == 253 || wearable.Contains(i) || (number >= 0 && wearableWords.Contains(number))) item.Wearable = true;
            if (i == 0)
            {
                item.LightSource = true;
                item.IsLit = true;
            }
            adv.Items.Add(item);
        }
        Note("Object 0 is Quill's light source: when flag 0 (f0) is non-zero it is dark unless object 0 is present.");
        if (db.ObjectWords is null)
            Note("This Quill version has no object-word table; item nouns were guessed from the object descriptions.");
    }

    internal static (string Article, string Name) SplitArticle(string text)
    {
        var single = Regex.Replace(text.Replace('\n', ' '), @"\s+", " ").Trim();
        if (single.EndsWith('.') && !single.EndsWith("..")) single = single[..^1].TrimEnd();
        foreach (var art in new[] { "a", "an", "some", "the" })
        {
            if (single.Length > art.Length + 1 && single.StartsWith(art + " ", StringComparison.OrdinalIgnoreCase))
                return (art, single[(art.Length + 1)..].Trim());
        }
        return ("", single);
    }

    private static IEnumerable<string> Tokens(string text) =>
        Regex.Matches(text, "[A-Za-z0-9']+").Select(m => m.Value.Trim('\''));

    private static string Key(string token) => (token.Length > 4 ? token[..4] : token).ToUpperInvariant();

    private IEnumerable<int> NumbersForToken(string token)
    {
        var key = Key(token);
        foreach (var (w, n) in db.Words)
            if (string.Equals(w, key, StringComparison.OrdinalIgnoreCase)) yield return n;
    }

    /// <summary>Picks the vocabulary noun that names an object: the last word of its text that is used as a noun.</summary>
    private int GuessNounNumber(string text)
    {
        var tokens = Tokens(text).Reverse().ToList();
        foreach (var t in tokens)
            foreach (var n in NumbersForToken(t))
                if (nounNumbers.Contains(n)) return n;
        foreach (var t in tokens)
            foreach (var n in NumbersForToken(t))
                if (!verbNumbers.Contains(n) && n != 255) return n;
        return -1;
    }

    private List<string> NounsFor(string text, int number)
    {
        var list = new List<string>();
        var synonyms = db.Synonyms(number).ToList();
        // Prefer the full spelling from the description ("torch" rather than the 4-letter "torc").
        foreach (var t in Tokens(text))
        {
            var lower = t.ToLowerInvariant();
            if (synonyms.Any(sy => string.Equals(sy, Key(t), StringComparison.OrdinalIgnoreCase)) && !list.Contains(lower))
                list.Add(lower);
        }
        foreach (var sy in synonyms) if (!list.Contains(sy)) list.Add(sy);
        return list;
    }

    // ---------------------------------------------------------------- triggers

    private void BuildTriggers()
    {
        for (int i = 0; i < db.Responses.Count; i++)
            adv.Triggers.Add(MakeTrigger(db.Responses[i], $"resp{i}", TriggerEvent.BeforeCommand));
        for (int i = 0; i < db.Processes.Count; i++)
            adv.Triggers.Add(MakeTrigger(db.Processes[i], $"proc{i}", TriggerEvent.EveryTurn));
    }

    private Trigger MakeTrigger(QuillEntry e, string id, TriggerEvent ev)
    {
        var t = new Trigger
        {
            Id = id,
            Name = $"{WordLabel(e.Verb)} {WordLabel(e.Noun)}",
            Event = ev,
            // Quill status-table words are only labels for the author; the table always runs in full.
            Verb = ev == TriggerEvent.EveryTurn ? null : VerbId(e.Verb),
            Noun1 = ev == TriggerEvent.EveryTurn ? null : NounId(e.Noun),
            StopsCommand = false,
        };
        foreach (var c in e.Conditions)
        {
            var cond = ConvertCondition(c, t);
            if (cond is not null) t.Conditions.Add(cond);
        }
        ConvertActions(e.Actions, t);
        return t;
    }

    private string ObjectId(int n, Trigger t)
    {
        if (n >= db.ObjectCount) Warn($"{t.Id} ({t.Name}): refers to object {n}, which does not exist.");
        return $"o{n}";
    }

    private string RoomId(int n, Trigger t)
    {
        if (n >= db.LocationCount) Warn($"{t.Id} ({t.Name}): refers to location {n}, which does not exist.");
        return $"r{n}";
    }

    /// <summary>Variable name used when reading Quill flag n (system flags map to engine pseudo-variables).</summary>
    private string ReadVar(int f, Trigger t)
    {
        switch (f)
        {
            case 1: return "@carried";
            case 30: return "@score";
            case 31:
                Warn("Tests of flag 31 (turns, low byte) were mapped to @turns; they differ after 255 turns.");
                return "@turns";
            case 35: return "@room";
            case 32:
            case 33:
            case 34:
                Warn($"Flag {f} ({FlagDescriptions[f]}) is not maintained by the engine; tests of it use variable f{f}.");
                break;
        }
        usedFlags.Add(f);
        return $"f{f}";
    }

    private string WriteVar(int f)
    {
        if (f is 1 or 31 or 32 or 33 or 34)
            Warn($"Flag {f} ({FlagDescriptions[f]}) is written by the game; the engine keeps its own value, so writes go to variable f{f}.");
        usedFlags.Add(f);
        return $"f{f}";
    }

    private Condition? ConvertCondition(QuillCondact c, Trigger t)
    {
        int a = c.Args[0];
        int b = c.Args.Length > 1 ? c.Args[1] : 0;
        return c.Op switch
        {
            0 => new Condition(ConditionType.PlayerIn, RoomId(a, t)),
            1 => new Condition(ConditionType.PlayerIn, RoomId(a, t), negate: true),
            2 => new Condition(ConditionType.VarGreater, "@room", a),
            3 => new Condition(ConditionType.VarLess, "@room", a),
            4 => new Condition(ConditionType.ItemPresent, ObjectId(a, t)),
            5 => new Condition(ConditionType.ItemPresent, ObjectId(a, t), negate: true),
            6 => new Condition(ConditionType.ItemWorn, ObjectId(a, t)),
            7 => new Condition(ConditionType.ItemWorn, ObjectId(a, t), negate: true),
            8 => new Condition(ConditionType.ItemIn, ObjectId(a, t), b: Locations.Carried),
            9 => new Condition(ConditionType.ItemIn, ObjectId(a, t), b: Locations.Carried, negate: true),
            10 => new Condition(ConditionType.Chance, n: a),
            11 => new Condition(ConditionType.VarEquals, ReadVar(a, t), 0),
            12 => new Condition(ConditionType.VarEquals, ReadVar(a, t), 0, negate: true),
            13 => new Condition(ConditionType.VarEquals, ReadVar(a, t), b),
            14 => new Condition(ConditionType.VarGreater, ReadVar(a, t), b),
            15 => new Condition(ConditionType.VarLess, ReadVar(a, t), b),
            _ => null,
        };
    }

    private void ConvertActions(List<QuillCondact> actions, Trigger t)
    {
        var list = t.Actions;
        int pending28 = -1; // value last stored in flag 28 by this entry (selects PAUSE's special function)
        foreach (var act in actions)
        {
            int a = act.Args.Length > 0 ? act.Args[0] : 0;
            int b = act.Args.Length > 1 ? act.Args[1] : 0;
            switch (act.Op)
            {
                case QuillDatabase.A_INVEN: list.Add(new GameAction(ActionType.Inventory)); list.Add(new GameAction(ActionType.Done)); break;
                case QuillDatabase.A_DESC: list.Add(new GameAction(ActionType.Look)); list.Add(new GameAction(ActionType.Done)); break;
                case QuillDatabase.A_QUIT: list.Add(new GameAction(ActionType.Quit)); break;
                case QuillDatabase.A_END: list.Add(new GameAction(ActionType.Lose, text: SysMessage(13, ""))); break;
                case QuillDatabase.A_DONE: list.Add(new GameAction(ActionType.Done)); break;
                case QuillDatabase.A_OK: list.Add(new GameAction(ActionType.Ok)); break;
                case QuillDatabase.A_ANYKEY: list.Add(new GameAction(ActionType.Pause, n: 0)); break;
                case QuillDatabase.A_SAVE: list.Add(new GameAction(ActionType.Save)); break;
                case QuillDatabase.A_LOAD: list.Add(new GameAction(ActionType.Restore)); break;
                case QuillDatabase.A_TURNS: list.Add(new GameAction(ActionType.ShowTurns)); break;
                case QuillDatabase.A_SCORE: list.Add(new GameAction(ActionType.ShowScore)); break;
                case QuillDatabase.A_CLS: list.Add(new GameAction(ActionType.ClearScreen)); break;
                case QuillDatabase.A_DROPALL: list.Add(new GameAction(ActionType.DropAll)); break;
                case QuillDatabase.A_AUTOG: list.Add(new GameAction(ActionType.TakeItem, "$noun1")); break;
                case QuillDatabase.A_AUTOD: list.Add(new GameAction(ActionType.DropItem, "$noun1")); break;
                case QuillDatabase.A_AUTOW: list.Add(new GameAction(ActionType.WearItem, "$noun1")); break;
                case QuillDatabase.A_AUTOR: list.Add(new GameAction(ActionType.UnwearItem, "$noun1")); break;
                case QuillDatabase.A_PAUSE:
                    if (db.Version > 0 && pending28 is > 0 and < 23)
                        PauseFunction(pending28, a, t);
                    else
                        list.Add(new GameAction(ActionType.Pause, n: (a == 0 ? 256 : a) * 20));
                    pending28 = -1;
                    break;
                case QuillDatabase.A_PAPER:
                case QuillDatabase.A_INK:
                case QuillDatabase.A_BORDER:
                    Warn("PAPER/INK/BORDER actions (screen colour changes) have no equivalent and were ignored.");
                    break;
                case QuillDatabase.A_GOTO: list.Add(new GameAction(ActionType.GoTo, RoomId(a, t))); break;
                case QuillDatabase.A_MESSAGE:
                    if (a < db.MessageCount) list.Add(GameAction.Say(db.Messages[a]));
                    else Warn($"{t.Id} ({t.Name}): MESSAGE {a} does not exist.");
                    break;
                case QuillDatabase.A_REMOVE: list.Add(new GameAction(ActionType.UnwearItem, ObjectId(a, t))); break;
                case QuillDatabase.A_GET: list.Add(new GameAction(ActionType.TakeItem, ObjectId(a, t))); break;
                case QuillDatabase.A_DROP: list.Add(new GameAction(ActionType.DropItem, ObjectId(a, t))); break;
                case QuillDatabase.A_WEAR: list.Add(new GameAction(ActionType.WearItem, ObjectId(a, t))); break;
                case QuillDatabase.A_DESTROY: list.Add(new GameAction(ActionType.DestroyItem, ObjectId(a, t))); break;
                case QuillDatabase.A_CREATE: list.Add(new GameAction(ActionType.CreateItem, ObjectId(a, t))); break;
                case QuillDatabase.A_SWAP: list.Add(new GameAction(ActionType.SwapItems, ObjectId(a, t), b: ObjectId(b, t))); break;
                case QuillDatabase.A_PLACE:
                    list.Add(new GameAction(ActionType.MoveItem, ObjectId(a, t), b: b < 252 ? RoomId(b, t) : MapLocation(b)));
                    break;
                case QuillDatabase.A_SET: SetFlag(list, a, 255); if (a == 28) pending28 = 255; break;
                case QuillDatabase.A_CLEAR: SetFlag(list, a, 0); if (a == 28) pending28 = -1; break;
                case QuillDatabase.A_LET: SetFlag(list, a, b); if (a == 28) pending28 = b; break;
                case QuillDatabase.A_PLUS: AddFlag(list, a, b); break;
                case QuillDatabase.A_MINUS: AddFlag(list, a, -b); break;
                case QuillDatabase.A_BEEP: list.Add(new GameAction(ActionType.Beep, n: a, b: b.ToString(CultureInfo.InvariantCulture))); break;
                default:
                    Warn($"Unknown action {act.Op} ignored.");
                    break;
            }
        }
    }

    private void SetFlag(List<GameAction> list, int f, int value)
    {
        switch (f)
        {
            case 35:
                list.Add(new GameAction(ActionType.GoTo, $"r{value}"));
                return;
            case 30:
                Warn("The score (flag 30) is set directly; mapped to SetVar on @score.");
                list.Add(new GameAction(ActionType.SetVar, "@score", value));
                return;
        }
        list.Add(new GameAction(ActionType.SetVar, WriteVar(f), value));
    }

    private void AddFlag(List<GameAction> list, int f, int delta)
    {
        switch (f)
        {
            case 30:
                list.Add(new GameAction(ActionType.AwardScore, n: delta));
                return;
            case 35:
                Warn("Arithmetic on the location flag (35) was mapped to AddVar on @room.");
                list.Add(new GameAction(ActionType.AddVar, "@room", delta));
                return;
        }
        if (delta != 0) Warn("PLUS/MINUS clamp flags to 0..255 in the Quill; the imported AddVar actions do not.");
        list.Add(new GameAction(ActionType.AddVar, WriteVar(f), delta));
    }

    /// <summary>Later Quill runtimes perform a special function when PAUSE runs with flag 28 set to 1..22.</summary>
    private void PauseFunction(int function, int arg, Trigger t)
    {
        var list = t.Actions;
        switch (function)
        {
            case 9: list.Add(new GameAction(ActionType.ClearScreen)); break;
            case 12: list.Add(new GameAction(ActionType.Restart)); break;
            case 13: list.Add(new GameAction(ActionType.Quit)); break;
            case 11:
                Warn($"{t.Id} ({t.Name}): PAUSE function 11 sets the carrying limit to {arg}; not supported at run time.");
                break;
            case 14:
            case 15:
                Warn($"{t.Id} ({t.Name}): PAUSE function {function} changes the carrying limit by {arg}; not supported at run time.");
                break;
            case 10:
                Warn($"{t.Id} ({t.Name}): PAUSE function 10 changes the \"you can also see\" message to system message {arg}; ignored.");
                break;
            case 19:
                Warn("PAUSE function 19 (pictures on/off) was ignored.");
                break;
            case 21:
                Warn("PAUSE function 21 (RAM save/load) was ignored; use SAVE/LOAD.");
                break;
            default:
                Warn($"PAUSE special function {function} (sound, fonts, keyboard click or screen effects) was ignored.");
                break;
        }
    }

    /// <summary>Emulates the Quill's automatically decremented flags 2-10 when a game uses them.</summary>
    private void BuildFlagTimers()
    {
        var flags = usedFlags.Where(f => f is >= 2 and <= 10).ToList();
        foreach (var f in flags)
        {
            var t = new Trigger
            {
                Id = $"flagtimer{f}",
                Name = $"Decrement f{f}",
                Event = f <= 4 ? TriggerEvent.EnterRoom : TriggerEvent.EveryTurn,
                StopsCommand = false,
                Notes = FlagDescriptions[f],
            };
            t.Conditions.Add(new Condition(ConditionType.VarGreater, $"f{f}", 0));
            if (f is 3 or 4 or 9 or 10) t.Conditions.Add(new Condition(ConditionType.VarEquals, "f0", 0, negate: true));
            if (f is 4 or 10) t.Conditions.Add(new Condition(ConditionType.ItemPresent, "o0", negate: true));
            t.Actions.Add(new GameAction(ActionType.AddVar, $"f{f}", -1));
            adv.Triggers.Add(t);
        }
        if (flags.Any(f => f <= 4))
            Note("Flags 2-4 are decremented when a location is described; this is approximated by EnterRoom triggers.");
    }

    private void BuildVariables()
    {
        usedFlags.Add(0);
        foreach (var f in usedFlags)
        {
            adv.Variables.Add(new Variable
            {
                Name = $"f{f}",
                InitialValue = 0,
                Description = FlagDescriptions.TryGetValue(f, out var d) ? d : "Quill flag.",
            });
        }
    }

    // ---------------------------------------------------------------- messages

    private string SysMessage(int n, string fallback) => n < db.SystemMessages.Count ? db.SystemMessages[n] : fallback;
    private string RawSys(int n) => n < db.RawSystemMessages.Count ? Regex.Replace(db.RawSystemMessages[n].Replace('\n', ' '), " {2,}", " ") : "";

    private void BuildMessages()
    {
        for (int i = 0; i < db.SystemMessages.Count; i++) adv.Messages[$"sys{i}"] = db.SystemMessages[i];
        if (db.SystemMessages.Count < 16) return;

        bool late = db.Version > 0;
        void Set(string id, int n) { if (n < db.SystemMessages.Count && db.SystemMessages[n].Length > 0) adv.Messages[id] = db.SystemMessages[n]; }

        Set(Msg.Dark, 0);
        if (db.SystemMessages[1].Length > 0) adv.Messages[Msg.YouCanSee] = db.SystemMessages[1] + " {list}";
        Set(Msg.DontUnderstand, 6);
        Set(Msg.UnknownWord, 6);
        Set(Msg.CantGo, 7);
        Set(Msg.CantDoThat, 8);
        if (db.SystemMessages[9].Length > 0)
        {
            adv.Messages[Msg.Carrying] = db.SystemMessages[9] + " {list}";
            adv.Messages[Msg.CarryingNothing] = db.SystemMessages[9] + "\n" + db.SystemMessages[11];
        }
        Set(Msg.Ok, 15);
        adv.Messages[Msg.Turns] = (RawSys(17) + "{turns}" + RawSys(18) + (late ? RawSys(19) + RawSys(20) : "s.")).Trim();
        if (late && db.SystemMessages.Count > 29)
        {
            adv.Messages[Msg.Score] = (RawSys(21) + "{score}" + RawSys(22)).Trim();
            Set(Msg.NotWorn, 23);
            Set(Msg.AlreadyCarrying, 25);
            Set(Msg.NotHere, 26);
            Set(Msg.TooMany, 27);
            Set(Msg.NotCarrying, 28);
        }
        else if (!late && db.SystemMessages.Count > 26)
        {
            adv.Messages[Msg.Score] = (RawSys(19) + "{score}.").Trim();
            Set(Msg.NotWorn, 20);
            Set(Msg.AlreadyCarrying, 22);
            Set(Msg.NotHere, 23);
            Set(Msg.TooMany, 24);
            Set(Msg.NotCarrying, 25);
        }
        // The Quill's GET/DROP/WEAR/REMOVE print nothing when they succeed; the game's own tables print "OK" etc.
        adv.Messages[Msg.Taken] = "";
        adv.Messages[Msg.Dropped] = "";
        adv.Messages[Msg.Worn] = "";
        adv.Messages[Msg.Removed] = "";
        Note("Quill GET/DROP/WEAR/REMOVE succeed silently, so the Taken/Dropped/Worn/Removed messages are empty; all system messages are also stored as sys0, sys1, ...");
    }

    // ---------------------------------------------------------------- pictures

    private void BuildPictures(IllustratorGraphics gfx)
    {
        for (int n = 0; n < gfx.Count; n++)
        {
            var pic = gfx.ToPicture(n);
            adv.Pictures.Add(pic);
            if (gfx.IsLocationPicture(n) && n < adv.Rooms.Count) adv.Rooms[n].PictureId = pic.Id;
        }
        foreach (var w in gfx.Warnings) Warn(w);
        adv.Settings.ShowPictures = true;
        Note("Illustrator pictures: flag 29 controls when the Quill draws them (first visit / every time / never); the engine's picture settings apply instead.");
        Note("Illustrator GOSUB calls to location pictures or to subroutines with absolute coordinates were expanded inline.");
    }

    // ---------------------------------------------------------------- helpers

    private void Warn(string message)
    {
        if (!warned.Add(message)) return;
        result.Warnings.Add(message);
        adv.Notes.Add(message);
    }

    private void Note(string message)
    {
        if (!adv.Notes.Contains(message)) adv.Notes.Add(message);
    }

    private static string Hex(uint argb) => $"#{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}";

    internal static string TitleFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? "");
        name = Regex.Replace(name.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
        if (name.Length == 0) return "Quill Adventure";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }
}
