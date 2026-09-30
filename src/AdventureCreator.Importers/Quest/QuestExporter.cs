using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Parsing;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Quest;

/// <summary>
/// Writes a game as a Quest 5 game: an .aslx file, or a .quest package when it has pictures or sounds. Rooms,
/// objects, exits, verbs, commands, turn scripts, timers and room scripts are written, with triggers as Quest
/// scripts (if blocks for their conditions).
/// </summary>
public sealed class QuestExporter : IAdventureExporter
{
    public string Id => "quest";
    public string Name => "Quest 5";

    public string Extension(Adventure a) =>
        a.Pictures.Any(p => p.BitmapAsset != null) || a.Sounds.Count > 0 || a.Origin?.Format.Contains(".quest") == true ? "quest" : "aslx";

    public string? CannotExport(Adventure a) => a.IsStory ? "Z-code stories can't be converted." : a.Rooms.Count == 0 ? "The game has no rooms." : null;

    public ExportResult Export(Adventure a)
    {
        var w = new Writer(a);
        var xml = w.Run(out var files);
        byte[] data;
        if (Extension(a) == "quest")
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var s = zip.CreateEntry("game.aslx").Open()) s.Write(Encoding.UTF8.GetBytes(xml));
                foreach (var (name, bytes) in files)
                    using (var s = zip.CreateEntry(name).Open()) s.Write(bytes);
            }
            data = ms.ToArray();
        }
        else data = Encoding.UTF8.GetBytes(xml);
        var result = new ExportResult(data) { Summary = $"{a.Rooms.Count} rooms, {a.Items.Count} objects, {w.Scripts} scripts{(files.Count > 0 ? $", {files.Count} files" : "")}" };
        w.Warnings.CopyTo(result.Warnings);
        return result;
    }

    private sealed class Writer(Adventure a)
    {
        public readonly ExportWarnings Warnings = new();
        public int Scripts;
        private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> files = new();
        private readonly Dictionary<string, string> lockExits = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> handled = new();
        private static readonly HashSet<string> BuiltInVerbIds = BuiltInLexicon.VerbTable.Select(r => r.Split('|')[0].Trim().TrimEnd('!')).ToHashSet();

        private string Name(string id) => names.TryGetValue(id, out var n) ? n : id;

        public string Run(out Dictionary<string, byte[]> resources)
        {
            foreach (var r in a.Rooms) names[r.Id] = Safe(r.Id);
            foreach (var i in a.Items) names[i.Id] = Safe(i.Id);
            // Locked exits (the importer's lock variables): named so scripts can unlock them.
            foreach (var r in a.Rooms)
                foreach (var e in r.Exits.Where(e => e.Conditions.Count == 1 && e.Conditions[0] is { Type: ConditionType.VarEquals, N: 0, Negate: false }))
                    lockExits[e.Conditions[0].A!] = $"{Name(r.Id)}_{e.Direction}";

            var sb = new StringBuilder();
            sb.Append("<asl version=\"550\">\n  <include ref=\"English.aslx\" />\n  <include ref=\"Core.aslx\" />\n");
            if (Gamebook) return GamebookRun(out resources);
            var start = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(a.Introduction)) start.Append(Msg(a.Introduction, null)).Append('\n');
            foreach (var t in a.Triggers.Where(t => t.Event == TriggerEvent.GameStart)) { start.Append(Script(t, null)); handled.Add(t.Id); }
            var body = new StringBuilder();
            foreach (var r in a.Rooms) Room(body, r);
            Verbs(body);
            Commands(body);
            Other(body);
            Game(sb, start.ToString());
            sb.Append(body).Append("</asl>\n");
            resources = files;
            return sb.ToString();
        }

        /// <summary>A choice-based story (imported from a gamebook, or every exit a numbered choice): written as a gamebook.</summary>
        private bool Gamebook => a.Origin?.Format.Contains("gamebook", StringComparison.OrdinalIgnoreCase) == true
            || a.Items.Count == 0 && a.Rooms.SelectMany(r => r.Exits).Any() && a.Rooms.SelectMany(r => r.Exits).All(e => e.Direction.StartsWith("choice"));

        private string GamebookRun(out Dictionary<string, byte[]> resources)
        {
            var sb = new StringBuilder("<asl version=\"550\">\n  <include ref=\"GamebookCore.aslx\" />\n");
            var body = new StringBuilder();
            foreach (var r in a.Rooms.OrderBy(r => r.Id == a.StartRoomId ? 0 : 1))
            {
                var (text, choices) = SplitChoices(r);
                var script = new StringBuilder();
                foreach (var t in a.Triggers.Where(t => t.RoomId == r.Id && t.Event is TriggerEvent.EnterRoom or TriggerEvent.BeforeEnterRoom or TriggerEvent.AfterDescribe))
                {
                    script.Append(Script(t, null, once: t.OnceOnly));
                    handled.Add(t.Id);
                }
                body.Append($"  <object name=\"{Name(r.Id)}\">\n");
                if (r.Id == a.StartRoomId) body.Append("    <object name=\"player\">\n      <inherit name=\"defaultplayer\" />\n    </object>\n");
                if (r.Name != Name(r.Id)) body.Append($"    <alias>{X(r.Name)}</alias>\n");
                if (script.Length > 0) body.Append("    <inherit name=\"scripttext\" />\n    <script type=\"script\">\n").Append(Block(script.ToString(), 6)).Append("    </script>\n");
                body.Append($"    <description>{X(text)}</description>\n");
                if (r.PictureId != null && File(r.PictureId) is { } pic) body.Append($"    <picture>{X(pic)}</picture>\n");
                if (choices.Count > 0)
                    body.Append("    <options type=\"stringdictionary\">\n")
                        .Append(string.Concat(choices.Select(c => $"      <item>\n        <key>{Name(c.Target)}</key>\n        <value>{X(c.Text)}</value>\n      </item>\n")))
                        .Append("    </options>\n");
                body.Append("  </object>\n");
            }
            foreach (var t in a.Triggers.Where(t => !handled.Contains(t.Id) && t.Event != TriggerEvent.Subroutine)) Warnings.Add($"{t.Event} triggers have no gamebook equivalent");
            var start = new StringBuilder();
            foreach (var t in a.Triggers.Where(t => t.Event == TriggerEvent.GameStart)) start.Append(Script(t, null));
            Game(sb, start.ToString());
            resources = files;
            return sb.Append(body).Append("</asl>\n").ToString();
        }

        /// <summary>A page's text without its "N. choice" lines, and the choices (target and text) in order.</summary>
        private (string Text, List<(string Target, string Text)> Choices) SplitChoices(Room r)
        {
            var choices = new List<(string, string)>();
            var lines = r.Description.Split('\n').ToList();
            foreach (var e in r.Exits.Where(e => e.Direction.StartsWith("choice")))
            {
                var n = e.Direction["choice".Length..];
                var i = lines.FindIndex(l => l.StartsWith(n + ". "));
                choices.Add((e.TargetRoomId, i >= 0 ? lines[i][(n.Length + 2)..] : a.FindRoom(e.TargetRoomId)?.Name ?? e.TargetRoomId));
                if (i >= 0) lines.RemoveAt(i);
            }
            foreach (var e in r.Exits.Where(e => !e.Direction.StartsWith("choice")))
                choices.Add((e.TargetRoomId, e.Direction));
            return (string.Join("\n", lines).TrimEnd(), choices);
        }

        private static string Safe(string id)
        {
            var s = Regex.Replace(id, @"[^A-Za-z0-9_]", "_");
            return char.IsDigit(s[0]) ? "o" + s : s;
        }

        private static string X(string s) => SecurityElement.Escape(s) ?? "";

        // ============================================================= game

        private void Game(StringBuilder sb, string start)
        {
            sb.Append($"  <game name=\"{X(a.Title)}\">\n");
            sb.Append($"    <gameid>{Guid.NewGuid()}</gameid>\n    <version>1.0</version>\n");
            if (a.Author.Length > 0) sb.Append($"    <author>{X(a.Author)}</author>\n");
            if (a.Description.Length > 0) sb.Append($"    <description>{X(a.Description)}</description>\n");
            if (Colour(a.Settings.TextColor) is { } fg) sb.Append($"    <defaultforeground>{fg}</defaultforeground>\n");
            if (Colour(a.Settings.BackgroundColor) is { } bg) sb.Append($"    <defaultbackground>{bg}</defaultbackground>\n");
            foreach (var v in a.Variables.Where(v => !lockExits.ContainsKey(v.Name) && !guards.Contains(v.Name) && Owner(v.Name).Owner == "game"))
                sb.Append($"    <attr name=\"{Owner(v.Name).Attribute}\" type=\"int\">{v.InitialValue}</attr>\n");
            if (start.Length > 0) sb.Append("    <start type=\"script\">\n").Append(Block(start, 6)).Append("    </start>\n");
            sb.Append("  </game>\n");
        }

        private static string? Colour(string? c) => c is { Length: 7 } && c[0] == '#' ? c : null;

        // ============================================================= rooms and objects

        private void Room(StringBuilder sb, Room r)
        {
            sb.Append($"  <object name=\"{Name(r.Id)}\">\n    <inherit name=\"editor_room\" />\n");
            if (r.Name.Length > 0 && r.Name != Name(r.Id)) sb.Append($"    <alias>{X(r.Name)}</alias>\n");
            var describe = a.Triggers.Where(t => t.RoomId == r.Id && t.Event == TriggerEvent.AfterDescribe).ToList();
            if (describe.Count == 0) sb.Append($"    <description>{X(Text(r.Description))}</description>\n");
            else
            {
                var script = new StringBuilder();
                if (r.Description.Length > 0) script.Append(Msg(r.Description, null)).Append('\n');
                foreach (var t in describe) { script.Append(Script(t, null)); handled.Add(t.Id); }
                sb.Append("    <description type=\"script\">\n").Append(Block(script.ToString(), 6)).Append("    </description>\n");
            }
            if (r.IsDark) sb.Append("    <dark />\n");
            if (r.PictureId != null && File(r.PictureId) is { } pic) sb.Append($"    <picture>{X(pic)}</picture>\n");
            foreach (var (ev, once, element) in new[] { (TriggerEvent.BeforeEnterRoom, true, "beforefirstenter"), (TriggerEvent.EnterRoom, true, "firstenter"), (TriggerEvent.EnterRoom, false, "enter"), (TriggerEvent.BeforeEnterRoom, false, "beforeenter"), (TriggerEvent.LeaveRoom, false, "onexit") })
            {
                var list = a.Triggers.Where(t => t.RoomId == r.Id && t.Event == ev && (t.OnceOnly == once || ev == TriggerEvent.LeaveRoom)).ToList();
                if (list.Count == 0) continue;
                var script = new StringBuilder();
                foreach (var t in list) { script.Append(Script(t, null, once: t.OnceOnly && !once)); handled.Add(t.Id); }
                sb.Append($"    <{element} type=\"script\">\n").Append(Block(script.ToString(), 6)).Append($"    </{element}>\n");
            }
            if (r.Id == a.StartRoomId)
            {
                sb.Append("    <object name=\"player\">\n      <inherit name=\"editor_object\" />\n      <inherit name=\"editor_player\" />\n");
                foreach (var it in a.Items.Where(i => i.Location is Locations.Carried or Locations.Worn)) Item(sb, it, 3);
                sb.Append("    </object>\n");
            }
            foreach (var e in r.Exits) Exit(sb, r, e);
            foreach (var it in a.Items.Where(i => string.Equals(i.Location, r.Id, StringComparison.OrdinalIgnoreCase))) Item(sb, it, 2);
            sb.Append("  </object>\n");
        }

        private static readonly HashSet<string> QuestDirections = new() { "north", "south", "east", "west", "northeast", "northwest", "southeast", "southwest", "up", "down", "in", "out" };

        private void Exit(StringBuilder sb, Room r, Exit e)
        {
            if (a.FindRoom(e.TargetRoomId) == null) return;
            bool locked = e.Conditions.Count == 1 && lockExits.ContainsKey(e.Conditions[0].A ?? "");
            var name = locked ? $" name=\"{lockExits[e.Conditions[0].A!]}\"" : "";
            sb.Append($"    <exit alias=\"{X(e.Direction)}\" to=\"{Name(e.TargetRoomId)}\"{name}>\n");
            if (QuestDirections.Contains(e.Direction)) sb.Append($"      <inherit name=\"{e.Direction}direction\" />\n");
            if (locked)
            {
                var start = a.Variables.FirstOrDefault(v => v.Name == e.Conditions[0].A)?.InitialValue ?? 0;
                if (start != 0) sb.Append("      <locked />\n");
                if (e.BlockedMessage.Length > 0) sb.Append($"      <lockmessage>{X(Text(e.BlockedMessage))}</lockmessage>\n");
            }
            else if (e.Conditions.Count > 0) Warnings.Add("Exits with conditions other than a lock became plain exits");
            if (e.TravelMessage.Length > 0) sb.Append($"      <message>{X(Text(e.TravelMessage))}</message>\n");
            sb.Append("    </exit>\n");
        }

        private void Item(StringBuilder sb, Item it, int depth)
        {
            var pad = new string(' ', depth * 2);
            sb.Append($"{pad}<object name=\"{Name(it.Id)}\">\n{pad}  <inherit name=\"editor_object\" />\n");
            if (it.Container) sb.Append($"{pad}  <inherit name=\"{(it.Openable ? (it.IsOpen ? "container_open" : "container_closed") : "container_open")}\" />\n");
            if (it.Supporter) sb.Append($"{pad}  <inherit name=\"surface\" />\n");
            if (it.Wearable) sb.Append($"{pad}  <inherit name=\"wearable\" />\n");
            if (it.IsCharacter) sb.Append($"{pad}  <inherit name=\"namedmale\" />\n");
            if (it.Name != Name(it.Id)) sb.Append($"{pad}  <alias>{X(it.Name)}</alias>\n");
            if (it.Description.Length > 0 && !a.Triggers.Any(t => t.Noun1 == it.Id && t.Verb == "examine" && t.Event == TriggerEvent.BeforeCommand))
                sb.Append($"{pad}  <look>{X(Text(it.Description))}</look>\n");
            if (it.Portable && !it.Scenery && !it.IsCharacter) sb.Append($"{pad}  <take />\n");
            if (it.Scenery) sb.Append($"{pad}  <scenery />\n");
            if (it.Location == Locations.Worn) sb.Append($"{pad}  <worn />\n");
            if (it.Edible) sb.Append($"{pad}  <edible />\n");
            if (it.LightSource) sb.Append($"{pad}  <lightsource />\n");
            if (it.Switchable) sb.Append($"{pad}  <feature_switchable />\n{pad}  <switchedon type=\"boolean\">{(it.IsLit ? "true" : "false")}</switchedon>\n");
            if (it.Readable && it.ReadText.Length > 0) sb.Append($"{pad}  <read>{X(Text(it.ReadText))}</read>\n");
            var alt = it.Nouns.Where(n => !it.Name.Contains(n, StringComparison.OrdinalIgnoreCase)).ToList();
            if (alt.Count > 0) sb.Append($"{pad}  <alt type=\"stringlist\">\n").Append(string.Concat(alt.Select(n => $"{pad}    <value>{X(n)}</value>\n"))).Append($"{pad}  </alt>\n");
            foreach (var (verb, text) in it.VerbResponses) sb.Append($"{pad}  <{VerbProperty(verb)}>{X(Text(text))}</{VerbProperty(verb)}>\n");
            foreach (var v in a.Variables.Where(v => !guards.Contains(v.Name) && Owner(v.Name).Owner == Name(it.Id)))
                sb.Append($"{pad}  <attr name=\"{Owner(v.Name).Attribute}\" type=\"int\">{v.InitialValue}</attr>\n");
            // The item's own verb triggers become its verb scripts.
            foreach (var group in a.Triggers.Where(t => t.Event == TriggerEvent.BeforeCommand && t.Noun1 == it.Id && t.Noun2 is null or "-" && t.RoomId == null && SingleVerb(t.Verb) != null)
                         .GroupBy(t => SingleVerb(t.Verb)!))
            {
                var script = Chain(group.ToList(), "this");
                foreach (var t in group) handled.Add(t.Id);
                sb.Append($"{pad}  <{VerbProperty(group.Key)} type=\"script\">\n").Append(Block(script, depth * 2 + 4)).Append($"{pad}  </{VerbProperty(group.Key)}>\n");
            }
            foreach (var inner in a.Items.Where(i => string.Equals(i.Location, it.Id, StringComparison.OrdinalIgnoreCase))) Item(sb, inner, depth + 1);
            sb.Append($"{pad}</object>\n");
        }

        private static string? SingleVerb(string? verb) => string.IsNullOrEmpty(verb) || verb.Contains('|') || verb == "*" ? null : verb;

        /// <summary>Quest's property name for a verb (its "look" is our "examine", its "speak" our "talk").</summary>
        private static string VerbProperty(string verb) => verb switch { "examine" => "look", "talk" => "speak", _ => Safe(verb) };

        // ============================================================= verbs and commands

        private void Verbs(StringBuilder sb)
        {
            foreach (var v in a.Vocabulary.Verbs.Where(v => !BuiltInVerbIds.Contains(v.Id)))
            {
                var words = v.Words.Count > 0 ? v.Words : new List<string> { v.Id };
                sb.Append($"  <verb>\n    <property>{VerbProperty(v.Id)}</property>\n    <pattern>{X(string.Join("; ", words))}</pattern>\n");
                if (!string.IsNullOrWhiteSpace(v.DefaultResponse)) sb.Append($"    <defaulttext>{X(Text(v.DefaultResponse))}</defaulttext>\n");
                sb.Append("  </verb>\n");
            }
        }

        private void Commands(StringBuilder sb)
        {
            var commands = a.Triggers.Where(t => t.Event == TriggerEvent.BeforeCommand && !handled.Contains(t.Id)).GroupBy(t => (t.Verb ?? "", t.RoomId ?? "", HasNoun(t), HasNoun2(t)));
            foreach (var group in commands)
            {
                var (verb, room, withNoun, withNoun2) = group.Key;
                if (verb is "" or "*") { Warnings.Add("Triggers for any command have no Quest equivalent"); continue; }
                var patterns = new List<string>();
                foreach (var v in verb.Split('|'))
                {
                    var def = a.Vocabulary.Verbs.FirstOrDefault(d => d.Id == v);
                    var words = def?.Words is { Count: > 0 } w ? w : new List<string> { v };
                    var grammars = def?.Grammar.Where(g => g.Contains("{noun2}") == withNoun2 && (g.Contains("{noun}") || g.Contains("{noun2}")) == withNoun).ToList() ?? new();
                    if (grammars.Count == 0) grammars.Add(withNoun2 ? "* {noun} on {noun2}" : withNoun ? "* {noun}" : "*");
                    foreach (var word in words)
                        foreach (var g in grammars)
                            patterns.Add((word + " " + g.TrimStart('*').Trim()
                                .Replace("{noun}", withNoun2 ? "#object1#" : "#object#").Replace("{noun2}", "#object2#").Replace("{text}", "#text#")).Trim());
                }
                hasNoun2 = withNoun2;
                var script = Chain(group.ToList(), "object");
                hasNoun2 = false;
                var pattern = string.Join("; ", patterns.Distinct());
                foreach (var t in group) handled.Add(t.Id);
                sb.Append($"  <command name=\"cmd_{Safe(verb.Split('|')[0])}{(room.Length > 0 ? "_" + Safe(room) : "")}{(withNoun ? "_obj" : "")}{(withNoun2 ? "2" : "")}\">\n");
                sb.Append($"    <pattern>{X(pattern)}</pattern>\n    <script>\n").Append(Block(script, 6)).Append("    </script>\n  </command>\n");
                if (room.Length > 0) Warnings.Add("Commands limited to one room check the player's room in their script");
            }
        }

        private static bool HasNoun(Trigger t) => !string.IsNullOrEmpty(t.Noun1) && t.Noun1 != "-";
        /// <summary>Whether the command names a second object: explicitly, or (any second object) through its verb's grammar.</summary>
        private bool HasNoun2(Trigger t) => t.Noun2 is { Length: > 0 } and not "-"
            || t.Noun2 == null && t.Verb != null && t.Verb.Split('|').Any(v => a.Vocabulary.Verbs.FirstOrDefault(d => d.Id == v)?.Grammar.Any(g => g.Contains("{noun2}")) == true);

        private bool hasNoun2;   // writing a command with two objects: the first is object1

        private void Other(StringBuilder sb)
        {
            foreach (var t in a.Triggers.Where(t => !handled.Contains(t.Id)))
            {
                switch (t.Event)
                {
                    case TriggerEvent.EveryTurn:
                        sb.Append($"  <turnscript name=\"{Safe(t.Id)}\">\n    {(t.Enabled ? "<enabled />" : "")}\n    <script>\n").Append(Block(Script(t, null), 6)).Append("    </script>\n  </turnscript>\n");
                        break;
                    case TriggerEvent.Timer:
                        sb.Append($"  <timer name=\"{Safe(t.Id)}\">\n    <interval>{Math.Max(1, t.Interval)}</interval>\n    {(t.Enabled ? "<enabled />" : "")}\n    <script>\n")
                          .Append(Block(Script(t, null), 6)).Append("    </script>\n  </timer>\n");
                        Warnings.Add("Timers run every n seconds in Quest (they ran every n turns here)");
                        break;
                    case TriggerEvent.Subroutine:
                        sb.Append($"  <function name=\"{Safe(t.Id)}\">\n").Append(Block(Script(t, null, once: t.OnceOnly), 4)).Append("  </function>\n");
                        break;
                    case TriggerEvent.EnterRoom or TriggerEvent.BeforeEnterRoom or TriggerEvent.LeaveRoom or TriggerEvent.AfterDescribe when t.RoomId == null:
                        sb.Append($"  <turnscript name=\"{Safe(t.Id)}\">\n    <enabled />\n    <script>\n").Append(Block(Script(t, null), 6)).Append("    </script>\n  </turnscript>\n");
                        Warnings.Add("Room triggers for every room became turn scripts");
                        break;
                    default:
                        Warnings.Add($"{t.Event} triggers have no Quest equivalent");
                        break;
                }
                Scripts++;
            }
        }

        // ============================================================= scripts

        /// <summary>Several triggers for the same command: an if / else if chain, the first match wins.</summary>
        private string Chain(List<Trigger> triggers, string objectName)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var t in triggers)
            {
                var conditions = new List<string>();
                if (objectName == "object" && t.Noun1 is { } n && n != "*" && n != "-")
                {
                    var o = hasNoun2 ? "object1" : "object";
                    if (a.FindItem(n) != null) conditions.Add($"{o} = {Name(n)}");
                    else conditions.Add(string.Join(" or ", n.Split('|').Select(w => $"{o}.alias = \"{w}\"")));
                }
                if (t.RoomId != null) conditions.Add($"player.parent = {Name(t.RoomId)}");
                foreach (var c in t.Conditions) conditions.Add(Condition(c) ?? "true");
                var body = Actions(t.Actions, objectName);
                Scripts++;
                if (conditions.Count == 0 && !first) { sb.Append("else {\n").Append(Indent(body, 2)).Append("}\n"); break; }
                if (conditions.Count == 0) { sb.Append(body); break; }
                sb.Append(first ? "if (" : "else if (").Append(string.Join(" and ", conditions)).Append(") {\n").Append(Indent(body, 2)).Append("}\n");
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>One trigger's conditions (as an if) and actions.</summary>
        private string Script(Trigger t, string? objectName, bool once = false)
        {
            Scripts++;
            var body = Actions(t.Actions, objectName);
            if (once) body = "firsttime {\n" + Indent(body, 2) + "}\n";
            var conditions = t.Conditions.Select(c => Condition(c) ?? "true").ToList();
            return conditions.Count == 0 ? body : $"if ({string.Join(" and ", conditions)}) {{\n{Indent(body, 2)}}}\n";
        }

        private string? Condition(Condition c)
        {
            string? expr = c.Type switch
            {
                ConditionType.Always => "true",
                ConditionType.PlayerIn => $"player.parent = {Name(c.A!)}",
                ConditionType.ItemCarried => $"Got({Ref(c.A)})",
                ConditionType.ItemWorn => $"GetBoolean({Ref(c.A)}, \"worn\")",
                ConditionType.ItemPresent => $"ListContains(ScopeVisible(), {Ref(c.A)})",
                ConditionType.ItemIn => $"{Ref(c.A)}.parent = {Place(c.B)}",
                ConditionType.ItemExists => $"not {Ref(c.A)}.parent = null",
                ConditionType.ItemOpen => $"GetBoolean({Ref(c.A)}, \"isopen\")",
                ConditionType.ItemLit => $"GetBoolean({Ref(c.A)}, \"switchedon\")",
                ConditionType.ItemLocked => $"GetBoolean({Ref(c.A)}, \"locked\")",
                ConditionType.RoomVisited => $"GetBoolean({Name(c.A!)}, \"visited\")",
                ConditionType.VarEquals when lockExits.TryGetValue(c.A ?? "", out var exit) => c.N == 0 ? $"not GetExitByName(player.parent, \"{exit}\") = null and not GetExitByName(player.parent, \"{exit}\").locked" : "true",
                ConditionType.VarEquals => $"{Attr(c.A!)} = {c.N}",
                ConditionType.VarGreater => $"{Attr(c.A!)} > {c.N}",
                ConditionType.VarLess => $"{Attr(c.A!)} < {c.N}",
                ConditionType.VarEqualsVar => $"{Attr(c.A!)} = {Attr(c.B!)}" + (c.N != 0 ? $" + {c.N}" : ""),
                ConditionType.VarGreaterVar => $"{Attr(c.A!)} > {Attr(c.B!)}" + (c.N != 0 ? $" + {c.N}" : ""),
                ConditionType.VarLessVar => $"{Attr(c.A!)} < {Attr(c.B!)}" + (c.N != 0 ? $" + {c.N}" : ""),
                ConditionType.Chance => $"RandomChance({c.N})",
                ConditionType.TriggerFired => $"GetBoolean(game, \"fired_{Safe(c.A ?? "")}\")",
                _ => null,
            };
            if (expr == null) { Warnings.Add($"{c.Type} conditions have no Quest equivalent (treated as true)"); return null; }
            return c.Negate ? $"not ({expr})" : expr;
        }

        private string Ref(string? id) => id switch
        {
            "$noun1" => hasNoun2 ? "object1" : "object",
            "$noun2" => "object2",
            null => "null",
            _ => Name(id),
        };

        private string Place(string? loc) => loc switch
        {
            null or "" => "null",
            Locations.Carried or Locations.Worn => "player",
            Locations.Here => "player.parent",
            _ => Ref(loc),
        };

        /// <summary>A variable: "item_attr" is that object's attribute, "@score" the score, others the game's.</summary>
        private string Attr(string variable) => variable switch
        {
            "@score" => "game.score",
            "@turns" => "game.turncount",
            _ => $"{Owner(variable).Owner}.{Owner(variable).Attribute}",
        };

        private (string Owner, string Attribute) Owner(string variable)
        {
            var v = variable.TrimStart('@');
            foreach (var (id, name) in names.OrderByDescending(kv => kv.Key.Length))
                if (v.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase) && v.Length > id.Length + 1) return (name, Safe(v[(id.Length + 1)..]));
            if (v.StartsWith("game_", StringComparison.OrdinalIgnoreCase)) return ("game", Safe(v[5..]));
            return ("game", Safe(v));
        }

        private string Actions(List<GameAction> actions, string? objectName)
        {
            // Text without a line break builds up in the local "s" (Quest's own idiom); the next msg prints it.
            var sb = new StringBuilder();
            for (int i = 0; i < actions.Count; i++)
            {
                var x = actions[i];
                if (x.Type == ActionType.Message && x.N == 1) { sb.Append($"s = s + {Expr(x.Text ?? "", objectName)}\n"); continue; }
                if (x.Type == ActionType.Message) { sb.Append($"msg (s + {Expr(x.Text ?? "", objectName)})\ns = \"\"\n"); continue; }
                // if / else if / else: a guard variable, then subroutines that each need it to still be 0.
                if (x.Type == ActionType.SetVar && x.N == 0 && Branches(actions, i + 1, x.A!) is { Count: > 0 } branches)
                {
                    bool first = true;
                    guards.Add(x.A!);
                    if (branches.All(sub => Actions(sub.Actions.Skip(1).ToList(), objectName).Trim().Length == 0))
                    {
                        foreach (var sub in branches) handled.Add(sub.Id);
                        i += branches.Count;
                        continue;
                    }
                    foreach (var sub in branches)
                    {
                        handled.Add(sub.Id);
                        var conditions = sub.Conditions.Skip(1).Select(c => Condition(c) ?? "true").ToList();
                        var body = Actions(sub.Actions.Skip(1).ToList(), objectName);
                        if (conditions.Count == 0) { sb.Append(first ? body : "else {\n" + Indent(body, 2) + "}\n"); break; }
                        sb.Append(first ? "if (" : "else if (").Append(string.Join(" and ", conditions)).Append(") {\n").Append(Indent(body, 2)).Append("}\n");
                        first = false;
                    }
                    guards.Add(x.A!);
                    i += branches.Count;
                    continue;
                }
                // firsttime { … } otherwise { … }
                if (x.Type == ActionType.RunTrigger && Sub(x.A) is { } later && later.Conditions is [{ Type: ConditionType.TriggerFired } fired]
                    && i + 1 < actions.Count && actions[i + 1] is { Type: ActionType.RunTrigger } next && next.A == fired.A && Sub(next.A) is { OnceOnly: true } once)
                {
                    handled.Add(later.Id); handled.Add(once.Id);
                    sb.Append("firsttime {\n").Append(Indent(Actions(once.Actions, objectName), 2)).Append("}\notherwise {\n").Append(Indent(Actions(later.Actions, objectName), 2)).Append("}\n");
                    i++;
                    continue;
                }
                if (x.Type == ActionType.RunTrigger && Sub(x.A) is { } inline)
                {
                    handled.Add(inline.Id);
                    sb.Append(Script(inline, objectName, once: inline.OnceOnly));
                    continue;
                }
                var line = Statement(x);
                if (line != null) sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private readonly HashSet<string> guards = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A subroutine only this trigger's script runs, so it can be written inline.</summary>
        private Trigger? Sub(string? id) =>
            a.Triggers.FirstOrDefault(t => t.Id == id) is { Event: TriggerEvent.Subroutine } t
            && a.Triggers.Sum(o => o.Actions.Count(x => x.Type == ActionType.RunTrigger && x.A == id)) == 1
            && !a.Triggers.Any(o => o.Actions.Any(x => x.Type is ActionType.EnableTrigger or ActionType.DisableTrigger && x.A == id))
                ? t : null;

        private List<Trigger> Branches(List<GameAction> actions, int from, string guard)
        {
            var list = new List<Trigger>();
            for (int i = from; i < actions.Count && actions[i].Type == ActionType.RunTrigger; i++)
            {
                if (Sub(actions[i].A) is not { } t || t.Conditions.FirstOrDefault() is not { Type: ConditionType.VarEquals, N: 0, Negate: false } c || c.A != guard) break;
                bool isElse = t.Conditions.Count == 1;
                if (!isElse && t.Actions.FirstOrDefault() is not { Type: ActionType.SetVar, N: 1 } set || !isElse && t.Actions[0].A != guard) break;
                if (isElse) t = new Trigger { Id = t.Id, Actions = t.Actions.Prepend(new GameAction(ActionType.Done)).ToList() };
                list.Add(t);
                if (isElse) break;
            }
            return list;
        }

        private string? Statement(GameAction x)
        {
            switch (x.Type)
            {
                case ActionType.GoTo: return $"player.parent = {Name(x.A!)}";
                case ActionType.MoveItem or ActionType.CreateItem:
                    return x.B switch
                    {
                        null or "" when x.Type == ActionType.MoveItem => $"RemoveObject ({Ref(x.A)})",
                        Locations.Carried or Locations.Worn => $"AddToInventory ({Ref(x.A)})",
                        Locations.Here or null or "" => $"MoveObjectHere ({Ref(x.A)})",
                        _ => $"MoveObject ({Ref(x.A)}, {Ref(x.B)})",
                    };
                case ActionType.TakeItem: return $"AddToInventory ({Ref(x.A)})";
                case ActionType.DropItem: return $"MoveObjectHere ({Ref(x.A)})";
                case ActionType.DestroyItem: return $"RemoveObject ({Ref(x.A)})";
                case ActionType.WearItem: return $"{Ref(x.A)}.worn = true";
                case ActionType.UnwearItem: return $"{Ref(x.A)}.worn = false";
                case ActionType.SetOpen: return $"{Ref(x.A)}.isopen = {(x.N != 0 ? "true" : "false")}";
                case ActionType.SetLit: return $"{Ref(x.A)}.switchedon = {(x.N != 0 ? "true" : "false")}";
                case ActionType.SetLocked: return $"{Ref(x.A)}.locked = {(x.N != 0 ? "true" : "false")}";
                case ActionType.SetVar when lockExits.TryGetValue(x.A ?? "", out var exit): return $"{(x.N == 0 ? "UnlockExit" : "LockExit")} ({exit})";
                case ActionType.SetVar: return $"{Attr(x.A!)} = {x.N}";
                case ActionType.AddVar: return $"{Attr(x.A!)} = {Attr(x.A!)} {(x.N < 0 ? "-" : "+")} {Math.Abs(x.N)}";
                case ActionType.CopyVar: return $"{Attr(x.A!)} = {Attr(x.B!)}";
                case ActionType.RandomVar: return $"{Attr(x.A!)} = GetRandomInt(1, {x.N})";
                case ActionType.AwardScore: return $"IncreaseScore ({x.N})";
                case ActionType.Win or ActionType.Lose:
                    return (string.IsNullOrWhiteSpace(x.Text) ? "" : Msg(x.Text!, null) + "\n") + "finish";
                case ActionType.Quit: return "finish";
                case ActionType.ShowPicture when x.A != null && File(x.A) is { } pic: return $"picture (\"{pic}\")";
                case ActionType.PlaySound when x.A != null && a.FindSound(x.A) is { } snd && Sound(snd) is { } file: return $"play sound (\"{file}\", false, {(x.N == 1 ? "true" : "false")})";
                case ActionType.StopSound: return "stop sound";
                case ActionType.ClearScreen: return "ClearScreen";
                case ActionType.Look: return "ShowRoomDescription";
                case ActionType.RunTrigger when a.Triggers.FirstOrDefault(t => t.Id == x.A) is { Event: TriggerEvent.Subroutine }: return $"{Safe(x.A!)}";
                case ActionType.EnableTrigger: return $"EnableTurnScript ({Safe(x.A!)})";
                case ActionType.DisableTrigger: return $"DisableTurnScript ({Safe(x.A!)})";
                case ActionType.Ok: return "msg (\"OK.\")";
                case ActionType.Done: return null;
                default:
                    Warnings.Add($"{x.Type} actions have no Quest equivalent");
                    return null;
            }
        }

        /// <summary>A msg statement; engine placeholders become Quest expressions.</summary>
        private string Msg(string text, string? objectName) => $"msg ({Expr(text, objectName)})";

        private string Expr(string text, string? objectName)
        {
            var parts = new List<string>();
            int last = 0;
            foreach (Match m in Regex.Matches(text, @"\{([^{}]+)\}"))
            {
                if (m.Index > last) parts.Add(Quote(text[last..m.Index]));
                var key = m.Groups[1].Value.Trim();
                var second = objectName == "object" && key.Contains("noun2") ? "object2" : null;
                if (objectName == "object" && hasNoun2) objectName = "object1";
                parts.Add(key switch
                {
                    "the noun1" or "The noun1" when objectName != null => $"GetDefiniteName({objectName})",
                    "noun1" or "Noun1" when objectName != null => $"GetDisplayAlias({objectName})",
                    "the noun2" or "The noun2" when second != null => $"GetDefiniteName({second})",
                    "noun2" or "Noun2" when second != null => $"GetDisplayAlias({second})",
                    _ when key.StartsWith("var:", StringComparison.Ordinal) => $"ToString({Attr(key[4..])})",
                    _ => Quote(""),
                });
                last = m.Index + m.Length;
            }
            if (last < text.Length) parts.Add(Quote(text[last..]));
            if (parts.Count == 0) parts.Add(Quote(""));
            return string.Join(" + ", parts);
        }

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "<br/>") + "\"";

        private static string Text(string s) => Regex.Replace(s, @"\{[^{}]*\}", "");

        /// <summary>A script inside an XML element: indented and escaped.</summary>
        private static string Block(string s, int spaces)
        {
            if (s.Contains("msg (s + ") || s.Contains("s = s + "))
            {
                if (!s.Contains("s = s + ")) s = s.Replace("msg (s + ", "msg (").Replace("\ns = \"\"\n", "\n");
                else
                {
                    s = "s = \"\"\n" + s;
                    if (s.LastIndexOf("s = s + ", StringComparison.Ordinal) > s.LastIndexOf("msg (s", StringComparison.Ordinal)) s += "msg (s)\n";   // text left over at the end
                    if (s.TrimEnd().EndsWith("s = \"\"")) s = s.TrimEnd()[..^"s = \"\"".Length];
                    s = s.Replace("msg (s + \"\")", "msg (s)");
                }
            }
            return X(Indent(s, spaces)).Replace("&apos;", "'").Replace("&quot;", "\"");
        }

        private static string Indent(string s, int spaces)
        {
            var pad = new string(' ', spaces);
            return string.Concat(s.Split('\n').Where(l => l.Length > 0).Select(l => pad + l + "\n"));
        }

        // ============================================================= files

        private string? File(string pictureId)
        {
            if (a.FindPicture(pictureId) is not { BitmapAsset: { } asset } || !a.Assets.TryGetValue(asset, out var bytes))
            {
                Warnings.Add("Drawn (vector) pictures have no Quest equivalent; only image pictures are kept");
                return null;
            }
            var name = Path.GetFileName(asset);
            files[name] = bytes;
            return name;
        }

        private string? Sound(SoundAsset s)
        {
            if (!a.Assets.TryGetValue(s.AssetName, out var bytes)) return null;
            var name = Path.GetFileName(s.AssetName);
            files[name] = bytes;
            return name;
        }
    }
}
