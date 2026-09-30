using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Gac;

/// <summary>Turns a raw <see cref="GacDatabase"/> into an <see cref="Adventure"/>. See <see cref="GacImporter"/> for the mapping.</summary>
internal sealed partial class GacConverter
{
    private const string DarkVar = "gac_dark";
    private const string DarknessRoutine = "gac_darkness";
    private const int CarriedRoom = 255;

    private readonly GacDatabase db;
    private readonly Adventure adv = new();
    public List<string> Warnings { get; } = new();

    private readonly Dictionary<int, (string Id, List<string> Words)> verbGroups = new();
    private readonly Dictionary<int, List<string>> nounGroups = new();
    private readonly Dictionary<int, List<string>> adverbGroups = new();
    /// <summary>Verb number → rooms where it is a connection (GAC moves the player before running the tables).</summary>
    private readonly Dictionary<int, SortedSet<int>> exitRooms = new();
    private readonly SortedSet<int> markers = new();
    private readonly SortedSet<int> counters = new();
    private bool needRoomIndex;
    // The highest constant ROOM is compared with (< or >), when ROOM is never copied or compared with a variable.
    private int roomConstMax = -1;
    private bool needExactRoomIndex;
    private bool usesDarkness;
    private int? strength;
    private readonly HashSet<string> seenNotes = new();

    // context of the table being converted
    private enum Table { High, Local, Low }
    private Table table;
    private int localRoom;

    public GacConverter(GacDatabase db, string title)
    {
        this.db = db;
        adv.Title = title;
        adv.Author = "";
        adv.Description = "Imported from a Graphic Adventure Creator game.";
    }

    public Adventure Convert()
    {
        var s = adv.Settings;
        s.LegacyTableSemantics = true;
        s.AutoListExits = false;         // GAC never lists exits by itself
        s.ExitsBeforeTriggers = true;    // GAC moves the player (connection table) before scanning the local/low priority tables
        s.AutoListItems = true;          // "I can also see" (message 253)
        s.SignificantLetters = 0;        // GAC matches any typed prefix of a stored word, not a fixed length
        s.SpellingCorrection = false;
        s.Verbose = true;
        s.MaxCarriedItems = 255;         // GAC limits weight only
        s.MaxCarriedWeight = 250;        // GAC's initial strength
        s.AllowUndo = true;

        BuildVocabulary();
        BuildRooms();
        BuildItems();
        BuildMessages();
        BuildPictures();

        var high = GacDecompiler.Decompile(db.HighPriority);
        var low = GacDecompiler.Decompile(db.LowPriority);
        var local = db.Local.Select(l => (l.Room, Lines: GacDecompiler.Decompile(l.Code))).ToList();
        usesDarkness = WritesLightMarkers(high) || WritesLightMarkers(low) || local.Any(l => WritesLightMarkers(l.Lines));

        table = Table.High;
        int priority = 30000;
        for (int i = 0; i < high.Count; i++) EmitLine(high[i], $"hp{i + 1}", priority--, null);
        foreach (var (room, lines) in local)
        {
            table = Table.Local;
            localRoom = room;
            priority = 20000;
            for (int i = 0; i < lines.Count; i++) EmitLine(lines[i], $"lc{room}_{i + 1}", priority--, $"r{room}");
        }
        table = Table.Low;
        priority = 10000;
        for (int i = 0; i < low.Count; i++) EmitLine(low[i], $"lp{i + 1}", priority--, null);

        if (strength is int st) s.MaxCarriedWeight = st;
        if (usesDarkness) AddDarkness();
        if (needRoomIndex) FillRoomGaps(needExactRoomIndex ? int.MaxValue : roomConstMax);
        BuildVariables();
        AddMappingNotes();
        return adv;
    }

    private void Note(string text)
    {
        if (seenNotes.Add(text))
        {
            adv.Notes.Add(text);
            Warnings.Add(text);
        }
    }

    // ---- vocabulary -------------------------------------------------------------------------------------------

