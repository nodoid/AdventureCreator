using System.Globalization;
using System.Text;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.ScottAdams;

/// <summary>
/// Scott Adams format games (.dat / .sao): Scott Adams' own adventures, games written with the open-source ScottKit
/// compiler, and others played by ScottFree. The action table becomes triggers, as for PAWS and the Quill.
/// </summary>
public sealed class ScottAdamsImporter : IAdventureImporter
{
    public string Name => "Scott Adams format (ScottKit / ScottFree)";
    public IReadOnlyList<string> Extensions => new[] { "dat", "sao" };

    public bool CanImport(byte[] data, string fileName)
    {
        try { return ScottGame.Parse(data).Rooms.Count > 0; }
        catch { return false; }
    }

    public ImportResult Import(byte[] data, string fileName) => new ScottConverter(ScottGame.Parse(data), fileName).Run();
}

/// <summary>The contents of a Scott Adams .dat file.</summary>
internal sealed class ScottGame
{
    public int MaxCarry, StartRoom, Treasures, WordLength, LightTime, TreasureRoom;
    public List<int[]> Actions { get; } = new();
    public List<string> Verbs { get; } = new();
    public List<string> Nouns { get; } = new();
    public List<(int[] Exits, string Text)> Rooms { get; } = new();
    public List<string> Messages { get; } = new();
    public List<(string Text, int Location)> Items { get; } = new();
    public List<string> Comments { get; } = new();

    public static ScottGame Parse(byte[] data)
    {
        var r = new Reader(Encoding.Latin1.GetString(data));
        var g = new ScottGame();
        r.Int();                                   // text size (unused)
        int ni = r.Int(), na = r.Int(), nw = r.Int(), nr = r.Int();
        g.MaxCarry = r.Int(); g.StartRoom = r.Int(); g.Treasures = r.Int(); g.WordLength = r.Int(); g.LightTime = r.Int();
        int nm = r.Int();
        g.TreasureRoom = r.Int();
        if (ni is < 0 or > 500 || na is < 0 or > 2000 || nw is < 0 or > 1000 || nr is < 1 or > 500 || nm is < 0 or > 1000 || g.WordLength is < 1 or > 8)
            throw new InvalidDataException("Not a Scott Adams game.");
        for (int i = 0; i <= na; i++) { var a = new int[8]; for (int k = 0; k < 8; k++) a[k] = r.Int(); g.Actions.Add(a); }
        for (int i = 0; i <= nw; i++) { g.Verbs.Add(r.Str()); g.Nouns.Add(r.Str()); }
        for (int i = 0; i <= nr; i++) { var e = new int[6]; for (int k = 0; k < 6; k++) e[k] = r.Int(); g.Rooms.Add((e, r.Str())); }
        for (int i = 0; i <= nm; i++) g.Messages.Add(r.Str());
        for (int i = 0; i <= ni; i++) { var t = r.Str(); g.Items.Add((t, r.Int())); }
        try { for (int i = 0; i <= na; i++) g.Comments.Add(r.Str()); } catch (InvalidDataException) { }
        return g;
    }

    private sealed class Reader(string text)
    {
        private int p;

        private void SkipSpace() { while (p < text.Length && char.IsWhiteSpace(text[p])) p++; }

        public int Int()
        {
            SkipSpace();
            int start = p;
            if (p < text.Length && text[p] == '-') p++;
            while (p < text.Length && char.IsDigit(text[p])) p++;
            if (p == start) throw new InvalidDataException("Expected a number.");
            return int.Parse(text.AsSpan(start, p - start), CultureInfo.InvariantCulture);
        }

        public string Str()
        {
            SkipSpace();
            if (p >= text.Length || text[p] != '"') throw new InvalidDataException("Expected a string.");
            p++;
            var sb = new StringBuilder();
            while (p < text.Length)
            {
                char c = text[p++];
                if (c == '"')
                {
                    if (p < text.Length && text[p] == '"') { sb.Append('"'); p++; continue; }
                    break;
                }
                sb.Append(c == '`' ? '"' : c);
            }
            return sb.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}

internal sealed class ScottConverter
{
    private static readonly string[] Directions = { "north", "south", "east", "west", "up", "down" };
    private const int Carried = 255;
    private const int LightSource = 9;

