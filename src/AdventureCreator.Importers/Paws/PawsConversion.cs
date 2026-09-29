using System.Globalization;
using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Paws;

/// <summary>Maps a parsed <see cref="PawsDatabase"/> onto the <see cref="Adventure"/> model.</summary>
internal sealed class PawsConversion
{
    private const int WordAny = 1;     // "*"
    private const int WordNone = 255;  // "_"

    private readonly PawsDatabase db;
    private readonly string fileName;
    private readonly Adventure adv = new();
    private readonly List<string> warnings = new();
    private readonly HashSet<string> notes = new();
    private readonly Dictionary<string, int> skipped = new();
    private readonly HashSet<int> usedFlags = new();
    private readonly List<Variable> chainVariables = new();

    private List<PawsWord> words = new();
    private readonly Dictionary<int, string> directionOfNumber = new();
    private readonly Dictionary<int, string> verbIdOfNumber = new();
    private readonly HashSet<int> containers = new();
    private bool usesListObj;
    private bool usesScore;
    private (int Objects, int Weight)? ability;

    public PawsConversion(PawsDatabase db, string fileName)
    {
        this.db = db;
        this.fileName = fileName;
    }

    public ImportResult Run()
    {
        adv.Title = TitleFromFile(fileName);
        adv.Description = $"Imported from the PAWS database in {Path.GetFileName(fileName)}.";
        adv.Settings.LegacyTableSemantics = true;
        adv.Settings.DarknessVariable = "f0";
        adv.Settings.SignificantLetters = 5;
        adv.Settings.AutoListExits = false;
        adv.Settings.SpellingCorrection = false;

        ReadVocabulary();
        ReadRooms();
        ReadItems();
        ReadExits();
        ReadSystemMessages();
        ReadProcessTables();
        ReadPictures();
        BuildVariables();
        ApplySettings();
        FinishNotes();

        var result = new ImportResult(adv)
        {
            DetectedFormat = $"PAWS (ZX Spectrum {(db.Is128K ? "128K" : "48K")}, " +
                             $"{(db.Compressed ? "compressed" : "uncompressed")} database" +
                             $"{(db.VersionByte is > 32 and < 127 ? $", version {(char)db.VersionByte}" : "")})",
        };
        result.Warnings.AddRange(db.Warnings);
        result.Warnings.AddRange(warnings);
        return result;
    }

    // ================================================================ vocabulary

