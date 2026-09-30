using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.ZMachine;
using AdventureSystem.Importers.Common;

namespace AdventureSystem.Importers.Twine;

/// <summary>
/// Twine stories (open source, GPL): published Twine 2 HTML (Harlowe, SugarCube, Chapbook, Snowman), Twine 1
/// HTML and Twee source. Passages become rooms and links become numbered choices, so a choice-based story plays in
/// the parser-based engine; variables, conditional text, jumps and images are converted where they map directly.
/// </summary>
public sealed class TwineImporter : IAdventureImporter
{
    public string Name => "Twine (Harlowe, SugarCube, Chapbook, Twee)";
    public string Id => "twine";
    public IReadOnlyList<string> Extensions => new[] { "html", "htm", "twee", "tw", "tw2", "txt" };

    public bool CanImport(byte[] data, string fileName)
    {
        var text = Head(data);
        return text.Contains("<tw-storydata", StringComparison.OrdinalIgnoreCase) || text.Contains("id=\"storeArea\"", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(text, @"^::\s*\S", RegexOptions.Multiline) && (text.Contains(":: StoryTitle") || text.Contains(":: Start") || fileName.EndsWith(".twee", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tw", StringComparison.OrdinalIgnoreCase));
    }

    private static string Head(byte[] data) => Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 400_000));

    public ImportResult Import(byte[] data, string fileName)
    {
        var story = TwineStory.Read(Encoding.UTF8.GetString(data), fileName);
        return new TwineConverter(story, fileName).Run();
    }
}

internal sealed record TwinePassage(string Name, string[] Tags, string Text);

internal sealed class TwineStory
{
    public string Title = "";
    public string Format = "";
    public string? Start;
    public List<TwinePassage> Passages = new();