    private static readonly (string Canonical, string[] Words)[] DirectionWords =
    {
        ("north", new[] { "n", "north", "norte", "nord" }),
        ("south", new[] { "s", "south", "sur", "sud", "syd" }),
        ("east", new[] { "e", "east", "este", "est", "ost" }),
        ("west", new[] { "w", "west", "o", "oeste", "ouest", "vest" }),
        ("northeast", new[] { "ne", "northeast", "noreste", "nordeste", "nordest" }),
        ("northwest", new[] { "nw", "northwest", "no", "noroeste", "nordoeste", "nordouest" }),
        ("southeast", new[] { "se", "southeast", "sureste", "sudeste", "sudest" }),
        ("southwest", new[] { "sw", "southwest", "so", "suroeste", "sudoeste", "sudouest" }),
        ("up", new[] { "u", "up", "arriba", "sube", "subir", "ar", "haut" }),
        ("down", new[] { "d", "down", "abajo", "baja", "bajar", "ab", "bas" }),
        ("in", new[] { "in", "enter", "inside", "entra", "entrar", "dentro" }),
        ("out", new[] { "out", "exit", "leave", "outside", "sal", "salir", "fuera", "sortir" }),
    };

    internal static string? CanonicalDirection(IEnumerable<string> words)
    {
        foreach (var w in words)
            foreach (var (canonical, list) in DirectionWords)
                if (list.Contains(w, StringComparer.OrdinalIgnoreCase)) return canonical;
        return null;
    }