    private readonly ScottGame g;
    private readonly string fileName;
    private readonly Adventure a = new();
    private readonly List<string> warnings = new();
    private readonly Dictionary<string, int> skipped = new();
    private readonly HashSet<string> variables = new();
    private readonly List<string> automaticIds = new();

    public ScottConverter(ScottGame game, string fileName)
    {
        g = game;
        this.fileName = fileName;
    }

    public ImportResult Run()
    {
        a.Title = Title(Path.GetFileNameWithoutExtension(fileName));
        a.Description = $"Imported from the Scott Adams format game {Path.GetFileName(fileName)}.";
        a.Settings.LegacyTableSemantics = true;
        a.Settings.ExitsBeforeTriggers = true;          // ScottFree moves the player before looking at the action table
        a.Settings.SignificantLetters = g.WordLength;
        a.Settings.MaxCarriedItems = g.MaxCarry;
        a.Settings.MaxScore = g.Treasures;
        a.Settings.DarknessVariable = "f15";
        a.Settings.AutoListExits = true;
        a.Settings.AutoListItems = true;
        a.Settings.SpellingCorrection = false;
        a.Settings.Prompt = "Tell me what to do? ";
        SetMessages();

        Vocabulary();
        Rooms();
        Items();
        Actions();
        Light();
        foreach (var v in variables.OrderBy(v => v, StringComparer.Ordinal))
            a.Variables.Add(new Variable { Name = v, InitialValue = v == "lighttime" ? Math.Max(0, g.LightTime) : 0, Description = Describe(v) });

        foreach (var (what, count) in skipped)
            warnings.Add($"{what} has no equivalent ({count} use{(count == 1 ? "" : "s")} skipped).");
        a.Notes.Add("Scott Adams format: the automatic actions (verb 0) run every turn, and once before the first command; command actions stop after the first match unless they CONTINUE; flag 15 is darkness and item 9 the light source.");
        a.Notes.AddRange(warnings);
        var result = new ImportResult(a)
        {
            DetectedFormat = $"Scott Adams format ({g.Rooms.Count - 1} rooms, {g.Items.Count} items, {g.Actions.Count} actions, word length {g.WordLength})",
        };
        result.Warnings.AddRange(warnings);
        return result;
    }

    private void SetMessages()
    {
        a.Messages["Ok"] = "O.K.";
        a.Messages["Taken"] = "O.K.";
        a.Messages["Dropped"] = "O.K.";
        a.Messages["CantGo"] = "I can't go in that direction.";
        a.Messages["DontUnderstand"] = "You use word(s) I don't know!";
        a.Messages["UnknownWord"] = "You use word(s) I don't know!";
        a.Messages["CantDoThat"] = "I can't do that yet.";
        a.Messages["NotHere"] = "It's beyond my power to do that.";
        a.Messages["TooMany"] = "I've too much to carry!";
        a.Messages["Dark"] = "I can't see. It is too dark!";
        a.Messages["YouCanSee"] = "I can also see: {list}";
        a.Messages["Exits"] = "Obvious exits: {list}.";
        a.Messages["NoExits"] = "Obvious exits: none.";
        a.Messages["Carrying"] = "I'm carrying: {list}";
        a.Messages["CarryingNothing"] = "I'm carrying: nothing.";
    }

    // ================================================================= words

    /// <summary>Trigger patterns per word number: every synonym, separated by | (e.g. "go|ent|run|wal|cli").</summary>
    private readonly Dictionary<int, string> verbIds = new();
    private readonly Dictionary<int, string> nounWords = new();

    private static string Word(string w) => w.TrimStart('*').Trim().ToLowerInvariant();