    private static readonly Dictionary<string, string> KnownDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "north", ["north"] = "north", ["s"] = "south", ["south"] = "south",
        ["e"] = "east", ["east"] = "east", ["w"] = "west", ["west"] = "west",
        ["ne"] = "northeast", ["nw"] = "northwest", ["se"] = "southeast", ["sw"] = "southwest",
        ["neast"] = "northeast", ["nwest"] = "northwest", ["seast"] = "southeast", ["swest"] = "southwest",
        ["u"] = "up", ["up"] = "up", ["ascen"] = "up", ["d"] = "down", ["down"] = "down", ["desce"] = "down",
        ["in"] = "in", ["insid"] = "in", ["enter"] = "in", ["out"] = "out", ["outsi"] = "out", ["exit"] = "out", ["leave"] = "out",
    };

    private void ReadVocabulary()
    {
        words = db.ReadVocabulary();
        var voc = adv.Vocabulary;

        // Canonical direction for each word value: any synonym (verb or noun) that is an obvious direction.
        foreach (var w in words)
            if (w.Type is PawsWordType.Verb or PawsWordType.Noun && KnownDirections.TryGetValue(w.Text, out var dir))
                directionOfNumber.TryAdd(w.Number, dir);

        foreach (var w in words)
        {
            if (!voc.LegacyWordNumbers.TryGetValue(w.Text, out _) || w.Type == PawsWordType.Verb)
                voc.LegacyWordNumbers[w.Text] = w.Number;
            var list = w.Type switch
            {
                PawsWordType.Noun => voc.Nouns,
                PawsWordType.Adverb => voc.Adverbs,
                PawsWordType.Adjective => voc.Adjectives,
                PawsWordType.Preposition => voc.Prepositions,
                _ => null,
            };
            if (list != null && !list.Contains(w.Text)) list.Add(w.Text);
        }

        // Verbs: one definition per word value (synonyms). Movement verbs become directions instead.
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in words.Where(w => w.Type == PawsWordType.Verb).GroupBy(w => w.Number))
        {
            if (directionOfNumber.TryGetValue(group.Key, out var dir))
            {
                verbIdOfNumber[group.Key] = dir;
                continue;
            }
            var id = group.First().Text;
            if (!usedIds.Add(id)) { id = $"{id}{group.Key}"; usedIds.Add(id); }
            verbIdOfNumber[group.Key] = id;
            voc.Verbs.Add(new VerbDefinition { Id = id, Words = group.Select(w => w.Text).Distinct().ToList() });
        }

        foreach (var w in words)
            if (w.Type is PawsWordType.Verb or PawsWordType.Noun && directionOfNumber.TryGetValue(w.Number, out var dir))
                voc.Directions[w.Text] = dir;
    }

    private string? WordText(int number, PawsWordType type) =>
        words.FirstOrDefault(w => w.Number == number && w.Type == type)?.Text;

    private string? AnyWord(int number, params PawsWordType[] preferred)
    {
        foreach (var t in preferred)
            if (WordText(number, t) is { } s) return s;
        return words.FirstOrDefault(w => w.Number == number)?.Text;
    }

    private List<string> Synonyms(int number, PawsWordType type) =>
        words.Where(w => w.Number == number && w.Type == type).Select(w => w.Text).Distinct().ToList();

    // ================================================================ rooms, items, exits

    private void ReadRooms()
    {
        for (int n = 0; n < db.NumLocations; n++)
        {
            var text = db.LocationText(n);
            adv.Rooms.Add(new Room { Id = $"r{n}", Name = RoomName(text, n), Description = text });
        }
        adv.StartRoomId = "r0";
    }

    internal static string RoomName(string text, int n)
    {
        var s = text.Trim();
        int end = s.Length;
        foreach (var c in new[] { '\n', '.', '!', '?' })
        {
            int i = s.IndexOf(c);
            if (i > 0 && i < end) end = i;
        }
        s = s[..end].Trim().TrimEnd(',', ';', ':').Trim();
        if (s.Length > 48)
        {
            int cut = s.LastIndexOf(' ', 48);
            s = (cut > 16 ? s[..cut] : s[..48]).TrimEnd(',', ';', ':', ' ') + "…";
        }
        if (s.Length == 0) return $"Location {n}";
        if (s.Any(char.IsLetter) && s.Where(char.IsLetter).All(char.IsUpper))
            s = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        return s;
    }

    private void ReadItems()
    {
        for (int n = 0; n < db.NumObjects; n++)
            if ((db.ObjectAttributes(n) & 0x40) != 0) containers.Add(n);

        for (int n = 0; n < db.NumObjects; n++)
        {
            var text = db.ObjectText(n);
            var (article, name, rest) = SplitObjectText(text);
            int attr = db.ObjectAttributes(n);
            int noun = db.ObjectNoun(n), adj = db.ObjectAdjective(n);
            var item = new Item
            {
                Id = $"o{n}",
                Article = article,
                Name = name.Length > 0 ? name : $"object {n}",
                Description = rest,
                Weight = attr & 0x3F,
                Container = (attr & 0x40) != 0,
                Wearable = (attr & 0x80) != 0,
                Portable = true,
                Location = Location(db.InitiallyAt(n)),
            };
            if (noun != WordNone) item.Nouns.AddRange(Synonyms(noun, PawsWordType.Noun));
            if (adj != WordNone) item.Adjectives.AddRange(Synonyms(adj, PawsWordType.Adjective));
            if (item.Nouns.Count == 0) GuessWords(item);
            if (item.Container) item.Capacity = 255;
            if (n == 0)
            {
                // PAWS: object 0 is the source of light – it is never dark while object 0 is present.
                item.LightSource = true;
                item.IsLit = true;
            }
            adv.Items.Add(item);
        }
        if (containers.Count > 0)
            notes.Add("PAWS containers: objects inside container object n are at location n; those locations were mapped to the container item \"o{n}\".");
    }

    /// <summary>
    /// Objects without an object-word entry ("_ _") can only be manipulated by the game's own table entries;
    /// give them the vocabulary nouns / adjectives that appear in their name so the engine can refer to them.
    /// </summary>
    private void GuessWords(Item item)
    {
        var nameWords = item.Name.ToLowerInvariant()
            .Split(new[] { ' ', ',', '(', ')', '\'', '"', '-', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 5 ? w[..5] : w).ToList();
        for (int i = nameWords.Count - 1; i >= 0 && item.Nouns.Count == 0; i--)
            if (words.FirstOrDefault(w => w.Type == PawsWordType.Noun && w.Text == nameWords[i] && w.Number >= 20) is { } nw)
            {
                item.Nouns.AddRange(Synonyms(nw.Number, PawsWordType.Noun));
                foreach (var a in nameWords.Take(i))
                    if (words.FirstOrDefault(w => w.Type == PawsWordType.Adjective && w.Text == a) is { } aw)
                        item.Adjectives.AddRange(Synonyms(aw.Number, PawsWordType.Adjective).Where(s => !item.Adjectives.Contains(s)));
            }
        if (item.Nouns.Count > 0)
            notes.Add("Objects with no object words (\"_ _\") were given the vocabulary nouns/adjectives found in their names.");
    }

    internal static (string Article, string Name, string Extra) SplitObjectText(string text)
    {
        var s = text.Replace('\n', ' ').Trim();
        string article = "";
        int sp = s.IndexOf(' ');
        if (sp > 0)
        {
            var first = s[..sp];
            if (first.Equals("a", StringComparison.OrdinalIgnoreCase) || first.Equals("an", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("some", StringComparison.OrdinalIgnoreCase) || first.Equals("the", StringComparison.OrdinalIgnoreCase))
            {
                article = first.ToLowerInvariant();
                s = s[(sp + 1)..].TrimStart();
            }
        }
        int dot = s.IndexOf('.');
        string name = dot >= 0 ? s[..dot].Trim() : s;
        string rest = dot >= 0 ? text.Trim() : "";
        if (dot >= 0 && s[(dot + 1)..].Trim().Length == 0) rest = "";
        return (article, name, rest);
    }

    /// <summary>Maps a PAWS object location ("locno+") to a location id.</summary>
    private string Location(int loc) => loc switch
    {
        252 => Locations.Nowhere,
        253 => Locations.Worn,
        254 => Locations.Carried,
        255 => Locations.Here,
        _ when containers.Contains(loc) => $"o{loc}",
        _ => $"r{loc}",
    };

    private string Direction(int number)
    {
        if (directionOfNumber.TryGetValue(number, out var dir)) return dir;
        return AnyWord(number, PawsWordType.Verb, PawsWordType.Noun) ?? $"w{number}";
    }

    private void ReadExits()
    {
        for (int n = 0; n < db.NumLocations; n++)
        {
            foreach (var (word, dest) in db.Connections(n))
            {
                var dir = Direction(word);
                adv.Rooms[n].Exits.Add(new Exit { Direction = dir, TargetRoomId = $"r{dest}" });
                if (dest >= db.NumLocations) warnings.Add($"Location {n}: connection to non-existent location {dest}.");
                foreach (var w in words.Where(w => w.Number == word && w.Type is PawsWordType.Verb or PawsWordType.Noun))
                    adv.Vocabulary.Directions.TryAdd(w.Text, dir);
            }
        }
    }

    // ================================================================ messages

    /// <summary>Makes PAWS text safe for the engine's placeholder syntax; "_" becomes the current object.</summary>
    private static string MessageText(string s) =>
        s.Replace("{", "(").Replace("}", ")").Replace("_", "{noun1}");

    private static string Squash(string s)
    {
        s = s.Replace('\n', ' ');
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    private void ReadSystemMessages()
    {
        var m = adv.Messages;
        string Sys(int n) => MessageText(db.SysMessageText(n));
        string Raw(int n) => MessageText(db.RawSysMessage(n));

        for (int n = 0; n < db.NumSysMessages; n++) m[$"sys{n}"] = Sys(n);

        void Map(string id, int n) { if (n < db.NumSysMessages && Sys(n).Length > 0) m[id] = Sys(n); }
        Map(Msg.Dark, 0);
        Map(Msg.CantSeeInDark, 0);
        Map(Msg.DontUnderstand, 6);
        Map(Msg.CantGo, 7);
        Map(Msg.CantDoThat, 8);
        Map(Msg.Ok, 15);
        Map(Msg.NotWorn, 23);
        Map(Msg.AlreadyCarrying, 25);
        Map(Msg.NotHere, 26);
        Map(Msg.TooMany, 27);
        Map(Msg.NotCarrying, 28);
        Map(Msg.Waited, 35);
        Map(Msg.Taken, 36);
        Map(Msg.Worn, 37);
        Map(Msg.Removed, 38);
        Map(Msg.Dropped, 39);
        Map(Msg.NotWearable, 40);
        Map(Msg.TooHeavy, 43);
        if (db.NumSysMessages > 53 && Sys(53).Length > 0) m[Msg.Nothing] = Sys(53).TrimEnd('.', ' ');
        if (db.NumSysMessages > 1 && Sys(1).Length > 0)
            m[Msg.YouCanSee] = Squash(Raw(1) + "{list}" + (db.NumSysMessages > 48 ? Raw(48) : "."));
        if (db.NumSysMessages > 11)
        {
            m[Msg.Carrying] = Squash(Raw(9) + " {list}");
            m[Msg.CarryingNothing] = Squash(Raw(9) + " " + Raw(11));
        }
        if (db.NumSysMessages > 20) m[Msg.Turns] = Squash(Raw(17) + "{turns}" + Raw(18) + Raw(19) + Raw(20));
        if (db.NumSysMessages > 22) m[Msg.Score] = Squash(Raw(21) + "{score}" + Raw(22));
        if (db.NumSysMessages > 33 && Squash(Raw(33)) is { Length: > 0 } prompt && prompt.Length < 12)
            adv.Settings.Prompt = prompt + " ";
    }

    // ================================================================ process tables

    private sealed class Segment
    {
        public List<Condition> Conditions { get; } = new();
        public List<GameAction> Actions { get; } = new();
    }

    private void ReadProcessTables()
    {
        int count = db.NumProcesses;
        var tables = new List<PawsEntry>[count];
        for (int t = 0; t < count; t++) tables[t] = db.ReadProcessTable(t);

        // Which tables are called via PROCESS, and from where.
        var callers = new Dictionary<int, HashSet<int>>();
        for (int t = 0; t < count; t++)
            foreach (var e in tables[t])
                foreach (var c in e.Condacts)
                {
                    if (c.Opcode == PawsCondactInfo.Opcode("PROCESS"))
                    {
                        if (!callers.TryGetValue(c.Args[0], out var set)) callers[c.Args[0]] = set = new();
                        set.Add(t);
                    }
                    if (c.Opcode == PawsCondactInfo.Opcode("LISTOBJ")) usesListObj = true;
                    if (c.Opcode == PawsCondactInfo.Opcode("ABILITY") && ability == null) ability = (c.Args[0], c.Args[1]);
                }

        // A sub-process called only from Response (directly or indirectly) matches the verb and noun too.
        var responseContext = new bool[Math.Max(count, 256)];
        responseContext[0] = true;
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int t = 3; t < count; t++)
            {
                bool rc = callers.TryGetValue(t, out var cs) && cs.All(c => c < count && responseContext[c]);
                if (rc != responseContext[t]) { responseContext[t] = rc; changed = true; }
            }
        }

        for (int i = 0; i < tables[0].Count; i++)
            AddEntry(0, i, tables[0][i], TriggerEvent.BeforeCommand, null, true, "");
        if (count > 1)
            for (int i = 0; i < tables[1].Count; i++)
                AddEntry(1, i, tables[1][i], TriggerEvent.EveryTurn, $"PRO1 #{i}", false, "");
        if (count > 2)
            for (int i = 0; i < tables[2].Count; i++)
                AddEntry(2, i, tables[2][i], TriggerEvent.EveryTurn, $"PRO2 #{i}", false, "");
        for (int t = 0; t < count; t++)
        {
            if (t < 3 && !callers.ContainsKey(t)) continue;
            for (int i = 0; i < tables[t].Count; i++)
                AddEntry(t, i, tables[t][i], TriggerEvent.Subroutine, $"proc{t}", responseContext[t], t < 3 ? "_sub" : "");
        }
        foreach (var t in callers.Keys.Where(t => t >= count))
            warnings.Add($"PROCESS {t} is called but the database has only {count} process tables.");

        notes.Add("PAWS runs Process 1 after describing a location and Process 2 once per turn before reading the next command; " +
                  "both are imported as EveryTurn triggers (PRO1 entries first, then PRO2), so Process 1 entries also run on turns without a new description.");
        notes.Add("PAWS entries may mix conditions and actions; entries with a condition after an action were split into chained triggers " +
                  "(ids t{table}_{entry}_{k}) linked by a chain_* variable stamped with @turns.");
    }

    private void AddEntry(int table, int index, PawsEntry entry, TriggerEvent ev, string? name, bool matchWords, string idSuffix)
    {
        var segments = new List<Segment> { new() };
        foreach (var c in entry.Condacts)
        {
            var info = PawsCondactInfo.All[c.Opcode];
            bool isCondition = info.IsCondition || info.Name == "MOVE";
            if (isCondition && segments[^1].Actions.Count > 0) segments.Add(new Segment());
            Translate(c, segments[^1]);
        }

        string baseId = $"t{table}_{index}{idSuffix}";
        string verb = VerbPattern(entry.Verb), noun = NounPattern(entry.Noun);
        string chainVar = $"chain_t{table}_{index}{idSuffix}";
        if (segments.Count > 1)
            chainVariables.Add(new Variable { Name = chainVar, InitialValue = -1, Description = $"Links the split parts of PAWS process {table} entry {index}." });

        for (int k = 0; k < segments.Count; k++)
        {
            var seg = segments[k];
            var trigger = new Trigger
            {
                Id = k == 0 ? baseId : $"{baseId}_{k}",
                Name = name ?? $"{AnyWord(entry.Verb, PawsWordType.Verb, PawsWordType.Noun) ?? verb} {AnyWord(entry.Noun, PawsWordType.Noun) ?? noun}",
                Event = ev,
                StopsCommand = false,
                Notes = $"PAWS process {table}, entry {index}" + (segments.Count > 1 ? $", part {k + 1} of {segments.Count}" : ""),
            };
            if (ev == TriggerEvent.BeforeCommand || matchWords)
            {
                trigger.Verb = verb;
                trigger.Noun1 = noun;
            }
            if (k > 0) trigger.Conditions.Add(new Condition(ConditionType.VarEqualsVar, chainVar, b: "@turns"));
            trigger.Conditions.AddRange(seg.Conditions);
            if (k > 0) trigger.Actions.Add(new GameAction(ActionType.SetVar, chainVar, -1));
            trigger.Actions.AddRange(seg.Actions);
            if (k < segments.Count - 1 && !seg.Actions.Any(a => a.Type is ActionType.Done or ActionType.Ok))
                trigger.Actions.Add(new GameAction(ActionType.CopyVar, chainVar, b: "@turns"));
            // An entry that performs no action does nothing in PAWS (and must not count as "handled").
            if (trigger.Actions.All(a => a.A == chainVar && a.Type is ActionType.CopyVar or ActionType.SetVar)) continue;
            adv.Triggers.Add(trigger);
        }
    }

    private string VerbPattern(int number)
    {
        if (number is WordAny or WordNone) return "*";
        if (verbIdOfNumber.TryGetValue(number, out var id)) return id;
        if (directionOfNumber.TryGetValue(number, out var dir)) return dir;
        // Conversion nouns (value < 20) act as verbs; the engine matches the noun typed on its own.
        return AnyWord(number, PawsWordType.Noun) ?? Warn($"Unknown verb word value {number}.", $"w{number}");
    }

    private string NounPattern(int number)
    {
        if (number is WordAny or WordNone) return "*";
        return AnyWord(number, PawsWordType.Noun) ?? Warn($"Unknown noun word value {number}.", "*");
    }

    private string Warn(string message, string value)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
        return value;
    }

    // ================================================================ condacts

    /// <summary>Flag read: some PAWS system flags map onto engine pseudo-variables.</summary>
    private string FlagR(int f)
    {
        switch (f)
        {
            case 1: return "@carried";
            case 30: usesScore = true; return "@score";
            case 31: return "@turns";
            case 38: return "@room";
        }
        usedFlags.Add(f);
        return $"f{f}";
    }

    /// <summary>Flag write target, or null when the flag is maintained by the engine itself.</summary>
    private string? FlagW(int f)
    {
        if (f == 1)
        {
            notes.Add("Writes to PAWS flag 1 (objects carried) were dropped; the engine counts carried objects itself (@carried).");
            return null;
        }
        return FlagR(f);
    }

    private string Room(int l) => $"r{l}";
    private static string Obj(int o) => $"o{o}";

    private string WordArg(int number, PawsWordType type) =>
        AnyWord(number, type) ?? Warn($"Unknown {type.ToString().ToLowerInvariant()} word value {number}.", $"w{number}");

    private Condition WordCondition(ConditionType type, int number, PawsWordType wtype)
    {
        if (number != WordNone) return new Condition(type, WordArg(number, wtype));
        // "_" means "no word in that slot".
        return type switch
        {
            ConditionType.Noun2Is => new Condition(type, "-"),
            ConditionType.PrepositionIs => new Condition(type, "*", negate: true),
            _ => new Condition(type, "", negate: true),
        };
    }

    /// <summary>Text of user message n with layout spaces collapsed but meaningful edge spaces kept.</summary>
    private string MessageById(int n)
    {
        if (n >= db.NumMessages) Warn($"Message {n} does not exist.", "");
        return MessageText(LightClean(db.RawMessage(n)));
    }

    internal static string LightClean(string s)
    {
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Replace(" \n", "\n").Replace("\n ", "\n");
    }

    private void Skip(string condact, string why)
    {
        skipped[condact] = skipped.TryGetValue(condact, out var c) ? c + 1 : 1;
        notes.Add($"PAWS {condact}: {why}");
    }

    private void Translate(PawsCondact c, Segment seg)
    {
        var cond = seg.Conditions;
        var act = seg.Actions;
        int a0 = c.Args.Length > 0 ? c.Args[0] : 0, a1 = c.Args.Length > 1 ? c.Args[1] : 0;
        var name = PawsCondactInfo.All[c.Opcode].Name;

        void Set(string? flag, int value) { if (flag != null) act.Add(new GameAction(ActionType.SetVar, flag, value)); }

        switch (name)
        {
            // ---------------------------------------------------------- conditions
            case "AT": cond.Add(new Condition(ConditionType.PlayerIn, Room(a0))); break;
            case "NOTAT": cond.Add(new Condition(ConditionType.PlayerIn, Room(a0), negate: true)); break;
            case "ATGT": cond.Add(new Condition(ConditionType.VarGreater, "@room", a0)); break;
            case "ATLT": cond.Add(new Condition(ConditionType.VarLess, "@room", a0)); break;
            case "PRESENT": cond.Add(new Condition(ConditionType.ItemPresent, Obj(a0))); break;
            case "ABSENT": cond.Add(new Condition(ConditionType.ItemPresent, Obj(a0), negate: true)); break;
            case "WORN": cond.Add(new Condition(ConditionType.ItemWorn, Obj(a0))); break;
            case "NOTWORN": cond.Add(new Condition(ConditionType.ItemWorn, Obj(a0), negate: true)); break;
            case "CARRIED": cond.Add(new Condition(ConditionType.ItemIn, Obj(a0), b: Locations.Carried)); break;
            case "NOTCARR": cond.Add(new Condition(ConditionType.ItemIn, Obj(a0), b: Locations.Carried, negate: true)); break;
            case "ISAT": cond.Add(new Condition(ConditionType.ItemIn, Obj(a0), b: Location(a1))); break;
            case "ISNOTAT": cond.Add(new Condition(ConditionType.ItemIn, Obj(a0), b: Location(a1), negate: true)); break;
            case "CHANCE": cond.Add(new Condition(ConditionType.Chance, n: a0)); break;
            case "ZERO": cond.Add(new Condition(ConditionType.VarEquals, FlagR(a0), 0)); break;
            case "NOTZERO": cond.Add(new Condition(ConditionType.VarEquals, FlagR(a0), 0, negate: true)); break;
            case "EQ": cond.Add(new Condition(ConditionType.VarEquals, FlagR(a0), a1)); break;
            case "NOTEQ": cond.Add(new Condition(ConditionType.VarEquals, FlagR(a0), a1, negate: true)); break;
            case "GT": cond.Add(new Condition(ConditionType.VarGreater, FlagR(a0), a1)); break;
            case "LT": cond.Add(new Condition(ConditionType.VarLess, FlagR(a0), a1)); break;
            case "SAME": cond.Add(new Condition(ConditionType.VarEqualsVar, FlagR(a0), b: FlagR(a1))); break;
            case "NOTSAME": cond.Add(new Condition(ConditionType.VarEqualsVar, FlagR(a0), b: FlagR(a1), negate: true)); break;
            case "ADJECT1": cond.Add(WordCondition(ConditionType.AdjectiveUsed, a0, PawsWordType.Adjective)); break;
            case "ADJECT2":
                cond.Add(WordCondition(ConditionType.AdjectiveUsed, a0, PawsWordType.Adjective));
                notes.Add("PAWS ADJECT2: mapped to AdjectiveUsed, which does not distinguish the first and second noun's adjective.");
                break;
            case "ADVERB": cond.Add(WordCondition(ConditionType.AdverbUsed, a0, PawsWordType.Adverb)); break;
            case "PREP": cond.Add(WordCondition(ConditionType.PrepositionIs, a0, PawsWordType.Preposition)); break;
            case "NOUN2": cond.Add(WordCondition(ConditionType.Noun2Is, a0, PawsWordType.Noun)); break;
            case "TIMEOUT":
                cond.Add(new Condition(ConditionType.Always, negate: true));
                Skip(name, "input time-outs do not exist in the engine, so entries testing TIMEOUT never fire.");
                break;
            case "MOVE":
                cond.Add(new Condition(ConditionType.Always, negate: true));
                Skip(name, "moving a flag-based character through the connections table has no equivalent; entries using MOVE never fire.");
                break;

            // ---------------------------------------------------------- objects
            case "GET": act.Add(new GameAction(ActionType.TakeItem, Obj(a0))); break;
            case "DROP": act.Add(new GameAction(ActionType.DropItem, Obj(a0))); break;
            case "WEAR": act.Add(new GameAction(ActionType.WearItem, Obj(a0))); break;
            case "REMOVE": act.Add(new GameAction(ActionType.UnwearItem, Obj(a0))); break;
            case "CREATE": act.Add(new GameAction(ActionType.CreateItem, Obj(a0))); break;
            case "DESTROY": act.Add(new GameAction(ActionType.DestroyItem, Obj(a0))); break;
            case "SWAP": act.Add(new GameAction(ActionType.SwapItems, Obj(a0), b: Obj(a1))); break;
            case "PLACE": act.Add(new GameAction(ActionType.MoveItem, Obj(a0), b: Location(a1))); break;
            case "PUTIN": act.Add(new GameAction(ActionType.MoveItem, Obj(a0), b: Location(a1))); break;
            case "TAKEOUT": act.Add(new GameAction(ActionType.MoveItem, Obj(a0), b: Locations.Carried)); break;
            case "PUTO":
                act.Add(new GameAction(ActionType.MoveItem, "$noun1", b: Location(a0)));
                notes.Add("PAWS PUTO: moves the object named by the first noun (PAWS uses the last referenced object, flag 51).");
                break;
            case "DROPALL": act.Add(new GameAction(ActionType.DropAll)); break;
            case "AUTOG": act.Add(new GameAction(ActionType.TakeItem, "$noun1")); break;
            case "AUTOD": act.Add(new GameAction(ActionType.DropItem, "$noun1")); break;
            case "AUTOW": act.Add(new GameAction(ActionType.WearItem, "$noun1")); break;
            case "AUTOR": act.Add(new GameAction(ActionType.UnwearItem, "$noun1")); break;
            case "AUTOP": act.Add(new GameAction(ActionType.MoveItem, "$noun1", b: Location(a0))); break;
            case "AUTOT": act.Add(new GameAction(ActionType.MoveItem, "$noun1", b: Locations.Carried)); break;

            // ---------------------------------------------------------- flags
            case "SET": Set(FlagW(a0), 255); break;
            case "CLEAR": Set(FlagW(a0), 0); break;
            case "LET": Set(FlagW(a0), a1); break;
            case "PLUS": if (FlagW(a0) is { } fp) act.Add(new GameAction(ActionType.AddVar, fp, a1)); break;
            case "MINUS": if (FlagW(a0) is { } fm) act.Add(new GameAction(ActionType.AddVar, fm, -a1)); break;
            case "COPYFF": if (FlagW(a1) is { } fc) act.Add(new GameAction(ActionType.CopyVar, fc, b: FlagR(a0))); break;
            case "RANDOM": if (FlagW(a0) is { } fr) act.Add(new GameAction(ActionType.RandomVar, fr, 100)); break;
            case "ABILITY":
                act.Add(new GameAction(ActionType.SetVar, FlagR(37), a0));
                act.Add(new GameAction(ActionType.SetVar, FlagR(52), a1));
                notes.Add("PAWS ABILITY: sets flags 37/52; the first ABILITY found also sets MaxCarriedItems / MaxCarriedWeight, later changes only update the flags.");
                break;

            // ---------------------------------------------------------- movement / description
            case "GOTO": act.Add(new GameAction(ActionType.GoTo, Room(a0))); break;
            case "DESC":
                act.Add(new GameAction(ActionType.Look));
                act.Add(new GameAction(ActionType.Done));
                break;

            // ---------------------------------------------------------- text
            case "MESSAGE":
            {
                var t = MessageById(a0).TrimEnd(' ');
                if (t.EndsWith('\n')) t = t[..^1];
                act.Add(new GameAction(ActionType.Message, text: t));
                break;
            }
            case "MES": act.Add(new GameAction(ActionType.Message, n: 1, text: MessageById(a0))); break;
            case "SYSMESS":
                act.Add(new GameAction(ActionType.Message, n: 1, text: MessageText(LightClean(db.RawSysMessage(a0)))));
                if (a0 >= db.NumSysMessages) Warn($"System message {a0} does not exist.", "");
                break;
            case "NEWLINE": act.Add(new GameAction(ActionType.Message, text: "")); break;
            case "PRINT": act.Add(new GameAction(ActionType.Message, n: 1, text: $"{{var:{FlagR(a0)}}}")); break;
            case "INVEN":
                act.Add(new GameAction(ActionType.Inventory));
                act.Add(new GameAction(ActionType.Done));
                break;
            case "TURNS": act.Add(new GameAction(ActionType.ShowTurns)); break;
            case "SCORE": act.Add(new GameAction(ActionType.ShowScore)); usesScore = true; break;
            case "CLS": act.Add(new GameAction(ActionType.ClearScreen)); break;
            case "PICTURE": act.Add(new GameAction(ActionType.ShowPicture, $"p{a0}")); break;
            case "ANYKEY": act.Add(new GameAction(ActionType.Pause, n: 0)); break;
            case "PAUSE": act.Add(new GameAction(ActionType.Pause, n: (a0 == 0 ? 256 : a0) * 20)); break;
            case "BEEP": act.Add(new GameAction(ActionType.Beep)); break;

            // ---------------------------------------------------------- control
            case "DONE": act.Add(new GameAction(ActionType.Done)); break;
            case "NOTDONE":
                act.Add(new GameAction(ActionType.Done));
                notes.Add("PAWS NOTDONE: mapped to Done (it ends the table like DONE but reports that nothing was done).");
                break;
            case "OK": act.Add(new GameAction(ActionType.Ok)); break;
            case "PROCESS": act.Add(new GameAction(ActionType.RunTrigger, $"proc{a0}")); break;
            case "END": act.Add(new GameAction(ActionType.Lose)); break;
            case "QUIT": act.Add(new GameAction(ActionType.Quit)); break;
            case "SAVE": act.Add(new GameAction(ActionType.Save)); break;
            case "LOAD": act.Add(new GameAction(ActionType.Restore)); break;
            case "RAMSAVE":
                act.Add(new GameAction(ActionType.Save));
                notes.Add("PAWS RAMSAVE / RAMLOAD: mapped to Save / Restore (RAMLOAD's \"last flag to restore\" parameter is ignored).");
                break;
            case "RAMLOAD": act.Add(new GameAction(ActionType.Restore)); break;

            // ---------------------------------------------------------- no equivalent
            case "LISTOBJ": Skip(name, "no action equivalent; Settings.AutoListItems is enabled instead so objects are listed with the room description."); break;
            case "LISTAT": Skip(name, "listing the objects at another location has no equivalent and was skipped."); break;
            case "DOALL": Skip(name, "\"ALL\" loops were skipped; the engine's parser expands ALL for its built-in commands."); break;
            case "COPYOF": Skip(name, "copying an object's location into a flag has no equivalent and was skipped."); break;
            case "COPYFO": Skip(name, "setting an object's location from a flag has no equivalent and was skipped."); break;
            case "COPYOO": Skip(name, "copying one object's location to another has no equivalent and was skipped."); break;
            case "WHATO": Skip(name, "the current-object mechanism (flags 51, 54-57) is not modelled; skipped (AUTO actions use $noun1)."); break;
            case "WEIGH": Skip(name, "weighing objects into flags has no equivalent and was skipped."); break;
            case "WEIGHT": Skip(name, "totalling carried weight into a flag has no equivalent and was skipped."); break;
            case "ADD": Skip(name, "adding one flag to another has no equivalent action and was skipped."); break;
            case "SUB": Skip(name, "subtracting one flag from another has no equivalent action and was skipped."); break;
            case "PARSE": Skip(name, "parsing quoted speech for characters is not supported; skipped."); break;
            case "NEWTEXT": Skip(name, "discarding the rest of the input line is not modelled; skipped."); break;
            case "RESET": Skip(name, "multi-part game object reset has no equivalent and was skipped."); break;
            case "EXTERN": Skip(name, "machine-code extensions cannot be run and were skipped."); break;
            case "MODE": Skip(name, "screen modes are a presentation detail and were skipped."); break;
            case "LINE": Skip(name, "the split-screen line is a presentation detail and was skipped."); break;
            case "PROTECT": Skip(name, "screen protection is a presentation detail and was skipped."); break;
            case "TIME": Skip(name, "input time-outs are not supported and were skipped."); break;
            case "INPUT": Skip(name, "input options are not supported and were skipped."); break;
            case "PROMPT": Skip(name, "prompt selection was skipped (the input marker system message is used as the prompt)."); break;
            case "GRAPHIC": Skip(name, "picture on/off options were skipped (pictures follow the player's settings)."); break;
            case "CHARSET": Skip(name, "custom character sets for text were skipped."); break;
            case "PAPER": Skip(name, "text colours were skipped."); break;
            case "INK": Skip(name, "text colours were skipped."); break;
            case "BORDER": Skip(name, "the border colour was skipped."); break;
            case "SAVEAT": Skip(name, "print-position control was skipped."); break;
            case "BACKAT": Skip(name, "print-position control was skipped."); break;
            case "PRINTAT": Skip(name, "print-position control was skipped."); break;
            default: Skip(name, "unsupported condact skipped."); break;
        }
    }

    // ================================================================ pictures

    private void ReadPictures()
    {
        var conv = new PawsPictureConverter(db);
        for (int n = 0; n < db.NumLocations; n++)
        {
            var pic = conv.Convert(n);
            if (pic == null) continue;
            adv.Pictures.Add(pic);
            if (!pic.IsSubroutine) adv.Rooms[n].PictureId = pic.Id;
        }
        foreach (var note in conv.Notes) notes.Add(note);
        if (conv.UsesCustomCharset)
            notes.Add("Picture TEXT commands using the database's own character sets or UDGs were drawn pixel by pixel from the glyph data.");
        if (db.NumCharsets > 0 && db.DefaultCharset != 0)
            notes.Add($"The game prints its text in custom character set {db.DefaultCharset}; the engine's own font is used instead.");
    }

    // ================================================================ variables & settings

    private static readonly Dictionary<int, string> FlagDescriptions = new()
    {
        [0] = "Darkness: non-zero means dark (object 0 is the light source)",
        [1] = "Number of objects carried (not worn) – maintained by the engine as @carried",
        [2] = "Auto-decremented when a location is described",
        [3] = "Auto-decremented when a location is described while dark",
        [4] = "Auto-decremented when a location is described while dark and object 0 is absent",
        [5] = "Auto-decremented every turn",
        [6] = "Auto-decremented every turn",
        [7] = "Auto-decremented every turn",
        [8] = "Auto-decremented every turn",
        [9] = "Auto-decremented every turn while dark",
        [10] = "Auto-decremented every turn while dark and object 0 is absent",
        [29] = "Picture control flags (bit 7 redraw, bit 6 always, bit 5 never)",
        [30] = "Score (percentage) – mapped to @score",
        [31] = "Turns (low byte) – mapped to @turns",
        [32] = "Turns (high byte)",
        [33] = "Verb of the current logical sentence",
        [34] = "First noun of the current logical sentence",
        [35] = "Adjective of the first noun",
        [36] = "Adverb",
        [37] = "Maximum number of objects conveyable",
        [38] = "Current location – mapped to @room",
        [39] = "Top line of the screen",
        [40] = "Screen mode",
        [41] = "Split screen line",
        [42] = "Prompt system message",
        [43] = "Preposition",
        [44] = "Second noun",
        [45] = "Adjective of the second noun",
        [46] = "Current pronoun noun",
        [47] = "Current pronoun adjective",
        [48] = "Timeout duration",
        [49] = "Timeout control flags",
        [50] = "DOALL location",
        [51] = "Last object referenced",
        [52] = "Strength: maximum weight carried and worn",
        [53] = "Object list flags",
        [54] = "Location of the current object",
        [55] = "Weight of the current object",
        [56] = "128 if the current object is a container",
        [57] = "128 if the current object is wearable",
    };

    private void BuildVariables()
    {
        usedFlags.Add(0);
        usedFlags.Add(37);
        usedFlags.Add(52);
        foreach (var f in usedFlags.OrderBy(f => f))
            adv.Variables.Add(new Variable
            {
                Name = $"f{f}",
                InitialValue = f switch { 37 => 4, 52 => 10, 46 or 47 => 255, _ => 0 },
                Description = FlagDescriptions.TryGetValue(f, out var d) ? d : "PAWS flag",
            });
        adv.Variables.AddRange(chainVariables);
        if (usedFlags.Any(f => f is >= 2 and <= 10))
            notes.Add("PAWS auto-decrementing flags 2-10 are imported as ordinary variables; the engine does not decrement them automatically.");
    }

    private void ApplySettings()
    {
        var s = adv.Settings;
        s.MaxCarriedItems = ability?.Objects ?? 4;
        s.MaxCarriedWeight = ability?.Weight ?? 10;
        s.AutoListItems = usesListObj;
        if (usesScore) s.MaxScore = 100;
        notes.Add($"PAWS limits carrying through flag 37 (objects, initially 4) and flag 52 (weight, initially 10): " +
                  $"MaxCarriedItems = {s.MaxCarriedItems}, MaxCarriedWeight = {s.MaxCarriedWeight}" +
                  (ability != null ? " (from the game's ABILITY action)." : "."));

        int bright = db.DefaultBright == 1 ? 8 : 0;
        if (db.DefaultInk < 8 && db.DefaultPaper < 8 && db.DefaultInk != db.DefaultPaper)
        {
            s.TextColor = Hex(Palettes.Spectrum[db.DefaultInk + bright]);
            s.BackgroundColor = Hex(Palettes.Spectrum[db.DefaultPaper + bright]);
        }
    }

    private static string Hex(uint argb) => $"#{argb & 0xFFFFFF:X6}";

    private void FinishNotes()
    {
        foreach (var (condact, count) in skipped.OrderBy(k => k.Key))
            warnings.Add($"PAWS condact {condact} has no equivalent ({count} use{(count == 1 ? "" : "s")} skipped).");
        adv.Notes.AddRange(notes.OrderBy(n => n, StringComparer.Ordinal));
    }

    private static string TitleFromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        if (name.Length == 0) return "PAWS Adventure";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }
}
