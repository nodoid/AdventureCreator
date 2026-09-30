using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Twine;

/// <summary>
/// Writes a game as a Twine story: Twee 3 source, or a Twine 2 library file (HTML) that Twine 2 can import and
/// publish. Rooms become passages and exits links; variables, jumps and conditional text are written in Harlowe or
/// SugarCube, matching the story's original format. Items and parser commands have no Twine equivalent.
/// </summary>
public sealed class TwineExporter : IAdventureExporter
{
    public string Id => "twine";
    public string Name => "Twine";

    private static bool Twee(Adventure a) => a.Origin?.System == "twine" && a.Origin.Format.Contains("Twee", StringComparison.OrdinalIgnoreCase);
    private static bool SugarCube(Adventure a) => a.Origin?.Format.Contains("SugarCube", StringComparison.OrdinalIgnoreCase) == true;

    public string Extension(Adventure a) => Twee(a) ? "twee" : "html";
    public string? CannotExport(Adventure a) => a.IsStory ? "Z-code stories can't be converted." : a.Rooms.Count == 0 ? "The game has no rooms." : null;

    public ExportResult Export(Adventure a) => new Writer(a, SugarCube(a), Twee(a)).Run();

    private sealed class Writer(Adventure a, bool sugarCube, bool twee)
    {
        private readonly ExportWarnings warnings = new();
        private readonly Dictionary<string, string> names = new();
        private static readonly Regex ChoiceLine = new(@"^(\d+)\. (.*)$");

        public ExportResult Run()
        {
            foreach (var r in a.Rooms)
            {
                var name = string.IsNullOrWhiteSpace(r.Name) ? r.Id : r.Name.Trim();
                if (names.ContainsValue(name)) name += $" ({r.Id})";
                names[r.Id] = name;
            }
            var passages = new List<(string Name, string[] Tags, string Text)>();
            foreach (var r in a.Rooms) passages.Add((names[r.Id], Array.Empty<string>(), Passage(r)));
            var init = Startup();
            if (init.Length > 0) passages.Add((sugarCube ? "StoryInit" : "Startup", sugarCube ? Array.Empty<string>() : new[] { "startup" }, init));

            if (a.Items.Any(i => !i.Scenery)) warnings.Add("Items can't be carried in Twine; they aren't included");
            if (a.Vocabulary.Verbs.Count > 0 || a.Triggers.Any(t => t.Event == TriggerEvent.BeforeCommand && !IsChoiceTrigger(t)))
                warnings.Add("Parser commands and their triggers have no Twine equivalent");

            var start = names.GetValueOrDefault(a.StartRoomId) ?? names.Values.First();
            var bytes = twee ? Twee(passages, start) : Html(passages, start);
            var result = new ExportResult(bytes) { Summary = $"{passages.Count} passages in {(sugarCube ? "SugarCube" : "Harlowe")} ({(twee ? "Twee source" : "Twine 2 library file")})" };
            warnings.CopyTo(result.Warnings);
            return result;
        }

        private static bool IsChoiceTrigger(Trigger t) => false;

        // ============================================================= passages

