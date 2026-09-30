using System.Text;
using System.Text.RegularExpressions;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.ScottAdams;

/// <summary>
/// Compiles a game to the Scott Adams .dat format, playable by ScottFree and similar interpreters. Rooms, items,
/// words, messages and the triggers that fit the format (item, room, flag and counter conditions; the classic
/// actions) are written; everything else is reported.
/// </summary>
public sealed class ScottAdamsExporter : IAdventureExporter
{
    public string Id => "scott";
    public string Name => "Scott Adams format (ScottFree)";
    public string Extension(Adventure a) => "dat";

    public string? CannotExport(Adventure a) =>
        a.IsStory ? "Z-code stories can't be converted."
        : a.Rooms.Count == 0 ? "The game has no rooms."
        : a.Rooms.Count > 250 ? "Scott Adams games can have at most 250 rooms."
        : a.Items.Count > 250 ? "Scott Adams games can have at most 250 items." : null;

    public ExportResult Export(Adventure a) => new Compiler(a).Run();

    private sealed class Compiler(Adventure a)
    {
        private static readonly string[] DirectionWords = { "NORTH", "SOUTH", "EAST", "WEST", "UP", "DOWN" };
        private static readonly string[] Directions = { "north", "south", "east", "west", "up", "down" };
        private const int Carried = 255;