    private void Vocabulary()
    {
        // Words starting with * are synonyms of the word before them.
        var verbGroups = Groups(g.Verbs);
        var nounGroups = Groups(g.Nouns);
        foreach (var (index, words) in verbGroups)
        {
            if (index == 0 || words.Count == 0) continue;
            // All synonyms match (in Scott Adams games CLIMB, ENTER, RUN and WALK are often synonyms of GO).
            verbIds[index] = string.Join('|', words.Prepend(index == 1 ? "go" : words[0]).Distinct());
            if (index == 1)
            {
                foreach (var w in words.Where(w => w != "go"))
                    if (!a.Vocabulary.Verbs.Any(v => v.Id == w)) a.Vocabulary.Verbs.Add(new VerbDefinition { Id = w, Words = { w } });
                continue;
            }
            if (Directions.Contains(words[0])) continue;
            // GET (10) and DROP (18) are ScottFree's built-in take and drop; other words that abbreviate a built-in
            // verb (LOO, INV, SCO…) extend it, so they keep their standard behaviour when no action applies.
            var id = index == 10 ? "take" : index == 18 ? "drop" : BuiltInVerbFor(words) ?? words[0];
            var existing = a.Vocabulary.Verbs.FirstOrDefault(v => v.Id == id);
            if (existing == null)
            {
                // No grammar lines: as for PAWS and the Quill, the action table decides what a sentence means.
                a.Vocabulary.Verbs.Add(new VerbDefinition { Id = id, Words = words.Distinct().ToList() });
            }
            else foreach (var w in words) if (!existing.Words.Contains(w)) existing.Words.Add(w);
        }
        foreach (var (index, words) in nounGroups)
        {
            if (index == 0 || words.Count == 0) continue;
            if (index <= 6)
            {
                nounWords[index] = Directions[index - 1];
                foreach (var w in words) if (!Directions.Contains(w)) a.Vocabulary.Directions.TryAdd(w, Directions[index - 1]);
                continue;
            }
            nounWords[index] = string.Join('|', words.Distinct());
            foreach (var w in words) if (!a.Vocabulary.Nouns.Contains(w)) a.Vocabulary.Nouns.Add(w);
        }
    }

    /// <summary>The engine's built-in verbs: id by (single) word, and all ids.</summary>
    private static readonly List<(string Word, string Id)> BuiltInWords = Core.Parsing.BuiltInLexicon.VerbTable
        .Select(row => row.Split('|'))
        .SelectMany(cols => cols[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Contains(' ')).Select(w => (w, cols[0].Trim().TrimEnd('!'))))
        .ToList();
    private static readonly HashSet<string> BuiltInIds = BuiltInWords.Select(x => x.Id).ToHashSet();

    /// <summary>The built-in verb a (possibly truncated) game word stands for, if exactly one fits.</summary>
    private string? BuiltInVerbFor(List<string> words)
    {
        foreach (var w in words)
        {
            if (w.Length < 2) continue;
            var exact = BuiltInWords.Where(x => x.Word == w).Select(x => x.Id).Distinct().ToList();
            if (exact.Count == 1) return exact[0];
            if (w.Length >= Math.Min(3, g.WordLength) && w.Length == g.WordLength)
            {
                var prefix = BuiltInWords.Where(x => x.Word.StartsWith(w, StringComparison.Ordinal)).Select(x => x.Id).Distinct().ToList();
                if (prefix.Count == 1) return prefix[0];
            }
        }
        return null;
    }

    private static Dictionary<int, List<string>> Groups(List<string> list)
    {
        var groups = new Dictionary<int, List<string>>();
        int current = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var raw = list[i];
            if (!raw.StartsWith('*') || i == 0) current = i;
            var w = Word(raw);
            if (w.Length == 0) continue;
            if (!groups.TryGetValue(current, out var l)) groups[current] = l = new List<string>();
            l.Add(w);
        }
        return groups;
    }