    public static TwineStory Read(string text, string fileName)
    {
        var s = new TwineStory();
        if (text.Contains("<tw-storydata", StringComparison.OrdinalIgnoreCase))
        {
            var data = Regex.Match(text, @"<tw-storydata([^>]*)>", RegexOptions.IgnoreCase);
            string Attr(string attrs, string name) => WebUtility.HtmlDecode(Regex.Match(attrs, name + @"\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase).Groups[1].Value);
            s.Title = Attr(data.Groups[1].Value, "name");
            s.Format = Attr(data.Groups[1].Value, "format");
            var startPid = Attr(data.Groups[1].Value, "startnode");
            foreach (Match m in Regex.Matches(text, @"<tw-passagedata([^>]*)>(.*?)</tw-passagedata>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var attrs = m.Groups[1].Value;
                var p = new TwinePassage(Attr(attrs, "name"), Attr(attrs, "tags").Split(' ', StringSplitOptions.RemoveEmptyEntries), WebUtility.HtmlDecode(m.Groups[2].Value));
                s.Passages.Add(p);
                if (Attr(attrs, "pid") == startPid) s.Start = p.Name;
            }
        }
        else if (text.Contains("id=\"storeArea\"", StringComparison.OrdinalIgnoreCase))
        {
            // Twine 1: <div tiddler="Name" tags="…">text with \n escapes</div>
            s.Format = "Twine 1";
            foreach (Match m in Regex.Matches(text, @"<div[^>]*\btiddler=""([^""]*)""([^>]*)>(.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var tags = Regex.Match(m.Groups[2].Value, @"tags=""([^""]*)""").Groups[1].Value;
                var body = WebUtility.HtmlDecode(m.Groups[3].Value).Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\s", "\\");
                s.Passages.Add(new TwinePassage(WebUtility.HtmlDecode(m.Groups[1].Value), tags.Split(' ', StringSplitOptions.RemoveEmptyEntries), body));
            }
            s.Title = s.Passages.FirstOrDefault(p => p.Name == "StoryTitle")?.Text.Trim() ?? "";
            s.Start = s.Passages.Any(p => p.Name == "Start") ? "Start" : null;
        }
        else
        {
            // Twee: ":: Name [tags] {metadata}"
            s.Format = "Twee";
            TwinePassage? current = null;
            var body = new StringBuilder();
            void Flush() { if (current != null) s.Passages.Add(current with { Text = body.ToString().Trim('\n') }); body.Clear(); }
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var header = Regex.Match(line, @"^::\s*(.*?)\s*(\[([^\]]*)\])?\s*(\{.*\})?\s*$");
                if (line.StartsWith("::") && header.Success)
                {
                    Flush();
                    current = new TwinePassage(header.Groups[1].Value.Replace("\\[", "[").Replace("\\]", "]"), header.Groups[3].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries), "");
                    continue;
                }
                if (current != null) body.Append(line).Append('\n');
            }
            Flush();
            s.Title = s.Passages.FirstOrDefault(p => p.Name == "StoryTitle")?.Text.Trim() ?? "";
            if (s.Passages.FirstOrDefault(p => p.Name == "StoryData") is { } storyData)
            {
                try
                {
                    using var json = JsonDocument.Parse(storyData.Text);
                    if (json.RootElement.TryGetProperty("start", out var start)) s.Start = start.GetString();
                    if (json.RootElement.TryGetProperty("format", out var format)) s.Format = "Twee (" + format.GetString() + ")";
                }
                catch (JsonException) { }
            }
            s.Start ??= s.Passages.Any(p => p.Name == "Start") ? "Start" : null;
        }
        if (s.Title.Length == 0) s.Title = Path.GetFileNameWithoutExtension(fileName);
        return s;
    }
}

internal sealed class TwineConverter
{
    private static readonly HashSet<string> Special = new(StringComparer.OrdinalIgnoreCase)
    {
        "StoryTitle", "StoryData", "StoryAuthor", "StorySubtitle", "StoryMenu", "StoryCaption", "StoryBanner", "StoryShare", "StoryInit",
        "StoryIncludes", "StorySettings", "PassageReady", "PassageDone", "PassageHeader", "PassageFooter", "StoryInterface",
    };

    private readonly TwineStory story;
    private readonly string fileName;
    private readonly Adventure a = new();
    private readonly Dictionary<string, string> ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> skipped = new();
    private readonly List<string> warnings = new();
    private int nextTrigger;

    public TwineConverter(TwineStory story, string fileName)
    {
        this.story = story;
        this.fileName = fileName;
    }

    public ImportResult Run()
    {
        a.Title = story.Title;
        a.Description = $"Imported from the Twine story {Path.GetFileName(fileName)} ({story.Format}).";
        Choices.Configure(a);
        var passages = story.Passages.Where(p => !Special.Contains(p.Name) && !p.Tags.Any(t => t is "script" or "stylesheet" or "widget" or "Twine.image" or "annotation")).ToList();
        foreach (var p in passages) ids[p.Name] = Unique(Slug(p.Name));
        foreach (var p in passages) Passage(p);

        // StoryInit (SugarCube) and Harlowe "startup" passages set up variables at the start.
        foreach (var init in story.Passages.Where(p => p.Name == "StoryInit" || p.Tags.Contains("startup")))
        {
            var t = new Trigger { Id = "twine_init", Name = "Story start", Event = TriggerEvent.GameStart, StopsCommand = false };
            foreach (var seg in Parse(init.Text)) if (seg is SetSeg set && SetAction(set) is { } act) t.Actions.Add(act);
            if (t.Actions.Count > 0) a.Triggers.Add(t);
        }

        a.StartRoomId = story.Start != null && ids.TryGetValue(story.Start, out var start) ? start : a.Rooms.FirstOrDefault()?.Id ?? "";
        if (a.Rooms.Count == 0) a.Rooms.Add(new Room { Id = "start", Name = a.Title });
        foreach (var v in variables.Order()) a.Variables.Add(new Variable { Name = v, Description = "Twine variable" });
        foreach (var (what, count) in skipped.OrderByDescending(k => k.Value)) warnings.Add($"{what}: {count} not converted.");
        a.Notes.Add("Imported from Twine: each passage is a room and each link a numbered choice (type its number). Variables, conditional text, jumps and images were converted where they map directly.");
        a.Notes.AddRange(warnings);
        var result = new ImportResult(a) { DetectedFormat = $"Twine story ({story.Format}), {passages.Count} passages" };
        result.Warnings.AddRange(warnings);
        return result;
    }

    private static string Slug(string name)
    {
        var s = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        return s.Length == 0 ? "passage" : char.IsDigit(s[0]) ? "p" + s : s;
    }

    private string Unique(string id)
    {
        var u = id;
        for (int i = 2; ids.ContainsValue(u); i++) u = $"{id}_{i}";
        return u;
    }

    private void Skip(string what) => skipped[what] = skipped.TryGetValue(what, out var n) ? n + 1 : 1;

    // ================================================================= segments

    private abstract record Seg;
    private sealed record TextSeg(string Text) : Seg;
    private sealed record LinkSeg(string Text, string Target) : Seg;
    private sealed record SetSeg(string Variable, string Op, string Value) : Seg;
    private sealed record GotoSeg(string Target) : Seg;
    private sealed record IfSeg(List<(string Condition, List<Seg> Body)> Branches, List<Seg>? Else) : Seg;
    private sealed record ImageSeg(string Source) : Seg;

    private static readonly Regex Link = new(@"\[\[(.*?)\]\]", RegexOptions.Singleline);

    /// <summary>Splits passage text into prose, links, variable changes, jumps, conditional blocks and images.</summary>
    private List<Seg> Parse(string text)
    {
        var segs = new List<Seg>();
        var prose = new StringBuilder();
        void FlushProse() { if (prose.Length > 0) { segs.Add(new TextSeg(prose.ToString())); prose.Clear(); } }
        int i = 0;
        while (i < text.Length)
        {
            // [[links]]
            if (text.AsSpan(i).StartsWith("[[") && Link.Match(text, i) is { Success: true } lm && lm.Index == i)
            {
                FlushProse();
                segs.Add(ParseLink(lm.Groups[1].Value));
                i += lm.Length;
                continue;
            }
            // [img[src]] or [img[src][link]]
            var img = Regex.Match(text[i..], @"^\[img\[([^\]]*)\](\[[^\]]*\])?\]");
            if (img.Success) { FlushProse(); segs.Add(new ImageSeg(img.Groups[1].Value)); i += img.Length; continue; }
            // <img src="…">
            var html = Regex.Match(text[i..], @"^<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
            if (html.Success) { FlushProse(); segs.Add(new ImageSeg(html.Groups[1].Value)); i += html.Length; continue; }
            // Harlowe macros: (name: …)[hook]
            if (text[i] == '(' && Regex.Match(text[i..], @"^\(([A-Za-z][\w-]*):") is { Success: true } hm)
            {
                int close = MatchParen(text, i);
                if (close > i)
                {
                    FlushProse();
                    var name = hm.Groups[1].Value.ToLowerInvariant();
                    var args = text[(i + hm.Length)..close].Trim();
                    i = close + 1;
                    Harlowe(name, args, text, ref i, segs);
                    continue;
                }
            }
            // SugarCube macros: <<name …>>
            if (text.AsSpan(i).StartsWith("<<") && !text.AsSpan(i).StartsWith("<</"))
            {
                int end = text.IndexOf(">>", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    FlushProse();
                    var inner = text[(i + 2)..end].Trim();
                    i = end + 2;
                    SugarCube(inner, text, ref i, segs);
                    continue;
                }
            }
            // $variable printed inline
            if (text[i] == '$' && Regex.Match(text[i..], @"^\$([A-Za-z_]\w*)") is { Success: true } vm)
            {
                prose.Append("{var:" + Var(vm.Groups[1].Value) + "}");
                i += vm.Length;
                continue;
            }
            prose.Append(text[i]);
            i++;
        }
        FlushProse();
        return segs;
    }

    private static LinkSeg ParseLink(string inner)
    {
        // [[text->target]] [[target<-text]] [[text|target]] [[target]] (with an optional ][setter in Twine 1)
        inner = Regex.Replace(inner, @"\]\[.*$", "");
        int arrow = inner.LastIndexOf("->", StringComparison.Ordinal);
        if (arrow >= 0) return new LinkSeg(inner[..arrow].Trim(), inner[(arrow + 2)..].Trim());
        int back = inner.IndexOf("<-", StringComparison.Ordinal);
        if (back >= 0) return new LinkSeg(inner[(back + 2)..].Trim(), inner[..back].Trim());
        int bar = inner.IndexOf('|');
        if (bar >= 0) return new LinkSeg(inner[..bar].Trim(), inner[(bar + 1)..].Trim());
        return new LinkSeg(inner.Trim(), inner.Trim());
    }

    private static int MatchParen(string s, int open)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0') { if (c == quote && s[i - 1] != '\\') quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>A hook "[…]" starting at <paramref name="i"/> (after optional spaces), or null.</summary>
    private static string? Hook(string s, ref int i)
    {
        int j = i;
        while (j < s.Length && s[j] == ' ') j++;
        if (j >= s.Length || s[j] != '[' || (j + 1 < s.Length && s[j + 1] == '[')) return null;
        int depth = 0;
        for (int k = j; k < s.Length; k++)
        {
            if (s[k] == '[') depth++;
            else if (s[k] == ']' && --depth == 0) { i = k + 1; return s[(j + 1)..k]; }
        }
        return null;
    }

    private static string Unquote(string s) => s.Trim().Trim('"', '\'');

    private void Harlowe(string name, string args, string text, ref int i, List<Seg> segs)
    {
        switch (name)
        {
            case "set":
            case "put":
                foreach (var part in args.Split(','))
                {
                    var m = Regex.Match(part, @"\$(\w+)\s+to\s+(.+)", RegexOptions.IgnoreCase);
                    var into = Regex.Match(part, @"(.+?)\s+into\s+\$(\w+)", RegexOptions.IgnoreCase);
                    if (m.Success) segs.Add(new SetSeg(m.Groups[1].Value, "=", m.Groups[2].Value.Trim()));
                    else if (into.Success) segs.Add(new SetSeg(into.Groups[2].Value, "=", into.Groups[1].Value.Trim()));
                }
                break;
            case "link-goto":
            case "click-goto":
            {
                var parts = Regex.Matches(args, @"""([^""]*)""|'([^']*)'").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
                if (parts.Count >= 1) segs.Add(new LinkSeg(parts[0], parts.Count > 1 ? parts[1] : parts[0]));
                break;
            }
            case "goto":
                segs.Add(new GotoSeg(Unquote(args)));
                break;
            case "link":
            case "link-reveal":
            {
                // (link: "text")[(goto: "target")] is a link; other link hooks just show their text.
                var hook = Hook(text, ref i) ?? "";
                var go = Regex.Match(hook, @"\(goto:\s*[""']([^""']+)[""']\s*\)");
                if (go.Success) segs.Add(new LinkSeg(Unquote(args), go.Groups[1].Value));
                else segs.AddRange(Parse(hook));
                break;
            }
            case "if":
            case "unless":
            {
                var branches = new List<(string, List<Seg>)>();
                var body = Hook(text, ref i) ?? "";
                branches.Add((name == "unless" ? $"not ({args})" : args, Parse(body)));
                List<Seg>? elseBody = null;
                while (true)
                {
                    var next = Regex.Match(text[i..], @"^\s*\((else-if|elseif|else):\s*([^)]*)\)");
                    if (!next.Success) break;
                    int j = i + next.Length;
                    var h = Hook(text, ref j);
                    if (h == null) break;
                    i = j;
                    if (next.Groups[1].Value == "else") { elseBody = Parse(h); break; }
                    branches.Add((next.Groups[2].Value, Parse(h)));
                }
                segs.Add(new IfSeg(branches, elseBody));
                break;
            }
            case "print":
                segs.Add(new TextSeg(Regex.Match(args, @"^\$(\w+)$") is { Success: true } v ? "{var:" + Var(v.Groups[1].Value) + "}" : Unquote(args)));
                break;
            default:
                // Formatting and other macros: keep a hook's text, drop the macro.
                Skip($"Harlowe ({name}:)");
                if (Hook(text, ref i) is { } other) segs.AddRange(Parse(other));
                break;
        }
    }

    private void SugarCube(string inner, string text, ref int i, List<Seg> segs)
    {
        var name = Regex.Match(inner, @"^\w+").Value.ToLowerInvariant();
        var args = inner[name.Length..].Trim();
        switch (name)
        {
            case "set":
                foreach (var part in args.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var m = Regex.Match(part, @"\$(\w+)\s*(to|=|\+=|-=)\s*(.+)");
                    if (m.Success) segs.Add(new SetSeg(m.Groups[1].Value, m.Groups[2].Value == "to" ? "=" : m.Groups[2].Value, m.Groups[3].Value.Trim()));
                }
                break;
            case "goto":
                segs.Add(new GotoSeg(Unquote(Regex.Match(args, @"\[\[(.*?)\]\]") is { Success: true } g ? g.Groups[1].Value : args)));
                break;
            case "link":
            case "button":
            {
                var quoted = Regex.Matches(args, @"""([^""]*)""|'([^']*)'").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
                var bracket = Regex.Match(args, @"\[\[(.*?)\]\]");
                if (bracket.Success) segs.Add(ParseLink(bracket.Groups[1].Value));
                else if (quoted.Count >= 2) segs.Add(new LinkSeg(quoted[0], quoted[1]));
                // A <<link>> with a body is a container: skip to its end.
                int close = text.IndexOf("<</" + name + ">>", i, StringComparison.OrdinalIgnoreCase);
                if (quoted.Count < 2 && !bracket.Success && close > 0) { segs.AddRange(Parse(text[i..close])); i = close + name.Length + 5; }
                else if (close > 0 && close - i < 2000 && !text[i..close].Contains("<<" + name, StringComparison.Ordinal)) i = close + name.Length + 5;
                break;
            }
            case "if":
            {
                var branches = new List<(string, List<Seg>)>();
                List<Seg>? elseBody = null;
                string condition = args;
                int depth = 0, start = i;
                // Find <<elseif>>, <<else>> and the matching <</if>>.
                for (int k = i; k < text.Length; k++)
                {
                    if (text.AsSpan(k).StartsWith("<<if", StringComparison.OrdinalIgnoreCase)) { depth++; continue; }
                    if (text.AsSpan(k).StartsWith("<</if>>", StringComparison.OrdinalIgnoreCase))
                    {
                        if (depth > 0) { depth--; continue; }
                        if (condition == "\u0001") elseBody = Parse(text[start..k]); else branches.Add((condition, Parse(text[start..k])));
                        i = k + 7;
                        break;
                    }
                    if (depth == 0 && text.AsSpan(k).StartsWith("<<else", StringComparison.OrdinalIgnoreCase))
                    {
                        int end = text.IndexOf(">>", k, StringComparison.Ordinal);
                        branches.Add((condition, Parse(text[start..k])));
                        var tag = text[(k + 2)..end].Trim();
                        condition = tag.StartsWith("elseif", StringComparison.OrdinalIgnoreCase) ? tag[6..].Trim() : tag.StartsWith("else if", StringComparison.OrdinalIgnoreCase) ? tag[7..].Trim() : "\u0001";
                        start = end + 2;
                        k = end + 1;
                    }
                }
                segs.Add(new IfSeg(branches, elseBody));
                break;
            }
            case "print":
            case "=":
                segs.Add(new TextSeg(Regex.Match(args, @"^\$(\w+)$") is { Success: true } v ? "{var:" + Var(v.Groups[1].Value) + "}" : Unquote(args)));
                break;
            default:
                Skip($"SugarCube <<{name}>>");
                break;
        }
    }

    // ================================================================= passages

    private void Passage(TwinePassage p)
    {
        var id = ids[p.Name];
        var room = new Room { Id = id, Name = p.Name };
        var segs = Parse(p.Text);
        // Variables and jumps happen as the passage is shown: before it is described.
        var enter = new Trigger { Id = $"{id}_enter", Name = $"{p.Name}: on arrival", Event = TriggerEvent.BeforeEnterRoom, RoomId = id, StopsCommand = false };
        int choice = 0;
        var prose = new StringBuilder();
        var conditionals = new List<IfSeg>();

        foreach (var seg in segs)
        {
            switch (seg)
            {
                case TextSeg t: prose.Append(t.Text); break;
                case LinkSeg l:
                    prose.Append(l.Text);
                    AddChoice(room, ref choice, l, null);
                    break;
                case SetSeg set:
                    if (SetAction(set) is { } act) enter.Actions.Add(act);
                    break;
                case GotoSeg g when ids.TryGetValue(g.Target, out var target):
                    enter.Actions.Add(new GameAction(ActionType.GoTo, target));
                    break;
                case ImageSeg img:
                    if (Image(img.Source) is { } pic) room.PictureId ??= pic;
                    break;
                case IfSeg cond:
                    conditionals.Add(cond);
                    break;
            }
        }
        // Conditional text is shown after the description, so its choices are numbered after the passage's own.
        foreach (var cond in conditionals) Conditional(room, cond, ref choice);
        room.Description = Clean(prose.ToString()) + (room.Description.Length > 0 ? "\n" + room.Description : "");
        if (enter.Actions.Count > 0) a.Triggers.Add(enter);
        a.Rooms.Add(room);
    }

    private void AddChoice(Room room, ref int choice, LinkSeg link, List<Condition>? conditions)
    {
        if (!ids.TryGetValue(link.Target, out var target)) { Skip("Links to passages that don't exist"); return; }
        if (choice >= Choices.Max) { Skip("Choices beyond 20 in one passage"); return; }
        choice++;
        if (conditions == null)
        {
            Choices.Add(a, room, choice, target, Clean(link.Text));
            return;
        }
        // A link inside conditional text: its exit has the same conditions (listed by the conditional trigger).
        room.Exits.Add(new Exit { Direction = Choices.Direction(choice), TargetRoomId = target, Hidden = true, Conditions = conditions.Select(Copy).ToList() });
        a.Vocabulary.Directions[choice.ToString()] = Choices.Direction(choice);
        a.Vocabulary.Directions[Choices.Direction(choice)] = Choices.Direction(choice);
    }

    private static Condition Copy(Condition c) => new(c.Type, c.A, c.N, c.B, c.Negate);

    /// <summary>Conditional text: an after-description trigger per branch, guarded so only one branch shows.</summary>
    private void Conditional(Room room, IfSeg seg, ref int choice)
    {
        var guard = Var($"{room.Id}_if{++nextTrigger}");
        var reset = new Trigger { Id = $"{room.Id}_if{nextTrigger}", Name = $"{room.Name}: conditional text", Event = TriggerEvent.AfterDescribe, RoomId = room.Id, StopsCommand = false };
        reset.Actions.Add(new GameAction(ActionType.SetVar, guard, 0));
        a.Triggers.Add(reset);
        var branches = seg.Branches.Select(b => (Conditions: ConditionOf(b.Condition), b.Body)).ToList();
        if (seg.Else != null) branches.Add((new List<List<Condition>> { new() }, seg.Else));
        foreach (var (dnf, body) in branches)
        {
            if (dnf == null) { Skip("Conditions that can't be converted"); continue; }
            foreach (var term in dnf)
            {
                var t = new Trigger { Id = $"{room.Id}_if{nextTrigger}_{reset.Actions.Count}", Name = $"{room.Name}: conditional text", Event = TriggerEvent.AfterDescribe, RoomId = room.Id, StopsCommand = false };
                t.Conditions.Add(new Condition(ConditionType.VarEquals, guard, 0));
                t.Conditions.AddRange(term);
                t.Actions.Add(new GameAction(ActionType.SetVar, guard, 1));
                var text = new StringBuilder();
                var choiceLines = new StringBuilder();
                foreach (var s in body)
                {
                    switch (s)
                    {
                        case TextSeg ts: text.Append(ts.Text); break;
                        case LinkSeg l:
                            text.Append(l.Text);
                            int before = choice;
                            AddChoice(room, ref choice, l, term);
                            if (choice > before) choiceLines.Append($"\n{choice}. {Clean(l.Text)}");
                            break;
                        case SetSeg set when SetAction(set) is { } act: t.Actions.Add(act); break;
                        case GotoSeg g when ids.TryGetValue(g.Target, out var target): t.Actions.Add(new GameAction(ActionType.GoTo, target)); break;
                        case IfSeg: Skip("Conditional text inside conditional text"); break;
                    }
                }
                var clean = (Clean(text.ToString()) + choiceLines).Trim();
                if (clean.Length > 0) t.Actions.Insert(1, GameAction.Say(clean));
                a.Triggers.Add(t);
                reset.Actions.Add(new GameAction(ActionType.RunTrigger, t.Id));
                t.Event = TriggerEvent.Subroutine;
            }
        }
    }

    // ================================================================= variables and conditions

    private string Var(string name)
    {
        var v = "tw_" + Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_]", "_");
        variables.Add(v);
        return v;
    }

    private static int? Number(string value) => value.Trim() switch
    {
        "true" => 1,
        "false" => 0,
        var s when int.TryParse(s, out var n) => n,
        _ => null,
    };

    private GameAction? SetAction(SetSeg set)
    {
        var v = Var(set.Variable);
        var value = set.Value.Trim();
        // $x to it + 1 / $x to $x + 1 / $x += 1
        var add = Regex.Match(value, @"^(?:it|\$" + Regex.Escape(set.Variable) + @")\s*([+-])\s*(\d+)$");
        if (set.Op is "+=" or "-=" && Number(value) is { } d) return new GameAction(ActionType.AddVar, v, set.Op == "+=" ? d : -d);
        if (add.Success) return new GameAction(ActionType.AddVar, v, (add.Groups[1].Value == "+" ? 1 : -1) * int.Parse(add.Groups[2].Value));
        if (Number(value) is { } n) return new GameAction(ActionType.SetVar, v, n);
        if (Regex.Match(value, @"^\$(\w+)$") is { Success: true } other) return new GameAction(ActionType.CopyVar, v, b: Var(other.Groups[1].Value));
        Skip("Variables set to text or calculations");
        return null;
    }

    /// <summary>Harlowe / SugarCube conditions as alternatives of AND-ed conditions, or null.</summary>
    private List<List<Condition>>? ConditionOf(string expr)
    {
        expr = expr.Trim();
        if (expr.StartsWith("not (", StringComparison.Ordinal) && expr.EndsWith(')'))
        {
            var inner = ConditionOf(expr[5..^1]);
            if (inner is { Count: 1 } && inner[0].Count == 1) { var c = inner[0][0]; c.Negate = !c.Negate; return inner; }
            return null;
        }
        var ors = Regex.Split(expr, @"\s+(?:or|\|\|)\s+");
        if (ors.Length > 1)
        {
            var all = new List<List<Condition>>();
            foreach (var o in ors) { var c = ConditionOf(o); if (c == null) return null; all.AddRange(c); }
            return all;
        }
        var ands = Regex.Split(expr, @"\s+(?:and|&&)\s+");
        var term = new List<Condition>();
        foreach (var part in ands)
        {
            if (Atom(part.Trim()) is not { } c) return null;
            term.Add(c);
        }
        return new List<List<Condition>> { term };
    }

    private Condition? Atom(string s)
    {
        bool negate = false;
        if (s.StartsWith("not ", StringComparison.Ordinal) || s.StartsWith('!')) { negate = true; s = s.TrimStart('!').Replace("not ", "").Trim(); }
        var m = Regex.Match(s, @"^\$(\w+)\s*(is not|isnot|neq|!==|!=|is|eq|===|==|gte|>=|lte|<=|gt|>|lt|<)\s*(.+)$");
        if (m.Success)
        {
            var v = Var(m.Groups[1].Value);
            var rhs = m.Groups[3].Value.Trim();
            if (Number(rhs) is not { } n)
                return Regex.Match(rhs, @"^\$(\w+)$") is { Success: true } other && m.Groups[2].Value is "is" or "eq" or "===" or "=="
                    ? new Condition(ConditionType.VarEqualsVar, v, b: Var(other.Groups[1].Value), negate: negate) : null;
            return m.Groups[2].Value switch
            {
                "is not" or "isnot" or "neq" or "!==" or "!=" => new Condition(ConditionType.VarEquals, v, n, negate: !negate),
                "is" or "eq" or "===" or "==" => new Condition(ConditionType.VarEquals, v, n, negate: negate),
                "gt" or ">" => new Condition(ConditionType.VarGreater, v, n, negate: negate),
                "lt" or "<" => new Condition(ConditionType.VarLess, v, n, negate: negate),
                "gte" or ">=" => new Condition(ConditionType.VarGreater, v, n - 1, negate: negate),
                _ => new Condition(ConditionType.VarLess, v, n + 1, negate: negate),
            };
        }
        var truthy = Regex.Match(s, @"^\$(\w+)$");
        if (truthy.Success) return new Condition(ConditionType.VarEquals, Var(truthy.Groups[1].Value), 0, negate: !negate);
        // (visited: "Name") / visited("Name") / (history:) contains "Name"
        var visited = Regex.Match(s, @"visited\(\s*[""']([^""']+)[""']\s*\)|\(visited:\s*[""']([^""']+)[""']\s*\)|\(history:\)\s+contains\s+[""']([^""']+)[""']");
        if (visited.Success)
        {
            var name = new[] { visited.Groups[1], visited.Groups[2], visited.Groups[3] }.First(g => g.Success).Value;
            return ids.TryGetValue(name, out var room) ? new Condition(ConditionType.RoomVisited, room, negate: negate) : null;
        }
        return null;
    }

    // ================================================================= text and images

    private static string Clean(string text)
    {
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "");
        text = Regex.Replace(text, @"''(.*?)''|//(.*?)//|\*\*(.*?)\*\*|~~(.*?)~~|\^\^(.*?)\^\^|__(.*?)__", m => m.Groups.Values.Skip(1).First(g => g.Success).Value);
        text = Regex.Replace(text, @"@@[^;]*;(.*?)@@", "$1");
        text = Regex.Replace(text, @"\|\w+>|<\w+\|", "");           // Harlowe hook names
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private readonly Dictionary<string, string> pictures = new();

    private string? Image(string source)
    {
        if (pictures.TryGetValue(source, out var existing)) return existing;
        byte[]? bytes = null;
        string ext = "png";
        var data = Regex.Match(source, @"^data:image/(\w+);base64,(.+)$", RegexOptions.Singleline);
        if (data.Success)
        {
            try { bytes = Convert.FromBase64String(data.Groups[2].Value.Trim()); ext = data.Groups[1].Value.Replace("jpeg", "jpg"); }
            catch (FormatException) { }
        }
        else if (!Regex.IsMatch(source, @"^[a-z]+://", RegexOptions.IgnoreCase))
        {
            var dir = Path.GetDirectoryName(fileName);
            var path = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, source.Replace('/', Path.DirectorySeparatorChar));
            if (path != null && File.Exists(path)) { bytes = File.ReadAllBytes(path); ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(); }
        }
        if (bytes == null) { Skip("Images on the web or missing files"); return null; }
        var (w, h) = Blorb.ImageSize(bytes);
        if (w <= 0 || h <= 0) { Skip("Images in formats other than PNG, JPEG and GIF"); return null; }
        var id = $"pic{pictures.Count + 1}";
        var asset = $"images/{id}.{ext}";
        a.Assets[asset] = bytes;
        int pw = Math.Min(w, 640), ph = (int)((long)h * pw / w);
        a.Pictures.Add(new Picture { Id = id, Name = Path.GetFileNameWithoutExtension(source.Length > 60 ? id : source), Width = pw, Height = ph, RenderMode = PictureRenderMode.Smooth, BitmapAsset = asset, Palette = Palettes.Extended.ToList() });
        pictures[source] = id;
        return id;
    }
}