        private readonly ExportWarnings warnings = new();
        private readonly Dictionary<string, int> rooms = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> items = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Text, int Location)> itemList = new();
        // Word groups (main word first, then synonyms), laid out into the word list after the actions are compiled.
        private readonly Dictionary<string, List<string>> verbGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> nounGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> verbWords = new();
        private readonly List<string> nounWords = new();
        private readonly Dictionary<string, int> verbNumber = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> nounNumber = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Entry, string? Verb, string? Noun, int Fixed)> vocab = new();
        private readonly List<string> messages = new() { "" };
        private readonly List<int[]> actions = new();
        private readonly List<string> comments = new();
        private readonly Dictionary<string, int> flags = new(StringComparer.OrdinalIgnoreCase);
        private int wordLength;

        public ExportResult Run()
        {
            wordLength = a.Settings.SignificantLetters is >= 3 and <= 8 ? a.Settings.SignificantLetters : 4;
            Rooms();
            Items();
            Actions();
            LayOutWords();

            int lightTime = a.Variables.FirstOrDefault(v => v.Name == "lighttime")?.InitialValue is int lt && lt > 0 ? lt : -1;
            int treasureRoom = TreasureRoom();
            int treasures = itemList.Count(i => i.Text.StartsWith('*'));
            var sb = new StringBuilder();
            void N(int v) => sb.Append(' ').Append(v).Append('\n');
            void S(string s) => sb.Append('"').Append(s.Replace("\"", "`").Replace("\r", "")).Append("\"\n");
            int wordCount = Math.Max(verbWords.Count, nounWords.Count);
            N(0); N(itemList.Count - 1); N(actions.Count - 1); N(wordCount - 1); N(rooms.Count); N(Math.Clamp(a.Settings.MaxCarriedItems, 1, 255));
            N(rooms.GetValueOrDefault(a.StartRoomId, 1)); N(treasures); N(wordLength); N(lightTime); N(messages.Count - 1); N(treasureRoom);
            foreach (var e in actions) foreach (var v in e) N(v);
            for (int i = 0; i < wordCount; i++)
            {
                S(i < verbWords.Count ? verbWords[i] : "");
                S(i < nounWords.Count ? nounWords[i] : "");
            }
            // Room 0 (the store room), then the rooms.
            for (int k = 0; k < 6; k++) N(0);
            S("");
            foreach (var r in a.Rooms)
            {
                var exits = new int[6];
                foreach (var e in r.Exits)
                {
                    int d = Array.IndexOf(Directions, e.Direction.ToLowerInvariant());
                    if (d < 0) { warnings.Add("Exits in directions other than N, S, E, W, U and D were left out"); continue; }
                    if (e.Conditions.Count > 0 || e.DoorItemId != null) warnings.Add("Exits with conditions or doors became plain exits");
                    exits[d] = rooms.GetValueOrDefault(e.TargetRoomId);
                }
                foreach (var x in exits) N(x);
                S(RoomText(r));
            }
            foreach (var m in messages) S(m);
            foreach (var (text, loc) in itemList) { S(text); N(loc); }
            foreach (var c in comments) S(c);
            N(416); N(1); N(0);   // version, adventure number, checksum

            var result = new ExportResult(Encoding.Latin1.GetBytes(sb.ToString()))
            {
                Summary = $"{rooms.Count} rooms, {itemList.Count} items, {actions.Count} actions, {messages.Count - 1} messages, word length {wordLength}",
            };
            warnings.CopyTo(result.Warnings);
            return result;
        }

        // ============================================================= world

        private void Rooms()
        {
            for (int i = 0; i < a.Rooms.Count; i++) rooms[a.Rooms[i].Id] = i + 1;
        }

        private static string RoomText(Room r)
        {
            var d = (r.Description ?? "").Trim();
            if (d.StartsWith("I'm in a ", StringComparison.Ordinal)) return d[9..];
            return "*" + (d.Length > 0 ? d : r.Name);
        }

        private void Items()
        {
            // Item 9 is the light source in the Scott Adams format.
            var list = a.Items.ToList();
            var light = list.FirstOrDefault(i => i.LightSource);
            if (light != null)
            {
                list.Remove(light);
                while (list.Count < 9) list.Add(null!);
                list.Insert(9, light);
            }
            if (a.Items.Count(i => i.LightSource) > 1) warnings.Add("Only one light source is possible; the others became ordinary items");
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) { itemList.Add(("", 0)); continue; }
                items[it.Id] = i;
                var text = it.Name;
                if (it.Portable && !it.Scenery && it.Nouns.FirstOrDefault() is { } noun) text += "/" + Word(noun) + "/";
                itemList.Add((text, Location(it.Location)));
                if (it.Container || it.Supporter) warnings.Add("Containers can't hold things in the Scott Adams format");
            }
        }

        private int Location(string? loc)
        {
            if (string.IsNullOrEmpty(loc)) return 0;
            if (loc is Locations.Carried or Locations.Worn) return Carried;
            if (rooms.TryGetValue(loc, out var r)) return r;
            // Inside another item: put it where that item is.
            if (a.FindItem(loc) is { } container) return Location(container.Location);
            return 0;
        }

        private int TreasureRoom()
        {
            var t = a.Triggers.FirstOrDefault(t => t.Id.StartsWith("scott_treasure", StringComparison.Ordinal));
            var room = t?.Conditions.FirstOrDefault(c => c.Type == ConditionType.ItemIn)?.B;
            return room != null && rooms.TryGetValue(room, out var r) ? r : rooms.GetValueOrDefault(a.StartRoomId, 1);
        }

        // ============================================================= words

        private string Word(string w)
        {
            w = Regex.Replace(w.ToUpperInvariant(), "[^A-Z0-9.]", "");
            return w.Length > wordLength ? w[..wordLength] : w;
        }

        /// <summary>Registers a verb pattern ("go|ent|cli", "polish", "take") and returns its group key.</summary>
        private string VerbKey(string pattern)
        {
            var words = pattern.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            // A single command id brings its words; a list of words ("say|spe|cal") is already the exact verb.
            if (words.Count == 1 && a.Vocabulary.Verbs.FirstOrDefault(v => v.Id == words[0]) is { } def) words.AddRange(def.Words.Where(w => !w.Contains(' ')));
            string key = words.Contains("go") ? "go" : words.Any(w => w is "take" or "get") ? "get" : words.Contains("drop") ? "drop"
                // Patterns sharing a word are the same verb.
                : verbGroups.FirstOrDefault(kv => words.Select(Word).Any(w => kv.Value.Contains(w))).Key ?? Word(words[0]);
            if (!verbGroups.TryGetValue(key, out var group)) verbGroups[key] = group = new List<string>();
            if (group.Count == 0 && key is "go" or "get" or "drop") group.Add(key == "drop" ? Word("drop") : key.ToUpperInvariant());
            foreach (var w in words.Select(Word).Where(w => w.Length > 0))
                if (!group.Contains(w) && !verbGroups.Values.Any(g => g != group && g.Contains(w))) group.Add(w);
            return key;
        }

        private string? NounKey(string? pattern)
        {
            if (pattern is null or "*" or "" or "-") return null;
            var words = pattern.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (a.FindItem(words[0]) is { } item) words = item.Nouns.Count > 0 ? item.Nouns.ToList() : new() { item.Name };
            if (Array.IndexOf(Directions, words[0].ToLowerInvariant()) >= 0) return words[0].ToLowerInvariant();
            var key = nounGroups.FirstOrDefault(kv => words.Select(Word).Any(w => kv.Value.Contains(w))).Key ?? Word(words[0]);
            if (!nounGroups.TryGetValue(key, out var group)) nounGroups[key] = group = new List<string>();
            foreach (var w in words.Select(Word).Where(w => w.Length > 0))
                if (!group.Contains(w) && !nounGroups.Values.Any(g => g != group && g.Contains(w))) group.Add(w);
            return key;
        }

        /// <summary>
        /// Lays out the word list: synonyms follow their main word (marked *), GO is verb 1, GET verb 10 and DROP verb
        /// 18 (ScottFree's built-in verbs), and nouns 1–6 are the directions.
        /// </summary>
        private void LayOutWords()
        {
            foreach (var (text, _) in itemList)
                if (Regex.Match(text, @"/([A-Z0-9.]+)/$") is { Success: true } m) NounKey(m.Groups[1].Value.ToLowerInvariant());
            VerbKey("go"); VerbKey("take"); VerbKey("drop");

            void Place(List<string> list, Dictionary<string, int> numbers, string key, List<string> group)
            {
                numbers[key] = list.Count;
                list.Add(group[0]);
                foreach (var w in group.Skip(1)) list.Add("*" + w);
            }
            verbWords.Add("AUT");
            Place(verbWords, verbNumber, "go", verbGroups["go"]);
            var others = verbGroups.Where(kv => kv.Key is not ("go" or "get" or "drop") && kv.Value.Count > 0).ToList();
            void FillUntil(int index)
            {
                foreach (var kv in others.ToList())
                {
                    if (verbWords.Count + kv.Value.Count > index) continue;
                    Place(verbWords, verbNumber, kv.Key, kv.Value);
                    others.Remove(kv);
                }
                while (verbWords.Count < index) verbWords.Add("");
            }
            FillUntil(10);
            Place(verbWords, verbNumber, "get", verbGroups["get"]);
            FillUntil(18);
            Place(verbWords, verbNumber, "drop", verbGroups["drop"]);
            foreach (var kv in others) Place(verbWords, verbNumber, kv.Key, kv.Value);

            nounWords.Add("ANY");
            for (int d = 0; d < 6; d++) { nounNumber[Directions[d]] = nounWords.Count; nounWords.Add(Word(DirectionWords[d])); }
            foreach (var (key, group) in nounGroups) if (group.Count > 0) Place(nounWords, nounNumber, key, group);

            foreach (var (entry, verb, noun, fixedValue) in vocab)
                actions[entry][0] = verb == null ? fixedValue : verbNumber[verb] * 150 + (noun == null ? 0 : nounNumber.GetValueOrDefault(noun));
            if (verbWords.Count > 150 || nounWords.Count > 150) warnings.Add("More than 150 words: the format can't number them all");
        }

        // ============================================================= actions

        private sealed class Entry
        {
            public List<(int Code, int Arg)> Conditions = new();
            public List<int> Params = new();
            public List<int> Ops = new();
            public string Comment = "";
            public List<Trigger> Continuations = new();
        }

        private static bool IsHelper(Trigger t) =>
            t.Id.StartsWith("scott_", StringComparison.Ordinal) || t.Id.EndsWith("_sub", StringComparison.Ordinal);

        private void Actions()
        {
            // Subroutines that are only run from other triggers become continuation entries after them.
            var called = a.Triggers.SelectMany(t => t.Actions).Where(x => x.Type == ActionType.RunTrigger).Select(x => x.A).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in a.Triggers)
            {
                if (IsHelper(t) || !t.Enabled) continue;
                if (t.Event == TriggerEvent.Subroutine) { if (!called.Contains(t.Id)) warnings.Add("Subroutine triggers that nothing runs were left out"); continue; }
                (string? Verb, string? Noun, int Fixed)? v;
                if (t.Event is TriggerEvent.BeforeCommand or TriggerEvent.AfterCommand) v = CommandVocab(t);
                else if (t.Event == TriggerEvent.EveryTurn)
                    v = (null, null, t.Conditions.FirstOrDefault(c => c.Type == ConditionType.Chance) is { } ch ? Math.Clamp(ch.N, 1, 100) : 100);
                else { warnings.Add($"{t.Event} triggers have no Scott Adams equivalent"); continue; }
                if (v == null) continue;
                Emit(t, v.Value, new HashSet<string>());
            }
            if (actions.Count == 0) actions.Add(new int[8]);
        }

        private (string? Verb, string? Noun, int Fixed)? CommandVocab(Trigger t)
        {
            if (string.IsNullOrEmpty(t.Verb) || t.Verb == "*") { warnings.Add("Command triggers without a verb were left out"); return null; }
            int d = Array.IndexOf(Directions, t.Verb.ToLowerInvariant());
            if (d >= 0) return ("go", Directions[d], 0);   // GO + direction
            return (VerbKey(t.Verb), NounKey(t.Noun1), 0);
        }

        /// <summary>Writes a trigger as one entry, or several joined with CONTINUE when it doesn't fit.</summary>
        private void Emit(Trigger t, (string? Verb, string? Noun, int Fixed) key, HashSet<string> visiting)
        {
            if (!visiting.Add(t.Id)) return;
            var e = new Entry { Comment = t.Name };
            foreach (var c in t.Conditions)
            {
                if (c.Type == ConditionType.Chance && key.Verb == null) continue;
                if (Condition(c) is not { } code) { warnings.Add($"Triggers with {c.Type} conditions were left out"); visiting.Remove(t.Id); return; }
                e.Conditions.Add(code);
            }
            var calls = new List<Trigger>();
            for (int i = 0; i < t.Actions.Count; i++) Action(t.Actions, ref i, e, calls);

            // Split into entries of at most 5 condition/parameter slots and 4 actions.
            var ops = e.Ops;
            var parameters = new Queue<int>(e.Params);
            bool first = true;
            int opIndex = 0;
            do
            {
                var entry = new int[8];
                if (first) vocab.Add((actions.Count, key.Verb, key.Noun, key.Fixed));
                int slot = 1;
                if (first)
                    foreach (var (code, arg) in e.Conditions.Take(5))
                        entry[slot++] = arg * 20 + code;
                bool more = false;
                var these = new List<int>();
                while (opIndex < ops.Count)
                {
                    int need = ParamCount(ops[opIndex]);
                    bool last = opIndex == ops.Count - 1 && calls.Count == 0;
                    int room = 4 - these.Count - (last ? 0 : 1);   // keep a slot for CONTINUE
                    if (room <= 0 || slot + need > 6) { more = true; break; }
                    for (int k = 0; k < need; k++) entry[slot++] = parameters.Count > 0 ? parameters.Dequeue() * 20 : 0;
                    these.Add(ops[opIndex++]);
                }
                if (more || (opIndex >= ops.Count && calls.Count > 0)) these.Add(73);
                while (these.Count < 4) these.Add(0);
                entry[6] = these[0] * 150 + these[1];
                entry[7] = these[2] * 150 + these[3];
                if (first && e.Conditions.Count > 5) warnings.Add("Triggers with more than five conditions lost the extra ones");
                actions.Add(entry);
                comments.Add(first ? e.Comment : "");
                first = false;
                if (!more) break;
            } while (true);

            // Subroutines run by this trigger follow as continuation entries.
            foreach (var sub in calls) Emit(sub, (null, null, 0), visiting);
            visiting.Remove(t.Id);
        }

        private static int ParamCount(int op) => op switch
        {
            52 or 53 or 54 or 55 or 58 or 59 or 60 or 74 or 79 or 81 or 82 or 83 => 1,
            62 or 72 or 75 => 2,
            _ => 0,
        };

        private (int Code, int Arg)? Condition(Condition c)
        {
            int Item(string? id) => id != null && items.TryGetValue(id, out var n) ? n : -1;
            switch (c.Type)
            {
                case ConditionType.ItemCarried when Item(c.A) >= 0: return (c.Negate ? 6 : 1, Item(c.A));
                case ConditionType.ItemPresent when Item(c.A) >= 0: return (c.Negate ? 12 : 3, Item(c.A));
                case ConditionType.ItemExists when Item(c.A) >= 0: return (c.Negate ? 14 : 13, Item(c.A));
                case ConditionType.ItemIn when Item(c.A) >= 0 && c.B == Locations.Here: return (c.Negate ? 5 : 2, Item(c.A));
                case ConditionType.ItemIn when Item(c.A) >= 0 && c.B is { } where && Location(where) == InitialLocation(c.A!): return (c.Negate ? 18 : 17, Item(c.A));
                case ConditionType.ItemIn when Item(c.A) >= 0 && c.B == Locations.Carried: return (c.Negate ? 6 : 1, Item(c.A));
                case ConditionType.PlayerIn when RoomNumber(c.A) is { } r: return (c.Negate ? 7 : 4, r);
                case ConditionType.VarGreater when c.A == "@carried" && c.N == 0: return (c.Negate ? 11 : 10, 0);
                case ConditionType.VarEquals when c.A == "@carried" && c.N == 0: return (c.Negate ? 10 : 11, 0);
                case ConditionType.VarLess when c.A == "counter" && !c.Negate: return (15, c.N - 1);
                case ConditionType.VarGreater when c.A == "counter" && !c.Negate: return (16, c.N);
                case ConditionType.VarEquals when c.A == "counter" && !c.Negate: return (19, c.N);
                case ConditionType.VarEquals when c.A != null && Flag(c.A) is { } f && c.N is 0 or 1:
                    bool set = (c.N == 1) != c.Negate;
                    return (set ? 8 : 9, f);
                default: return null;
            }
        }

        /// <summary>A room's number; "r0" (the store room, not a real room) is 0.</summary>
        private int? RoomNumber(string? id) => id == null ? null : rooms.TryGetValue(id, out var r) ? r : id == "r0" ? 0 : null;

        private int InitialLocation(string itemId) => a.FindItem(itemId) is { } it ? Location(it.Location) : -1;

        /// <summary>A flag number for a variable used as an on/off flag (f0–f31 keep their numbers).</summary>
        private int? Flag(string variable)
        {
            if (flags.TryGetValue(variable, out var f)) return f;
            if (Regex.Match(variable, @"^f(\d+)$") is { Success: true } m && int.Parse(m.Groups[1].Value) < 32) return flags[variable] = int.Parse(m.Groups[1].Value);
            for (int n = 0; n < 32; n++)
                if (n is not 15 and not 16 && !flags.ContainsValue(n) && !a.Variables.Any(v => v.Name == $"f{n}")) return flags[variable] = n;
            return null;
        }

        private int Message(string text)
        {
            text = Regex.Replace(text, @"\{[^}]*\}", "");
            int i = messages.IndexOf(text);
            if (i > 0) return i;
            if (messages.Count >= 100) { warnings.Add("More than 99 messages: the extra ones were left out"); return -1; }
            messages.Add(text);
            return messages.Count - 1;
        }

        private void Action(List<GameAction> list, ref int i, Entry e, List<Trigger> calls)
        {
            var x = list[i];
            int Item(string? id) => id != null && items.TryGetValue(id, out var n) ? n : -1;
            void Op(int op, params int[] ps) { e.Ops.Add(op); e.Params.AddRange(ps); }
            switch (x.Type)
            {
                case ActionType.Message when x.Text == "{var:counter}": Op(78); break;
                case ActionType.Message when x.Text == "{noun1}": Op(x.N == 1 ? 84 : 85); break;
                case ActionType.Message when string.IsNullOrEmpty(x.Text): Op(86); break;
                case ActionType.Message:
                {
                    int m = Message(x.Text!);
                    if (m > 0) Op(m <= 51 ? m : m + 50);
                    break;
                }
                case ActionType.MoveItem when Item(x.A) >= 0:
                    if (x.B == Locations.Carried) Op(74, Item(x.A));
                    else if (x.B == Locations.Here) Op(53, Item(x.A));
                    else if (string.IsNullOrEmpty(x.B)) Op(59, Item(x.A));
                    else Op(62, Item(x.A), Location(x.B));
                    break;
                case ActionType.TakeItem when Item(x.A) >= 0: Op(52, Item(x.A)); break;
                case ActionType.DropItem when Item(x.A) >= 0: Op(53, Item(x.A)); break;
                case ActionType.DestroyItem when Item(x.A) >= 0: Op(59, Item(x.A)); break;
                case ActionType.GoTo when RoomNumber(x.A) is { } room: Op(54, room); break;
                case ActionType.SwapItems when Item(x.A) >= 0 && Item(x.B) >= 0: Op(72, Item(x.A), Item(x.B)); break;
                case ActionType.SetVar when x.A == "f15": Op(x.N != 0 ? 56 : 57); break;
                case ActionType.SetVar when x.A == "counter": Op(79, x.N); break;
                case ActionType.SetVar when x.A == "lighttime":
                    // The importer's lamp refill: SetVar lighttime, MoveItem lamp, SetLit, SetVar f16 0.
                    Op(69);
                    while (i + 1 < list.Count && list[i + 1].Type is ActionType.MoveItem or ActionType.SetLit || (i + 1 < list.Count && list[i + 1].A == "f16")) i++;
                    break;
                case ActionType.SetVar when x.A != null && Flag(x.A) is { } f && x.N is 0 or 1: Op(x.N == 1 ? 58 : 60, f); break;
                case ActionType.AddVar when x.A == "counter": Op(x.N >= 0 ? 82 : 83, Math.Abs(x.N)); break;
                case ActionType.CopyVar when x.A == "counter_swap" && i + 2 < list.Count && list[i + 1].B is { } c && Regex.Match(c, @"^c(\d+)$") is { Success: true } cm:
                    Op(81, int.Parse(cm.Groups[1].Value));
                    i += 2;
                    break;
                case ActionType.Look: Op(64); break;
                case ActionType.ShowScore: Op(65); break;
                case ActionType.RunTrigger when x.A == "scott_score": Op(65); break;
                case ActionType.Inventory: Op(66); break;
                case ActionType.ClearScreen: Op(70); break;
                case ActionType.Save: Op(71); break;
                case ActionType.Pause: Op(88); break;
                case ActionType.Win:
                case ActionType.Lose:
                case ActionType.Quit:
                    if (!string.IsNullOrWhiteSpace(x.Text) && Message(x.Text!) is > 0 and var wm) Op(wm <= 51 ? wm : wm + 50);
                    Op(63);
                    if (x.Type == ActionType.Win) warnings.Add("Winning became game over (Scott Adams games are won by storing treasures)");
                    break;
                case ActionType.RunTrigger when a.Triggers.FirstOrDefault(t => t.Id == x.A) is { } sub:
                    calls.Add(sub);
                    break;
                case ActionType.Done:
                case ActionType.Ok:
                    break;
                default:
                    warnings.Add($"{x.Type} actions have no Scott Adams equivalent");
                    break;
            }
        }
    }
}