    // ================================================================= rooms and items

    private static string RoomId(int n) => $"r{n}";
    private static string ItemId(int n) => $"o{n}";

    private void Rooms()
    {
        // Room 0 is the store room where absent items live, so it isn't a real room.
        for (int i = 1; i < g.Rooms.Count; i++)
        {
            var (exits, text) = g.Rooms[i];
            var description = text.StartsWith('*') ? text[1..] : text.Length == 0 ? "" : "I'm in a " + text;
            var room = new Room { Id = RoomId(i), Name = RoomName(text, i), Description = description.Trim() };
            for (int d = 0; d < 6; d++)
                if (exits[d] > 0 && exits[d] < g.Rooms.Count) room.Exits.Add(new Exit { Direction = Directions[d], TargetRoomId = RoomId(exits[d]) });
            a.Rooms.Add(room);
        }
        a.StartRoomId = RoomId(Math.Clamp(g.StartRoom, 1, g.Rooms.Count - 1));
    }

    private static string RoomName(string text, int n)
    {
        var t = text.TrimStart('*').Trim();
        // Names drop the narration: "I'm in the vault." → "The vault".
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^(I'm|I am)\s+(in|on|at)\s+(an?\s+)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (t.Length == 0) return $"Room {n}";
        int end = t.IndexOfAny(new[] { '.', '\n', '!' });
        if (end > 0) t = t[..end];
        if (t.Length > 50) t = t[..50].TrimEnd() + "…";
        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    private void Items()
    {
        for (int i = 0; i < g.Items.Count; i++)
        {
            var (text, loc) = g.Items[i];
            string? autoget = null;
            var name = text;
            int slash = text.TrimEnd().EndsWith('/') ? text.LastIndexOf('/', text.TrimEnd().Length - 2) : -1;
            if (slash >= 0)
            {
                autoget = text[(slash + 1)..].TrimEnd().TrimEnd('/').ToLowerInvariant();
                name = text[..slash];
            }
            name = name.Trim();
            var item = new Item
            {
                Id = ItemId(i), Name = name.Length == 0 ? $"item {i}" : name,
                Location = loc == 0 ? "" : loc == Carried ? Locations.Carried : RoomId(loc),
                Portable = autoget != null,
                LightSource = i == LightSource,
            };
            if (autoget is { Length: > 0 }) item.Nouns.Add(autoget);
            // Also any noun words that appear in the name.
            foreach (var w in name.ToLowerInvariant().Split(new[] { ' ', '*', '-', ',', '.', '!', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var cut = w.Length > g.WordLength ? w[..g.WordLength] : w;
                if (a.Vocabulary.Nouns.Any(n => n == w || n == cut) && !item.Nouns.Contains(w)) item.Nouns.Add(w);
            }
            a.Items.Add(item);
        }
    }

    // ================================================================= actions

    private void Actions()
    {
        var triggers = new Trigger?[g.Actions.Count];
        for (int i = 0; i < g.Actions.Count; i++) triggers[i] = Convert(i);

        // CONTINUE (action 73) runs the entries with vocabulary 0 that follow it.
        for (int i = 0; i < g.Actions.Count; i++)
        {
            if (triggers[i] is not { } t || !continuing.Contains(i)) continue;
            for (int k = i + 1; k < g.Actions.Count && g.Actions[k][0] == 0; k++)
                if (triggers[k] != null) t.Actions.Insert(t.Actions.Count - (t.Actions.LastOrDefault()?.Type == ActionType.Done ? 1 : 0), new GameAction(ActionType.RunTrigger, triggers[k]!.Id));
        }
        foreach (var t in triggers) if (t != null) a.Triggers.Add(t);

        // The automatic actions also run once before the first command.
        var start = new Trigger { Id = "scott_start", Name = "Automatic actions before the first command", Event = TriggerEvent.GameStart, StopsCommand = false };
        foreach (var id in automaticIds) start.Actions.Add(new GameAction(ActionType.RunTrigger, id + "_sub"));
        if (automaticIds.Count > 0)
        {
            a.Triggers.Add(start);
            // Subroutine twins of the automatic triggers, for the start.
            foreach (var id in automaticIds)
            {
                var original = a.Triggers.First(t => t.Id == id);
                a.Triggers.Add(new Trigger
                {
                    Id = id + "_sub", Name = original.Name + " (at the start)", Event = TriggerEvent.Subroutine, StopsCommand = false,
                    Conditions = original.Conditions.Select(Clone).ToList(), Actions = original.Actions.Select(Clone).ToList(), Notes = original.Notes,
                });
            }
        }
    }

    private static Condition Clone(Condition c) => new(c.Type, c.A, c.N, c.B, c.Negate);
    private static GameAction Clone(GameAction x) => new(x.Type, x.A, x.N, x.B, x.Text);

    private readonly HashSet<int> continuing = new();

    private Trigger? Convert(int index)
    {
        var e = g.Actions[index];
        int verb = e[0] / 150, noun = e[0] % 150;
        var comment = index < g.Comments.Count ? g.Comments[index].Trim() : "";
        var t = new Trigger { Id = $"a{index}", StopsCommand = false, Notes = $"Scott Adams action {index}" + (comment.Length > 0 ? $": {comment}" : "") };
        bool command;
        if (verb == 0 && noun == 0)
        {
            t.Event = TriggerEvent.Subroutine;      // continuation entry
            t.Name = comment.Length > 0 ? comment : $"Continuation {index}";
            command = false;
        }
        else if (verb == 0)
        {
            t.Event = TriggerEvent.EveryTurn;
            t.Name = comment.Length > 0 ? comment : $"Automatic {index}";
            if (noun < 100) t.Conditions.Add(new Condition(ConditionType.Chance, n: noun));
            automaticIds.Add(t.Id);
            command = false;
        }
        else
        {
            t.Event = TriggerEvent.BeforeCommand;
            if (verb == 1 && noun is >= 1 and <= 6) t.Verb = Directions[noun - 1];
            else
            {
                t.Verb = verbIds.TryGetValue(verb, out var v) ? v : $"verb{verb}";
                t.Noun1 = noun == 0 ? "*" : nounWords.TryGetValue(noun, out var n) ? n : "*";
            }
            t.Name = comment.Length > 0 ? comment : $"{t.Verb} {t.Noun1}".Trim();
            command = true;
        }

        // Conditions; code 0 supplies parameters to the actions.
        var parameters = new Queue<int>();
        for (int k = 1; k <= 5; k++)
        {
            int code = e[k] % 20, arg = e[k] / 20;
            if (code == 0) { parameters.Enqueue(arg); continue; }
            if (ConditionFor(code, arg) is { } c) t.Conditions.Add(c);
        }

        int[] ops = { e[6] / 150, e[6] % 150, e[7] / 150, e[7] % 150 };
        int P() => parameters.Count > 0 ? parameters.Dequeue() : 0;
        foreach (var op in ops) AddAction(t, op, P, index);
        if (command) t.Actions.Add(new GameAction(ActionType.Done));
        return t;
    }

    private Condition? ConditionFor(int code, int arg)
    {
        string item = ItemId(arg);
        switch (code)
        {
            case 1: return new Condition(ConditionType.ItemCarried, item);
            case 2: return new Condition(ConditionType.ItemIn, item, b: Locations.Here);
            case 3: return new Condition(ConditionType.ItemPresent, item);
            case 4: return new Condition(ConditionType.PlayerIn, RoomId(arg));
            case 5: return new Condition(ConditionType.ItemIn, item, b: Locations.Here, negate: true);
            case 6: return new Condition(ConditionType.ItemCarried, item, negate: true);
            case 7: return new Condition(ConditionType.PlayerIn, RoomId(arg), negate: true);
            case 8: return new Condition(ConditionType.VarEquals, Flag(arg), 0, negate: true);
            case 9: return new Condition(ConditionType.VarEquals, Flag(arg), 0);
            case 10: return new Condition(ConditionType.VarGreater, "@carried", 0);
            case 11: return new Condition(ConditionType.VarEquals, "@carried", 0);
            case 12: return new Condition(ConditionType.ItemPresent, item, negate: true);
            case 13: return new Condition(ConditionType.ItemExists, item);
            case 14: return new Condition(ConditionType.ItemExists, item, negate: true);
            case 15: return new Condition(ConditionType.VarLess, Var("counter"), arg + 1);
            case 16: return new Condition(ConditionType.VarGreater, Var("counter"), arg);
            case 17: return new Condition(ConditionType.ItemIn, item, b: InitialLocation(arg));
            case 18: return new Condition(ConditionType.ItemIn, item, b: InitialLocation(arg), negate: true);
            case 19: return new Condition(ConditionType.VarEquals, Var("counter"), arg);
            default: return null;
        }
    }

    private string InitialLocation(int item)
    {
        if (item < 0 || item >= g.Items.Count) return "";
        int loc = g.Items[item].Location;
        return loc == 0 ? "" : loc == Carried ? Locations.Carried : RoomId(loc);
    }

    private string Flag(int n) => Var($"f{n}");

    private string Var(string name)
    {
        variables.Add(name);
        return name;
    }

    private void AddAction(Trigger t, int op, Func<int> param, int index)
    {
        var list = t.Actions;
        if (op == 0) return;
        if (op is >= 1 and <= 51 || op >= 102)
        {
            int m = op <= 51 ? op : op - 50;
            if (m < g.Messages.Count) list.Add(GameAction.Say(g.Messages[m]));
            return;
        }
        switch (op)
        {
            case 52: list.Add(new GameAction(ActionType.MoveItem, ItemId(param()), b: Locations.Carried)); break;   // GET (no weight check)
            case 53: list.Add(new GameAction(ActionType.MoveItem, ItemId(param()), b: Locations.Here)); break;
            case 54: list.Add(new GameAction(ActionType.GoTo, RoomId(param()))); break;
            case 55:
            case 59: list.Add(new GameAction(ActionType.MoveItem, ItemId(param()), b: "")); break;
            case 56: list.Add(new GameAction(ActionType.SetVar, Flag(15), 1)); break;
            case 57: list.Add(new GameAction(ActionType.SetVar, Flag(15), 0)); break;
            case 58: list.Add(new GameAction(ActionType.SetVar, Flag(param()), 1)); break;
            case 60: list.Add(new GameAction(ActionType.SetVar, Flag(param()), 0)); break;
            case 61: // death: the player ends up in the last room ("limbo")
                list.Add(GameAction.Say("I'm dead..."));
                list.Add(new GameAction(ActionType.SetVar, Flag(15), 0));
                list.Add(new GameAction(ActionType.GoTo, RoomId(g.Rooms.Count - 1)));
                break;
            case 62:
            {
                int item = param(), room = param();
                list.Add(new GameAction(ActionType.MoveItem, ItemId(item), b: room == 0 ? "" : room == Carried ? Locations.Carried : RoomId(room)));
                break;
            }
            case 63: list.Add(new GameAction(ActionType.Lose, text: "The game is now over.")); break;
            case 64:
            case 76:
                // GoTo already describes the new room. The common GOTO, CLEAR SCREEN, LOOK becomes CLEAR SCREEN, GOTO.
                if (list.Count >= 2 && list[^1].Type == ActionType.ClearScreen && list[^2].Type == ActionType.GoTo)
                {
                    (list[^1], list[^2]) = (list[^2], list[^1]);
                    break;
                }
                if (list.LastOrDefault()?.Type != ActionType.GoTo) list.Add(new GameAction(ActionType.Look));
                break;
            case 65: Score(list); break;
            case 66: list.Add(new GameAction(ActionType.Inventory)); break;
            case 67: list.Add(new GameAction(ActionType.SetVar, Flag(0), 1)); break;
            case 68: list.Add(new GameAction(ActionType.SetVar, Flag(0), 0)); break;
            case 69: // refill the lamp
                list.Add(new GameAction(ActionType.SetVar, Var("lighttime"), Math.Max(0, g.LightTime)));
                list.Add(new GameAction(ActionType.MoveItem, ItemId(LightSource), b: Locations.Carried));
                list.Add(new GameAction(ActionType.SetLit, ItemId(LightSource), 1));
                list.Add(new GameAction(ActionType.SetVar, Flag(16), 0));
                break;
            case 70: list.Add(new GameAction(ActionType.ClearScreen)); break;
            case 71: list.Add(new GameAction(ActionType.Save)); break;
            case 72: list.Add(new GameAction(ActionType.SwapItems, ItemId(param()), b: ItemId(param()))); break;
            case 73: continuing.Add(index); break;
            case 74: list.Add(new GameAction(ActionType.MoveItem, ItemId(param()), b: Locations.Carried)); break;
            case 75: // put item 1 with item 2: approximated when item 2 is carried or in a room we can name
            {
                int item = param(), other = param();
                list.Add(new GameAction(ActionType.MoveItem, ItemId(item), b: InitialLocation(other)));
                Skip("PUT item WITH item (approximated: moved to the second item's starting place)");
                break;
            }
            case 77: list.Add(new GameAction(ActionType.AddVar, Var("counter"), -1)); break;
            case 78: list.Add(new GameAction(ActionType.Message, n: 1, text: "{var:counter}")); break;
            case 79: list.Add(new GameAction(ActionType.SetVar, Var("counter"), param())); break;
            case 81: // select counter n: swap the current counter with counter n
            {
                var c = Var($"c{param()}");
                list.Add(new GameAction(ActionType.CopyVar, Var("counter_swap"), b: Var("counter")));
                list.Add(new GameAction(ActionType.CopyVar, "counter", b: c));
                list.Add(new GameAction(ActionType.CopyVar, c, b: "counter_swap"));
                break;
            }
            case 82: list.Add(new GameAction(ActionType.AddVar, Var("counter"), param())); break;
            case 83: list.Add(new GameAction(ActionType.AddVar, Var("counter"), -param())); break;
            case 84: list.Add(new GameAction(ActionType.Message, n: 1, text: "{noun1}")); break;
            case 85: list.Add(GameAction.Say("{noun1}")); break;
            case 86: list.Add(GameAction.Say("")); break;
            case 88: list.Add(new GameAction(ActionType.Pause, n: 2000)); break;
            case 80:
            case 87:
                param();
                Skip("Swapping with a saved room (actions 80/87)");
                break;
            case 89: param(); Skip("SAGA picture (action 89)"); break;
            default: Skip($"Action {op}"); break;
        }
    }

    /// <summary>SCORE: counts the treasures (items marked *) in the treasure room; all of them wins.</summary>
    private void Score(List<GameAction> list)
    {
        if (!a.Triggers.Any(t => t.Id == "scott_score"))
        {
            var score = new Trigger { Id = "scott_score", Name = "Count treasures", Event = TriggerEvent.Subroutine, StopsCommand = false };
            score.Actions.Add(new GameAction(ActionType.SetVar, "@score", 0));
            for (int i = 0; i < g.Items.Count; i++)
            {
                if (!g.Items[i].Text.StartsWith('*')) continue;
                var id = $"scott_treasure{i}";
                a.Triggers.Add(new Trigger
                {
                    Id = id, Name = $"Treasure {i} stored", Event = TriggerEvent.Subroutine, StopsCommand = false,
                    Conditions = { new Condition(ConditionType.ItemIn, ItemId(i), b: RoomId(g.TreasureRoom)) },
                    Actions = { new GameAction(ActionType.AddVar, "@score", 1) },
                });
                score.Actions.Add(new GameAction(ActionType.RunTrigger, id));
            }
            score.Actions.Add(GameAction.Say($"I've stored {{var:@score}} treasures out of {g.Treasures}."));
            score.Actions.Add(new GameAction(ActionType.RunTrigger, "scott_win"));
            a.Triggers.Add(score);
            a.Triggers.Add(new Trigger
            {
                Id = "scott_win", Name = "All treasures stored", Event = TriggerEvent.Subroutine, StopsCommand = false,
                Conditions = { new Condition(ConditionType.VarEquals, "@score", g.Treasures) },
                Actions = { new GameAction(ActionType.Win, text: "Well done. The game is now over.") },
            });
        }
        list.Add(new GameAction(ActionType.RunTrigger, "scott_score"));
    }

    /// <summary>The lamp (item 9) burns down while it exists, warning when low and going out at zero.</summary>
    private void Light()
    {
        if (g.LightTime <= 0 || g.Items.Count <= LightSource) return;
        Var("lighttime");
        a.Triggers.Add(new Trigger
        {
            Id = "scott_light", Name = "The light burns down", Event = TriggerEvent.EveryTurn, StopsCommand = false,
            Conditions = { new Condition(ConditionType.ItemExists, ItemId(LightSource)), new Condition(ConditionType.VarGreater, "lighttime", 0) },
            Actions = { new GameAction(ActionType.AddVar, "lighttime", -1) },
        });
        foreach (var when in new[] { 20, 15, 10, 5 })
            a.Triggers.Add(new Trigger
            {
                Id = $"scott_dim{when}", Name = "The light grows dim", Event = TriggerEvent.EveryTurn, StopsCommand = false,
                Conditions = { new Condition(ConditionType.VarEquals, "lighttime", when), new Condition(ConditionType.ItemPresent, ItemId(LightSource)) },
                Actions = { GameAction.Say("Your light is growing dim.") },
            });
        a.Triggers.Add(new Trigger
        {
            Id = "scott_lightout", Name = "The light runs out", Event = TriggerEvent.EveryTurn, StopsCommand = false,
            Conditions = { new Condition(ConditionType.VarEquals, "lighttime", 0), new Condition(ConditionType.VarEquals, Flag(16), 0), new Condition(ConditionType.ItemExists, ItemId(LightSource)) },
            Actions = { GameAction.Say("Light has run out!"), new GameAction(ActionType.SetLit, ItemId(LightSource), 0), new GameAction(ActionType.SetVar, Flag(16), 1) },
        });
    }

    private void Skip(string what) => skipped[what] = skipped.TryGetValue(what, out var n) ? n + 1 : 1;

    private static string Describe(string v) => v switch
    {
        "f15" => "Darkness: 1 means dark (item 9 is the light source)",
        "f16" => "The light has run out",
        "lighttime" => "Turns of light left in the lamp",
        "counter" => "The current counter",
        "counter_swap" => "Working space for SELECT COUNTER",
        _ when v.StartsWith('f') => "Scott Adams flag",
        _ when v.StartsWith('c') => "Scott Adams counter",
        _ => "",
    };

    private static string Title(string name)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["adv01"] = "Adventureland", ["adv02"] = "Pirate Adventure", ["adv03"] = "Secret Mission", ["adv04"] = "Voodoo Castle",
            ["adv05"] = "The Count", ["adv06"] = "Strange Odyssey", ["adv07"] = "Mystery Fun House", ["adv08"] = "Pyramid of Doom",
            ["adv09"] = "Ghost Town", ["adv10"] = "Savage Island, Part 1", ["adv11"] = "Savage Island, Part 2", ["adv12"] = "The Golden Voyage",
            ["adv13"] = "Sorcerer of Claymorgue Castle", ["adv14a"] = "Return to Pirate's Isle", ["adv14b"] = "Buckaroo Banzai",
        };
        if (known.TryGetValue(name, out var t)) return t;
        var words = name.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "Scott Adams game" : string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