        private string Passage(Room r)
        {
            var sb = new StringBuilder();
            // Arrival: variables and jumps.
            foreach (var t in a.Triggers.Where(t => t.RoomId == r.Id && t.Event is TriggerEvent.BeforeEnterRoom or TriggerEvent.EnterRoom))
            {
                var macros = t.Actions.Select(Macro).Where(m => m != null).ToList();
                var say = t.Actions.Where(x => x.Type == ActionType.Message).Select(x => x.Text ?? "").ToList();
                var body = string.Concat(macros) + string.Join("\n", say);
                if (body.Length == 0) continue;
                sb.Append(Wrap(t.Conditions, body)).Append('\n');
            }
            if (r.PictureId != null && a.FindPicture(r.PictureId) is { BitmapAsset: { } asset } && a.Assets.TryGetValue(asset, out var img))
                sb.Append($"<img src=\"data:image/{(img.Length > 1 && img[0] == 0xFF ? "jpeg" : "png")};base64,{Convert.ToBase64String(img)}\">\n");

            // The description, without the numbered choice list the importer added.
            var choices = r.Exits.Where(e => e.Direction.StartsWith("choice", StringComparison.Ordinal)).ToList();
            var lines = (r.Description ?? "").Split('\n').ToList();
            var choiceText = new Dictionary<int, string>();
            while (lines.Count > 0 && ChoiceLine.Match(lines[^1]) is { Success: true } m && choices.Any(e => e.Direction == "choice" + m.Groups[1].Value))
            {
                choiceText[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
                lines.RemoveAt(lines.Count - 1);
            }
            var prose = Text(string.Join('\n', lines).TrimEnd());

            // Links: unconditional choices, then ordinary exits.
            var links = new List<string>();
            foreach (var e in choices.Where(e => e.Conditions.Count == 0).OrderBy(e => int.Parse(e.Direction[6..])))
            {
                int n = int.Parse(e.Direction[6..]);
                links.Add(Link(choiceText.GetValueOrDefault(n) ?? names.GetValueOrDefault(e.TargetRoomId) ?? e.TargetRoomId, e.TargetRoomId));
            }
            foreach (var e in r.Exits.Where(e => !e.Direction.StartsWith("choice", StringComparison.Ordinal) && !e.Hidden))
            {
                var link = Link("Go " + e.Direction, e.TargetRoomId);
                links.Add(e.Conditions.Count == 0 ? link : Wrap(e.Conditions, link));
            }
            sb.Append(Inline(prose, links));

            // Conditional text after the description.
            foreach (var t in a.Triggers.Where(t => t.RoomId == r.Id && t.Event == TriggerEvent.AfterDescribe))
                sb.Append(Conditional(r, t, choices));
            return sb.ToString().Trim();
        }

        /// <summary>
        /// An after-description trigger. The importer's pattern – reset a guard, then run branch subroutines each
        /// testing the guard – becomes an if / else-if / else chain; other triggers become a single if.
        /// </summary>
        private string Conditional(Room r, Trigger t, List<Exit> choices)
        {
            var subs = t.Actions.Where(x => x.Type == ActionType.RunTrigger).Select(x => a.Triggers.FirstOrDefault(s => s.Id == x.A)).Where(s => s != null).Select(s => s!).ToList();
            var guard = t.Actions.FirstOrDefault(x => x.Type == ActionType.SetVar && x.N == 0)?.A;
            if (guard == null || subs.Count == 0)
            {
                var text = string.Join("\n", t.Actions.Where(x => x.Type == ActionType.Message).Select(x => x.Text));
                return text.Length == 0 ? "" : "\n" + Wrap(t.Conditions, Text(text));
            }
            var sb = new StringBuilder("\n");
            bool first = true;
            foreach (var s in subs)
            {
                var conditions = s.Conditions.Where(c => c.A != guard).ToList();
                var body = new StringBuilder();
                foreach (var x in s.Actions)
                {
                    if (x.Type == ActionType.SetVar && x.A == guard) continue;
                    if (x.Type == ActionType.Message) body.Append(BodyText(x.Text ?? "", choices));
                    else if (Macro(x) is { } m) body.Append(m);
                }
                if (conditions.Count == 0 && !first) { sb.Append(sugarCube ? $"<<else>>{body}" : $"(else:)[{body}]"); break; }
                var cond = Condition(conditions);
                if (cond == null) { warnings.Add("Conditions Twine can't express were left out"); continue; }
                sb.Append(sugarCube ? (first ? $"<<if {cond}>>" : $"<<elseif {cond}>>") + body : $"({(first ? "if" : "else-if")}: {cond})[{body}]");
                first = false;
            }
            if (sugarCube && !first) sb.Append("<</if>>");
            return first ? "" : sb.ToString();
        }

        /// <summary>Conditional text: its "n. text" choice lines become links again.</summary>
        private string BodyText(string text, List<Exit> choices)
        {
            var lines = text.Split('\n').ToList();
            var links = new List<string>();
            while (lines.Count > 0 && ChoiceLine.Match(lines[^1]) is { Success: true } m && choices.FirstOrDefault(e => e.Direction == "choice" + m.Groups[1].Value) is { } exit)
            {
                links.Insert(0, Link(m.Groups[2].Value, exit.TargetRoomId));
                lines.RemoveAt(lines.Count - 1);
            }
            return Inline(Text(string.Join('\n', lines).TrimEnd()), links);
        }

        /// <summary>
        /// Puts links where their text appears in the prose (the importer keeps link text in the prose); links whose
        /// text isn't there go at the end.
        /// </summary>
        private static string Inline(string prose, List<string> links)
        {
            var extra = new List<string>();
            foreach (var link in links)
            {
                var m = Regex.Match(link, @"^\[\[(.*?)(->.*)?\]\]$");
                var text = m.Success ? m.Groups[1].Value : "";
                int at = text.Length > 0 ? prose.IndexOf(text, StringComparison.Ordinal) : -1;
                // Only replace text that isn't already inside a link.
                if (at >= 0 && !prose[..at].EndsWith("[[", StringComparison.Ordinal) && prose.LastIndexOf("[[", at, StringComparison.Ordinal) <= prose.LastIndexOf("]]", at, StringComparison.Ordinal))
                    prose = prose[..at] + link + prose[(at + text.Length)..];
                else extra.Add(link);
            }
            return extra.Count == 0 ? prose : prose + (prose.Length > 0 ? "\n\n" : "") + string.Join("\n", extra);
        }

        private string Link(string text, string target)
        {
            var name = names.GetValueOrDefault(target) ?? target;
            return text == name ? $"[[{name}]]" : $"[[{text}->{name}]]";
        }

        private string Wrap(List<Condition> conditions, string body)
        {
            if (conditions.Count == 0) return body;
            var cond = Condition(conditions);
            if (cond == null) { warnings.Add("Conditions Twine can't express were left out"); return body; }
            return sugarCube ? $"<<if {cond}>>{body}<</if>>" : $"(if: {cond})[{body}]";
        }

        // ============================================================= variables and conditions

        private static string VarName(string v) => Regex.Replace(v.StartsWith("tw_") ? v[3..] : v.TrimStart('@'), @"[^A-Za-z0-9_]", "_");

        private string? Macro(GameAction x)
        {
            switch (x.Type)
            {
                case ActionType.SetVar: return sugarCube ? $"<<set ${VarName(x.A!)} to {x.N}>>" : $"(set: ${VarName(x.A!)} to {x.N})";
                case ActionType.AddVar:
                    return sugarCube ? $"<<set ${VarName(x.A!)} {(x.N < 0 ? "-=" : "+=")} {Math.Abs(x.N)}>>" : $"(set: ${VarName(x.A!)} to it {(x.N < 0 ? "-" : "+")} {Math.Abs(x.N)})";
                case ActionType.CopyVar: return sugarCube ? $"<<set ${VarName(x.A!)} to ${VarName(x.B!)}>>" : $"(set: ${VarName(x.A!)} to ${VarName(x.B!)})";
                case ActionType.AwardScore: return sugarCube ? $"<<set $score += {x.N}>>" : $"(set: $score to it + {x.N})";
                case ActionType.GoTo:
                    var target = names.GetValueOrDefault(x.A ?? "") ?? x.A;
                    return sugarCube ? $"<<goto \"{target}\">>" : $"(goto: \"{target}\")";
                case ActionType.Message: return null;
                case ActionType.RunTrigger: case ActionType.Done: return null;
                default:
                    warnings.Add($"{x.Type} actions have no Twine equivalent");
                    return null;
            }
        }

        private string? Condition(List<Condition> conditions)
        {
            var parts = new List<string>();
            foreach (var c in conditions)
            {
                string? p = c.Type switch
                {
                    ConditionType.VarEquals => sugarCube ? $"${VarName(c.A!)} {(c.Negate ? "neq" : "eq")} {c.N}" : $"${VarName(c.A!)} {(c.Negate ? "is not" : "is")} {c.N}",
                    ConditionType.VarGreater => (c.Negate ? "not " : "") + $"${VarName(c.A!)} {(sugarCube ? "gt" : ">")} {c.N}",
                    ConditionType.VarLess => (c.Negate ? "not " : "") + $"${VarName(c.A!)} {(sugarCube ? "lt" : "<")} {c.N}",
                    ConditionType.VarEqualsVar => sugarCube ? $"${VarName(c.A!)} {(c.Negate ? "neq" : "eq")} ${VarName(c.B!)}" : $"${VarName(c.A!)} {(c.Negate ? "is not" : "is")} ${VarName(c.B!)}",
                    ConditionType.RoomVisited when names.TryGetValue(c.A ?? "", out var room) =>
                        (c.Negate ? (sugarCube ? "!" : "not ") : "") + (sugarCube ? $"visited(\"{room}\")" : $"(visited: \"{room}\")"),
                    ConditionType.Always when c.Negate => "false",
                    ConditionType.Always => "true",
                    _ => null,
                };
                if (p == null) return null;
                parts.Add(p);
            }
            return string.Join(" and ", parts);
        }

        private string Startup()
        {
            var sb = new StringBuilder();
            foreach (var v in a.Variables.Where(v => v.InitialValue != 0))
                sb.Append(sugarCube ? $"<<set ${VarName(v.Name)} to {v.InitialValue}>>" : $"(set: ${VarName(v.Name)} to {v.InitialValue})");
            foreach (var t in a.Triggers.Where(t => t.Event == TriggerEvent.GameStart))
                foreach (var x in t.Actions) if (Macro(x) is { } m) sb.Append(m);
            return sb.ToString();
        }

        /// <summary>Engine placeholders become Twine variables; others are dropped.</summary>
        private string Text(string s) => Regex.Replace(s, @"\{var:([^}]+)\}", m => "$" + VarName(m.Groups[1].Value)).Replace("{", "").Replace("}", "");

        // ============================================================= files

        private byte[] Twee(List<(string Name, string[] Tags, string Text)> passages, string start)
        {
            var sb = new StringBuilder();
            sb.Append(":: StoryTitle\n").Append(a.Title).Append("\n\n");
            sb.Append(":: StoryData\n{\n  \"ifid\": \"").Append(Guid.NewGuid().ToString().ToUpperInvariant()).Append("\",\n  \"format\": \"")
              .Append(sugarCube ? "SugarCube" : "Harlowe").Append("\",\n  \"format-version\": \"").Append(sugarCube ? "2.36.1" : "3.3.8")
              .Append("\",\n  \"start\": \"").Append(start.Replace("\"", "\\\"")).Append("\"\n}\n\n");
            foreach (var (name, tags, text) in passages)
                sb.Append(":: ").Append(name.Replace("[", "\\[").Replace("]", "\\]")).Append(tags.Length > 0 ? $" [{string.Join(' ', tags)}]" : "").Append('\n').Append(text).Append("\n\n");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private byte[] Html(List<(string Name, string[] Tags, string Text)> passages, string start)
        {
            var sb = new StringBuilder();
            int startPid = passages.FindIndex(p => p.Name == start) + 1;
            sb.Append($"<tw-storydata name=\"{WebUtility.HtmlEncode(a.Title)}\" startnode=\"{startPid}\" creator=\"Adventure Creator\" creator-version=\"1.0\" ");
            sb.Append($"ifid=\"{Guid.NewGuid().ToString().ToUpperInvariant()}\" zoom=\"1\" format=\"{(sugarCube ? "SugarCube" : "Harlowe")}\" format-version=\"{(sugarCube ? "2.36.1" : "3.3.8")}\" options=\"\" hidden>");
            sb.Append("<style role=\"stylesheet\" id=\"twine-user-stylesheet\" type=\"text/twine-css\"></style><script role=\"script\" id=\"twine-user-script\" type=\"text/twine-javascript\"></script>\n");
            for (int i = 0; i < passages.Count; i++)
            {
                var (name, tags, text) = passages[i];
                sb.Append($"<tw-passagedata pid=\"{i + 1}\" name=\"{WebUtility.HtmlEncode(name)}\" tags=\"{string.Join(' ', tags)}\" position=\"{100 + (i % 8) * 150},{100 + (i / 8) * 150}\" size=\"100,100\">")
                  .Append(WebUtility.HtmlEncode(text)).Append("</tw-passagedata>\n");
            }
            sb.Append("</tw-storydata>\n");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