    private void BuildVocabulary()
    {
        var voc = adv.Vocabulary;
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in db.Verbs.Where(w => w.Text.Length > 0).GroupBy(w => w.Number))
        {
            var words = group.Select(w => w.Text.ToLowerInvariant()).Distinct().ToList();
            // Movement verbs get the canonical direction as their id ("n", "north" → "north").
            var id = CanonicalDirection(words) ?? words[0];
            if (!usedIds.Add(id)) { id = $"{words[0]}{group.Key}"; usedIds.Add(id); }
            verbGroups[group.Key] = (id, words);
            voc.Verbs.Add(new VerbDefinition { Id = id, Words = words });
        }
        foreach (var group in db.Nouns.Where(w => w.Text.Length > 0).GroupBy(w => w.Number))
        {
            var words = group.Select(w => w.Text.ToLowerInvariant()).Distinct().ToList();
            nounGroups[group.Key] = words;
            if (group.Key == 255)
            {
                Note($"Pronoun words ({string.Join(", ", words)}) refer to the previous noun (GAC noun 255); the engine's own pronoun handling is used.");
                continue;
            }
            foreach (var w in words) if (!voc.Nouns.Contains(w)) voc.Nouns.Add(w);
        }
        foreach (var group in db.Adverbs.Where(w => w.Text.Length > 0).GroupBy(w => w.Number))
        {
            var words = group.Select(w => w.Text.ToLowerInvariant()).Distinct().ToList();
            adverbGroups[group.Key] = words;
            foreach (var w in words) if (!voc.Adverbs.Contains(w)) voc.Adverbs.Add(w);
        }
        foreach (var w in db.Verbs.Concat(db.Nouns).Concat(db.Adverbs))
            if (w.Text.Length > 0) voc.LegacyWordNumbers.TryAdd(w.Text.ToLowerInvariant(), w.Number);
    }

    private string VerbId(int n)
    {
        if (verbGroups.TryGetValue(n, out var g)) return g.Id;
        Note($"Verb {n} is used by a condition but is not in the vocabulary.");
        return $"verb{n}";
    }

    private string NounWord(int n)
    {
        if (nounGroups.TryGetValue(n, out var g)) return g[0];
        Note($"Noun {n} is used by a condition but is not in the vocabulary.");
        return $"noun{n}";
    }

    private string AdverbWord(int n)
    {
        if (adverbGroups.TryGetValue(n, out var g)) return g[0];
        Note($"Adverb {n} is used by a condition but is not in the vocabulary.");
        return $"adverb{n}";
    }

    private string ExitDirection(int verb)
    {
        if (!verbGroups.TryGetValue(verb, out var g)) return $"verb{verb}";
        return CanonicalDirection(g.Words) ?? g.Words[0];
    }

    // ---- rooms and items --------------------------------------------------------------------------------------

    private static string RoomName(string description, int number)
    {
        var d = description.Trim();
        if (d.Length == 0) return $"Room {number}";
        int end = d.IndexOfAny(new[] { '.', '!', '?', '\n' });
        var first = end > 0 ? d[..end] : d;
        if (first.Length > 40)
        {
            int cut = first.LastIndexOf(' ', 40);
            first = (cut > 10 ? first[..cut] : first[..40]) + "…";
        }
        return first;
    }

    private void BuildRooms()
    {
        foreach (var room in db.Rooms.Values)
        {
            var r = new Room
            {
                Id = $"r{room.Number}",
                Name = RoomName(room.Description, room.Number),
                Description = room.Description.Trim(),
                PictureId = room.Picture != 0 && db.Pictures.ContainsKey(room.Picture) ? $"p{room.Picture}" : null,
            };
            if (room.Picture != 0 && !db.Pictures.ContainsKey(room.Picture))
                Note($"Room {room.Number} uses picture {room.Picture}, which is not in the database.");
            foreach (var (verb, dest) in room.Exits)
            {
                var dir = ExitDirection(verb);
                r.Exits.Add(new Exit { Direction = dir, TargetRoomId = $"r{dest}" });
                if (!db.Rooms.ContainsKey(dest)) Note($"Room {room.Number} has an exit to room {dest}, which does not exist.");
                if (!exitRooms.TryGetValue(verb, out var set)) exitRooms[verb] = set = new SortedSet<int>();
                set.Add(room.Number);
                if (verbGroups.TryGetValue(verb, out var g))
                    foreach (var w in g.Words)
                        if (!string.Equals(w, dir, StringComparison.OrdinalIgnoreCase) || !DirectionWords.Any(d => d.Canonical == dir))
                            adv.Vocabulary.Directions.TryAdd(w, dir);
            }
            adv.Rooms.Add(r);
        }
        adv.StartRoomId = $"r{db.StartRoom}";
        if (!db.Rooms.ContainsKey(db.StartRoom))
        {
            Note($"The start room {db.StartRoom} does not exist; using the first room.");
            if (adv.Rooms.Count > 0) adv.StartRoomId = adv.Rooms[0].Id;
        }
    }

    private static readonly string[] Articles = { "a", "an", "the", "some", "un", "una", "unos", "unas", "el", "la", "los", "las" };

    internal static (string Article, string Name) SplitArticle(string text)
    {
        var t = text.Trim();
        int sp = t.IndexOf(' ');
        if (sp > 0)
        {
            var first = t[..sp];
            if (Articles.Contains(first, StringComparer.OrdinalIgnoreCase))
                return (first.ToLowerInvariant(), t[(sp + 1)..].Trim());
        }
        return ("", t);
    }

    private string LocationId(int loc) => loc switch
    {
        0 => Locations.Nowhere,
        CarriedRoom => Locations.Carried,
        _ => $"r{loc}",
    };

    /// <summary>
    /// GAC has no object ↔ noun link: games test NOUN n and object numbers separately (by convention noun n is
    /// object n, which is what GET NO1 relies on). Use the noun groups whose words appear in the object's name,
    /// else the noun with the same number.
    /// </summary>
    private List<string> ItemNouns(GacObject o)
    {
        var nameWords = new HashSet<string>(
            o.Name.ToLowerInvariant().Split(new[] { ' ', ',', '.', '-', '!', '?', ':', '\'' }, StringSplitOptions.RemoveEmptyEntries));
        var result = new List<string>();
        foreach (var (number, words) in nounGroups)
            if (number != 255 && words.Any(nameWords.Contains))
                result.AddRange(words.Where(w => !result.Contains(w)));
        if (result.Count == 0 && nounGroups.TryGetValue(o.Number, out var same) && o.Number != 255)
            result.AddRange(same);
        return result;
    }

    private void BuildItems()
    {
        foreach (var o in db.Objects.Values)
        {
            var (article, name) = SplitArticle(o.Name);
            var item = new Item
            {
                Id = $"o{o.Number}",
                Name = name,
                Article = article,
                Weight = o.Weight,
                Location = LocationId(o.Location),
                Portable = true,
            };
            item.Nouns.AddRange(ItemNouns(o));
            if (o.Location != 0 && o.Location != CarriedRoom && !db.Rooms.ContainsKey(o.Location))
                Note($"Object {o.Number} starts in room {o.Location}, which does not exist.");
            adv.Items.Add(item);
        }
    }

    private void BuildMessages()
    {
        string? M(int n) => db.Messages.TryGetValue(n, out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : null;
        var msgs = adv.Messages;
        if (M(240) is { } prompt) adv.Settings.Prompt = prompt.EndsWith(' ') ? prompt : prompt + " ";
        if (M(241) is { } cant) msgs[Msg.CantDoThat] = cant;
        if (M(242) is { } pardon) { msgs[Msg.DontUnderstand] = pardon; }
        if (M(245) is { } have) msgs[Msg.AlreadyCarrying] = have;
        if (M(246) is { } notHave) msgs[Msg.NotCarrying] = notHave;
        if (M(247) is { } notSee) msgs[Msg.NotHere] = notSee;
        if (M(248) is { } heavy) msgs[Msg.TooHeavy] = heavy;
        if (M(251) is { } dark) msgs[Msg.Dark] = dark;
        if (M(253) is { } see) msgs[Msg.YouCanSee] = see + " {list}";
        if (M(254) is { } ok) msgs[Msg.Ok] = ok;
        if (M(249) is { } score)
            msgs[Msg.Score] = $"{score} {{score}}{(M(250) is { } took ? " " + took + " {turns}" + (M(255) is { } turns ? " " + turns : "") : "")}";
    }

    // ---- variables --------------------------------------------------------------------------------------------

    private string Marker(int n)
    {
        if (n == 0) Note("Marker 0 (\"a room has just been described\") is maintained by the GAC interpreter; the imported game only sees values set by its own conditions.");
        markers.Add(n);
        return $"m{n}";
    }

    private string CounterRead(int c)
    {
        switch (c)
        {
            case 0: return "@score";
            case 126: return "@turns";
            case 127: throw new GacConvertException("counter 127 (turns, high byte) has no equivalent");
            default:
                counters.Add(c);
                return $"c{c}";
        }
    }

    private void BuildVariables()
    {
        foreach (var m in markers)
            adv.Variables.Add(new Variable
            {
                Name = $"m{m}",
                InitialValue = m == 1 ? 1 : 0,
                Description = m switch
                {
                    0 => "GAC marker 0 (room just described, set by the interpreter)",
                    1 => "GAC marker 1 (this place has light)",
                    2 => "GAC marker 2 (the player carries a light)",
                    3 => "GAC marker 3 (don't show the score at the end)",
                    _ => $"GAC marker {m}",
                },
            });
        foreach (var c in counters)
            adv.Variables.Add(new Variable { Name = $"c{c}", Description = $"GAC counter {c}" });
        if (usesDarkness)
            adv.Variables.Add(new Variable { Name = DarkVar, Description = "1 when GAC markers 1 (light) and 2 (lamp) are both clear" });
    }

    private static bool WritesLightMarkers(List<List<GacSegment>> lines) =>
        lines.SelectMany(l => l).SelectMany(s => s.Statements)
            .Any(n => n.Op is GacOps.Set or GacOps.Rese && n.Args.Length == 1 && (!n.Args[0].IsConst || n.Args[0].Value is 1 or 2));

    private void AddDarkness()
    {
        adv.Settings.DarknessVariable = DarkVar;
        markers.Add(1);
        markers.Add(2);
        adv.Triggers.Add(new Trigger
        {
            Id = "gac_darkness_1", Name = DarknessRoutine, Event = TriggerEvent.Subroutine,
            Actions = { new GameAction(ActionType.SetVar, DarkVar, 1) },
            Notes = "GAC: a place is dark when marker 1 (light) and marker 2 (lamp) are both clear.",
        });
        adv.Triggers.Add(new Trigger
        {
            Id = "gac_darkness_2", Name = DarknessRoutine, Event = TriggerEvent.Subroutine,
            Conditions = { new Condition(ConditionType.VarEquals, "m1", 1) },
            Actions = { new GameAction(ActionType.SetVar, DarkVar, 0) },
        });
        adv.Triggers.Add(new Trigger
        {
            Id = "gac_darkness_3", Name = DarknessRoutine, Event = TriggerEvent.Subroutine,
            Conditions = { new Condition(ConditionType.VarEquals, "m2", 1) },
            Actions = { new GameAction(ActionType.SetVar, DarkVar, 0) },
        });
        Note($"Darkness: GAC markers 1 (light) and 2 (lamp) are combined into variable \"{DarkVar}\" (the DarknessVariable) by the subroutine \"{DarknessRoutine}\", run after every SET/RESE of those markers.");
    }

    /// <summary>
    /// Makes Rooms[i].Id == "r{i}" for every room numbered up to <paramref name="upTo"/>, so that "@room" (the room's
    /// index) equals the GAC room number there; higher-numbered rooms follow in order. When ROOM is only compared with
    /// constants, that keeps every comparison exact without thousands of placeholders for rooms numbered up to 9999.
    /// </summary>
    private void FillRoomGaps(int upTo)
    {
        var byNumber = adv.Rooms.ToDictionary(r => int.Parse(r.Id[1..]));
        int max = Math.Min(byNumber.Keys.DefaultIfEmpty(0).Max(), upTo);
        var list = new List<Room>();
        for (int n = 0; n <= max; n++)
            list.Add(byNumber.TryGetValue(n, out var r) ? r : new Room { Id = $"r{n}", Name = $"(unused GAC room {n})", Description = "" });
        list.AddRange(byNumber.Where(kv => kv.Key > max).OrderBy(kv => kv.Key).Select(kv => kv.Value));
        int added = list.Count - byNumber.Count;
        adv.Rooms = list;
        if (added > 0)
            Note(upTo == int.MaxValue
                ? $"ROOM is used as a number somewhere, so {added} placeholder rooms were added to make each room's index equal its GAC number (@room)."
                : $"ROOM is compared with < or > (up to {upTo}), so {added} placeholder rooms were added to make each room's index up to there equal its GAC number (@room).");
    }

    private void AddMappingNotes()
    {
        adv.Notes.Insert(0, string.Join(" ", new[]
        {
            "Imported from Graphic Adventure Creator.",
            "Turn order in GAC: high priority conditions (every turn, before input) → connections (moving ends the turn) → local conditions of the room → low priority conditions.",
            "High priority → EveryTurn triggers (priority 30000 down; copies that don't test the command also run at GameStart, as GAC runs the table before the first input).",
            "Local → BeforeCommand triggers for the room (priority 20000 down); low priority → BeforeCommand triggers (priority 10000 down); all with StopsCommand = false; WAIT → Done, OKAY → Ok.",
            "OR/XOR conditions were split into one trigger per disjunct (the original line is in each trigger's Notes).",
            "Markers → m{n} (0/1), counters → c{n}; counter 0 = score (@score, AwardScore), TURN/counter 126 = @turns.",
            "Objects o{n} get the nouns whose words appear in their names (else noun n); GET NO1 etc. rely on GAC's convention that noun n is object n and become \"$noun1\" unless the condition fixes NO1; room 0 = nowhere, 255 = carried.",
        }));
    }
}
