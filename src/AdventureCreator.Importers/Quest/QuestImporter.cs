using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.ZMachine;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Quest;

/// <summary>
/// Quest 5 games (textadventures.co.uk, open source under the MIT licence): .aslx source files and .quest packages
/// (a zip with the game and its pictures and sounds). Rooms, objects, exits, commands, verbs, turn scripts and
/// pictures are converted; scripts become triggers where their statements have equivalents.
/// </summary>
public sealed class QuestImporter : IAdventureImporter
{
    public string Name => "Quest 5 (textadventures.co.uk)";
    public string Id => "quest";
    public IReadOnlyList<string> Extensions => new[] { "aslx", "quest" };

    public bool CanImport(byte[] data, string fileName) => ReadGame(data, fileName, out _, out _) != null;

    public ImportResult Import(byte[] data, string fileName)
    {
        var xml = ReadGame(data, fileName, out var resources, out bool package) ?? throw new InvalidDataException("Not a Quest 5 game.");
        return new QuestConverter(xml, resources, fileName, package).Run();
    }

    /// <summary>The game XML, and its resource files (from the .quest zip, or the .aslx file's folder).</summary>
    internal static XDocument? ReadGame(byte[] data, string fileName, out Dictionary<string, byte[]> resources, out bool package)
    {
        resources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        package = false;
        try
        {
            if (data.Length > 4 && data[0] == 'P' && data[1] == 'K')
            {
                using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                var gameEntry = zip.GetEntry("game.aslx") ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".aslx", StringComparison.OrdinalIgnoreCase));
                if (gameEntry == null) return null;
                XDocument doc;
                using (var s = gameEntry.Open()) doc = XDocument.Load(s);
                if (doc.Root?.Name.LocalName != "asl") return null;
                foreach (var e in zip.Entries.Where(e => e != gameEntry && e.Length > 0))
                {
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    resources[e.FullName] = ms.ToArray();
                }
                package = true;
                return doc;
            }
            var text = Encoding.UTF8.GetString(data).TrimStart('﻿');
            if (!text.TrimStart().StartsWith("<", StringComparison.Ordinal) || !text.Contains("<asl", StringComparison.Ordinal)) return null;
            var d = XDocument.Parse(text);
            if (d.Root?.Name.LocalName != "asl") return null;
            // Pictures and sounds next to the .aslx file (when it was opened from disk).
            var dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir).Where(f => Regex.IsMatch(f, @"\.(png|jpe?g|gif|wav|mp3|ogg|m4a)$", RegexOptions.IgnoreCase)).Take(200))
                    resources[Path.GetFileName(f)] = File.ReadAllBytes(f);
            return d;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed partial class QuestConverter
{
    private readonly XDocument doc;
    private readonly Dictionary<string, byte[]> resources;
    private readonly string fileName;
    private readonly bool package;
    private readonly Adventure a = new();
    private readonly List<string> warnings = new();
    private readonly Dictionary<string, int> skipped = new();

    // Quest names → our ids
    private readonly Dictionary<string, string> roomIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> itemIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> exitLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (List<string> Params, XElement Body)> functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> turnScriptIds = new(StringComparer.OrdinalIgnoreCase);
    private string playerName = "player";
    private int nextSub;

    public QuestConverter(XDocument doc, Dictionary<string, byte[]> resources, string fileName, bool package)
    {
        this.doc = doc;
        this.resources = resources;
        this.fileName = fileName;
        this.package = package;
    }

    private XElement Root => doc.Root!;

    public ImportResult Run()
    {
        var game = Root.Element("game");
        a.Title = game?.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(fileName);
        a.Author = Text(game, "author") ?? "";
        a.Description = Text(game, "description") ?? $"Imported from the Quest 5 game {Path.GetFileName(fileName)}.";
        a.Settings.AutoListExits = true;
        a.Settings.AutoListItems = true;
        Colours(game);
        playerName = Text(game, "pov") ?? Root.Descendants("object").FirstOrDefault(o => Inherits(o, "editor_player"))?.Attribute("name")?.Value ?? "player";

        foreach (var f in Root.Elements("function"))
            if (f.Attribute("name")?.Value is { } n)
                functions[n] = ((f.Attribute("parameters")?.Value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(), f);

        // Gamebooks include the gamebook library, or (older files) are pages with options and no rooms.
        bool gamebook = Root.Elements("include").Any(i => (i.Attribute("ref")?.Value ?? "").Contains("Gamebook", StringComparison.OrdinalIgnoreCase))
                        || (Root.Elements("object").Any(o => o.Element("options") != null) && !Root.Descendants("object").Any(o => Inherits(o, "editor_room") || o.Elements("exit").Any()));
        if (gamebook) GamebookPages();
        else
        {
            AssignIds();
            foreach (var v in Root.Elements("verb")) Verb(v);
            foreach (var o in Root.Elements("object")) Object(o, null);
            foreach (var c in Root.Descendants("command")) Command(c);
            foreach (var t in Root.Descendants("turnscript")) TurnScript(t);
            foreach (var t in Root.Descendants("timer")) Timer(t);
            if (game?.Element("start") is { } start && IsScript(start))
                a.Triggers.Add(ScriptTrigger("q_start", "Game start", TriggerEvent.GameStart, start, null));
        }
        foreach (var w in Root.Elements("walkthrough"))
            a.Notes.Add($"Quest walkthrough \"{w.Attribute("name")?.Value}\": " + string.Join("; ", (w.Element("steps")?.Value ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));

        if (a.Rooms.Count == 0) a.Rooms.Add(new Room { Id = "nowhere", Name = a.Title, Description = "" });
        if (string.IsNullOrEmpty(a.StartRoomId) || a.FindRoom(a.StartRoomId) == null) a.StartRoomId = a.Rooms[0].Id;
        foreach (var v in variables.Order()) a.Variables.Add(new Variable { Name = v, InitialValue = initialValues.TryGetValue(v, out var iv) ? iv : 0, Description = "Quest attribute" });

        foreach (var (what, count) in skipped.OrderByDescending(k => k.Value))
            warnings.Add($"{what}: {count} use{(count == 1 ? "" : "s")} not converted (see the triggers' Notes).");
        a.Notes.Add("Imported from Quest 5. Quest's own library (take, drop, look, containers…) is replaced by Adventure Creator's built-in behaviour; the game's own scripts became triggers.");
        a.Notes.AddRange(warnings);
        var result = new ImportResult(a)
        {
            DetectedFormat = $"Quest 5{(gamebook ? " gamebook" : "")} ({(package ? ".quest package" : ".aslx")}, ASL version {Root.Attribute("version")?.Value ?? "?"})",
        };
        result.Warnings.AddRange(warnings);
        return result;
    }

    // ================================================================= helpers

    private static string? Text(XElement? e, string name) => e?.Element(name) is { } c && !IsScript(c) ? c.Value.Trim() : null;
    private static bool IsScript(XElement e) => e.Attribute("type")?.Value == "script";
    private static bool Inherits(XElement e, string name) => e.Elements("inherit").Any(i => i.Attribute("name")?.Value == name);
    private static bool Flag(XElement e, string name)
    {
        var c = e.Element(name) ?? e.Elements("attr").FirstOrDefault(x => x.Attribute("name")?.Value == name);
        if (c == null) return false;
        var type = c.Attribute("type")?.Value;
        if (type == "boolean") return c.Value.Trim() != "false";
        return type == null && c.Value.Trim() is "" or "true";
    }

    private static string Slug(string name)
    {
        var s = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        return s.Length == 0 ? "q" : char.IsDigit(s[0]) ? "q" + s : s;
    }

    private string Unique(string baseId)
    {
        var id = baseId;
        for (int i = 2; a.FindRoom(id) != null || a.FindItem(id) != null || roomIds.ContainsValue(id) || itemIds.ContainsValue(id); i++) id = $"{baseId}_{i}";
        return id;
    }

    private void Skip(string what) => skipped[what] = skipped.TryGetValue(what, out var n) ? n + 1 : 1;

    private void Colours(XElement? game)
    {
        static string? Css(string? c)
        {
            if (string.IsNullOrWhiteSpace(c)) return null;
            c = c.Trim();
            if (Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$")) return c;
            if (Regex.IsMatch(c, "^#[0-9a-fA-F]{3}$")) return "#" + string.Concat(c[1..].Select(ch => $"{ch}{ch}"));
            var named = System.Drawing.Color.FromName(c);
            return named.IsKnownColor ? $"#{named.R:X2}{named.G:X2}{named.B:X2}" : null;
        }
        if (Css(Text(game, "defaultforeground")) is { } fg) a.Settings.TextColor = fg;
        if (Css(Text(game, "defaultbackground")) is { } bg) a.Settings.BackgroundColor = bg;
    }

    // ================================================================= rooms and objects

    private static bool IsRoom(XElement o, XElement? parent) =>
        parent == null && !Inherits(o, "surface") && !Inherits(o, "container") && !Inherits(o, "container_open") && !Inherits(o, "container_closed")
        && (Inherits(o, "editor_room") || Flag(o, "isroom") || o.Elements("exit").Any() || o.Elements("object").Any());

    private void AssignIds()
    {
        foreach (var o in Root.Descendants("object"))
        {
            var name = o.Attribute("name")?.Value;
            if (name == null) continue;
            var parent = o.Parent?.Name.LocalName == "object" ? o.Parent : null;
            if (IsRoom(o, parent)) roomIds[name] = Unique(Slug(name));
            else if (!string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase)) itemIds[name] = Unique(Slug(name));
        }
    }

    private void Object(XElement o, XElement? parent)
    {
        var name = o.Attribute("name")?.Value;
        if (name == null) return;
        if (string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase))
        {
            if (parent?.Attribute("name")?.Value is { } start && roomIds.TryGetValue(start, out var sid)) a.StartRoomId = sid;
            foreach (var c in o.Elements("object")) Object(c, o);
            return;
        }
        if (roomIds.TryGetValue(name, out var roomId)) Room(o, name, roomId);
        else Item(o, name, parent);
        foreach (var c in o.Elements("object")) Object(c, o);
    }

    private void Room(XElement o, string name, string id)
    {
        var room = new Room { Id = id, Name = Text(o, "alias") ?? Capitalise(name), Description = TextProcessor(Text(o, "description") ?? "") };
        room.IsDark = Flag(o, "dark");
        if (o.Element("description") is { } d && IsScript(d))
            a.Triggers.Add(ScriptTrigger($"{id}_describe", $"{room.Name}: description", TriggerEvent.AfterDescribe, d, o, roomId: id));
        if (Text(o, "picture") is { } pic && PictureFor(pic) is { } p) room.PictureId = p;
        foreach (var e in o.Elements("exit")) Exit(room, e, id);
        foreach (var (element, ev, once) in new[] { ("beforefirstenter", TriggerEvent.BeforeEnterRoom, true), ("firstenter", TriggerEvent.EnterRoom, true), ("beforeenter", TriggerEvent.BeforeEnterRoom, false), ("enter", TriggerEvent.EnterRoom, false), ("onexit", TriggerEvent.LeaveRoom, false) })
            if (o.Element(element) is { } s && IsScript(s))
            {
                var t = ScriptTrigger($"{id}_{element}", $"{room.Name}: {element}", ev, s, o, roomId: id);
                t.OnceOnly = once;
                a.Triggers.Add(t);
            }
        Attributes(o, id);
        a.Rooms.Add(room);
    }

    private static readonly Dictionary<string, string> DirectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = "north", ["south"] = "south", ["east"] = "east", ["west"] = "west", ["northeast"] = "northeast", ["northwest"] = "northwest",
        ["southeast"] = "southeast", ["southwest"] = "southwest", ["up"] = "up", ["down"] = "down", ["in"] = "in", ["out"] = "out",
    };

    private void Exit(Room room, XElement e, string roomId)
    {
        var to = e.Attribute("to")?.Value;
        if (to == null || !roomIds.TryGetValue(to, out var target)) return;
        var alias = e.Attribute("alias")?.Value ?? e.Element("alias")?.Value;
        var inherit = e.Elements("inherit").Select(i => i.Attribute("name")?.Value ?? "").FirstOrDefault(n => n.EndsWith("direction"));
        string direction = alias != null && DirectionNames.TryGetValue(alias, out var d) ? d
            : inherit != null ? inherit[..^"direction".Length]
            : alias?.ToLowerInvariant() ?? "out";
        if (!DirectionNames.ContainsKey(direction)) a.Vocabulary.Directions.TryAdd(direction, direction);
        var exit = new Exit { Direction = direction, TargetRoomId = target };
        if (Flag(e, "locked"))
        {
            var v = Var($"{roomId}_{direction}_locked", 1);
            exit.Conditions.Add(new Condition(ConditionType.VarEquals, v, 0));
            if (e.Attribute("name")?.Value is { } exitName) exitLocks[exitName] = v;
            if (Text(e, "lockmessage") is { } msg) exit.BlockedMessage = TextProcessor(msg);
        }
        if (Text(e, "message") is { } travel) exit.TravelMessage = TextProcessor(travel);
        room.Exits.Add(exit);
    }

    private static readonly string[] ContainerTypes = { "container", "container_open", "container_closed", "container_limited", "surface", "container_lockable" };
    private static readonly string[] KnownVerbs = { "push", "pull", "open", "close", "use", "eat", "drink", "read", "speak", "talk", "listen", "smell", "touch", "sit", "lie", "kiss", "hit", "climb", "move", "search", "turn", "wear", "remove", "take", "drop", "give", "buy" };

    private void Item(XElement o, string name, XElement? parent)
    {
        if (!itemIds.TryGetValue(name, out var id)) return;
        if (Inherits(o, "dialoguepage") || Inherits(o, "topic") || Inherits(o, "editor_topic"))
        {
            // Conversation menu pages (ConvLib): not objects in the world.
            Skip("Conversation pages (dialogue menus)");
            itemIds.Remove(name);
            return;
        }
        var parentName = parent?.Attribute("name")?.Value;
        string location = parentName == null ? ""
            : string.Equals(parentName, playerName, StringComparison.OrdinalIgnoreCase) ? Locations.Carried
            : roomIds.TryGetValue(parentName, out var r) ? r : itemIds.TryGetValue(parentName, out var i) ? i : "";
        if (Text(o, "visible") == "false" || (o.Element("visible") is { } vis && vis.Attribute("type")?.Value == "boolean" && vis.Value.Trim() == "false")) location = "";

        var alias = Text(o, "alias");
        var item = new Item
        {
            Id = id, Name = alias ?? name, Location = location,
            Description = o.Element("look") is { } look && !IsScript(look) ? TextProcessor(look.Value.Trim()) : "",
            Portable = Flag(o, "take") || o.Element("take") is { } tk && IsScript(tk),
            Scenery = Flag(o, "scenery"),
            Wearable = Inherits(o, "wearable") || Flag(o, "feature_wearable"),
            Container = ContainerTypes.Any(t => Inherits(o, t)) || Flag(o, "feature_container"),
            Edible = Flag(o, "edible"),
            LightSource = Flag(o, "lightsource"),
            IsCharacter = Inherits(o, "namedmale") || Inherits(o, "namedfemale") || Inherits(o, "male") || Inherits(o, "female") || Inherits(o, "npc_type"),
        };
        if (Inherits(o, "container_closed")) { item.Openable = true; item.IsOpen = false; }
        if (Inherits(o, "container_open")) { item.Openable = true; item.IsOpen = true; }
        if (Inherits(o, "surface")) { item.Supporter = true; item.Container = false; }
        if (Flag(o, "switchable") || Flag(o, "feature_switchable")) { item.Switchable = true; item.IsLit = Flag(o, "switchedon"); }
        if (Text(o, "read") is { } readText) { item.Readable = true; item.ReadText = TextProcessor(readText); }
        foreach (var w in NameWords(name).Concat(NameWords(alias)).Concat(o.Element("alt")?.Elements("value").Select(v => v.Value.Trim().ToLowerInvariant()) ?? Enumerable.Empty<string>()).Distinct())
            if (!item.Nouns.Contains(w)) item.Nouns.Add(w);
        a.Items.Add(item);

        // Verb responses on the object: text becomes the item's response, a script becomes a trigger.
        foreach (var child in o.Elements())
        {
            var verb = child.Name.LocalName;
            if (verb == "attr") verb = child.Attribute("name")?.Value ?? "";
            if (!KnownVerbs.Contains(verb) && !definedVerbs.Contains(verb) && verb != "look") continue;
            var verbId = verb switch { "look" => "examine", "speak" => "talk", _ => verb };
            if (IsScript(child))
            {
                var t = ScriptTrigger($"{id}_{verb}", $"{verb} {item.Name}", TriggerEvent.BeforeCommand, child, o);
                t.Verb = verbId;
                t.Noun1 = id;
                t.Actions.Add(new GameAction(ActionType.Done));
                a.Triggers.Add(t);
            }
            else if (verb != "look" && verb != "take" && verb != "read" && child.Value.Trim().Length > 0 && child.Attribute("type")?.Value is null or "string")
                item.VerbResponses[verbId] = TextProcessor(child.Value.Trim());
        }
        if (o.Elements("attr").FirstOrDefault(x => x.Attribute("name")?.Value == "_initialise_") is { } init && IsScript(init))
            a.Triggers.Add(ScriptTrigger($"{id}_init", $"{item.Name}: initialise", TriggerEvent.GameStart, init, o));
        Attributes(o, id);
    }

    private static IEnumerable<string> NameWords(string? name) =>
        (name ?? "").ToLowerInvariant().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && w is not ("the" or "a" or "an" or "of"));

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Custom int/boolean attributes become variables with their starting values.</summary>
    private void Attributes(XElement o, string id)
    {
        foreach (var c in o.Elements())
        {
            var name = c.Name.LocalName == "attr" ? c.Attribute("name")?.Value : c.Name.LocalName;
            var type = c.Attribute("type")?.Value;
            if (name == null || name.StartsWith("feature_") || name.StartsWith("editor_") || name.StartsWith('_')) continue;
            if (type == "int" && int.TryParse(c.Value.Trim(), out var n)) initialValues[Var($"{id}_{name}")] = n;
            else if (type == "boolean" && name is not ("take" or "scenery" or "visible" or "isroom" or "usedefaultprefix" or "dark" or "locked"))
                initialValues[Var($"{id}_{name}")] = c.Value.Trim() == "false" ? 0 : 1;
        }
    }

    private readonly HashSet<string> variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> initialValues = new(StringComparer.OrdinalIgnoreCase);

    private string Var(string name, int? initial = null)
    {
        var v = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_@]+", "_");
        variables.Add(v);
        if (initial.HasValue) initialValues[v] = initial.Value;
        return v;
    }

    private readonly Dictionary<string, string> pictures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A picture made from an image resource (by file name), or null if the file isn't in the game.</summary>
    private string? PictureFor(string file)
    {
        file = file.Trim();
        if (pictures.TryGetValue(file, out var existing)) return existing;
        var key = resources.Keys.FirstOrDefault(k => string.Equals(Path.GetFileName(k), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
        if (key == null) { Skip("Picture files that aren't in the game package"); return null; }
        var bytes = resources[key];
        var (w, h) = Blorb.ImageSize(bytes);
        if (w <= 0 || h <= 0) return null;
        var id = Unique("pic_" + Slug(Path.GetFileNameWithoutExtension(file)));
        var asset = "images/" + Path.GetFileName(key);
        a.Assets[asset] = bytes;
        // Large photographs are shown scaled down; the picture keeps the image's proportions.
        int pw = Math.Min(w, 640), ph = (int)((long)h * pw / w);
        a.Pictures.Add(new Picture { Id = id, Name = Path.GetFileNameWithoutExtension(file), Width = pw, Height = ph, RenderMode = PictureRenderMode.Smooth, BitmapAsset = asset, Palette = Palettes.Extended.ToList() });
        pictures[file] = id;
        return id;
    }

    private readonly Dictionary<string, string> sounds = new(StringComparer.OrdinalIgnoreCase);

    private string? SoundFor(string file)
    {
        if (sounds.TryGetValue(file, out var existing)) return existing;
        var key = resources.Keys.FirstOrDefault(k => string.Equals(Path.GetFileName(k), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
        if (key == null) { Skip("Sound files that aren't in the game package"); return null; }
        var id = "snd_" + Slug(Path.GetFileNameWithoutExtension(file));
        a.Assets["sounds/" + Path.GetFileName(key)] = resources[key];
        a.Sounds.Add(new SoundAsset { Id = id, Name = Path.GetFileNameWithoutExtension(file), AssetName = "sounds/" + Path.GetFileName(key) });
        sounds[file] = id;
        return id;
    }

    // ================================================================= commands, verbs, turn scripts

    private readonly HashSet<string> definedVerbs = new(StringComparer.OrdinalIgnoreCase);

    private void Verb(XElement v)
    {
        var property = Text(v, "property") ?? v.Attribute("name")?.Value;
        if (property == null) return;
        definedVerbs.Add(property);
        // Verbs the engine already has (push, pull, eat…) keep their built-in behaviour; objects' responses still apply.
        if (BuiltInVerb(property) != null) return;
        var words = Patterns(Text(v, "pattern") ?? property).Select(p => p.Verb).Distinct().ToList();
        var def = new VerbDefinition { Id = property.ToLowerInvariant(), Words = words, Grammar = { "* {noun}" } };
        def.DefaultResponse = TextProcessor(Text(v, "defaulttext") ?? StringsOf(Text(v, "defaultexpression")) ?? "");
        if (!a.Vocabulary.Verbs.Any(x => x.Id == def.Id)) a.Vocabulary.Verbs.Add(def);
    }

    private static string? StringsOf(string? expression)
    {
        if (expression == null) return null;
        var parts = Regex.Matches(expression, "\"([^\"]*)\"").Select(m => m.Groups[1].Value);
        var s = string.Join("{the noun1}", parts);
        return s.Length == 0 ? null : s;
    }

    /// <summary>Quest command patterns ("hang up #object#;hang #object#", or regular expressions) as verb words and grammar lines.</summary>
    private static List<(string Verb, string Grammar)> Patterns(string pattern)
    {
        var list = new List<(string, string)>();
        IEnumerable<string> alternatives;
        if (pattern.TrimStart().StartsWith('^') || pattern.Contains("(?<"))
        {
            alternatives = pattern.Split('|').Select(p => Regex.Replace(p, @"\(\?<(\w+)>[^)]*\)", m => $"#{m.Groups[1].Value}#").Trim('^', '$', ' '));
        }
        else alternatives = pattern.Split(';');
        foreach (var alt in alternatives)
        {
            var words = alt.Trim().TrimStart('>').Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0].StartsWith('#')) continue;
            var grammar = "* " + string.Join(' ', words.Skip(1).Select(w => w.StartsWith('#') ? w.Trim('#') switch
            {
                "object" or "object1" => "{noun}",
                "object2" => "{noun2}",
                _ => "{text}",
            } : w));
            list.Add((words[0].ToLowerInvariant(), grammar.Trim()));
        }
        return list;
    }

    /// <summary>Quest's standard library commands (published .quest files contain the whole library).</summary>
    private static readonly HashSet<string> LibraryCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "alttellto", "ask", "close", "drop", "give", "givesingle", "go", "help", "inventory", "jump", "lie", "listen", "look", "lookat", "lookdir",
        "oops", "open", "put", "quit", "removefrom", "restart", "save", "sit", "sleep", "take", "tell", "tellto", "transcript_off_cmd",
        "transcript_on_cmd", "undo", "use", "useon", "version_cmd", "view_transcript_cmd", "wait", "xyzzy", "speakto", "wear", "remove",
        "switchon", "switchoff", "eat", "drink", "search", "log", "map", "pov_cmd",
    };

    private void Command(XElement c)
    {
        if (c.Attribute("name")?.Value is { } libraryName && LibraryCommands.Contains(libraryName) && c.Parent == Root && package) return;
        var pattern = Text(c, "pattern");
        if (pattern == null || c.Element("script") is not { } script) return;
        var patterns = Patterns(pattern);
        if (patterns.Count == 0) { Skip("Commands with patterns that can't be read"); return; }
        var room = c.Parent?.Name.LocalName == "object" && c.Parent.Attribute("name")?.Value is { } rn && roomIds.TryGetValue(rn, out var rid) ? rid : null;
        var name = c.Attribute("name")?.Value ?? patterns[0].Verb;
        var t = ScriptTrigger(Unique("cmd_" + Slug(name)), $"Command: {pattern}", TriggerEvent.BeforeCommand, script, null, roomId: room, command: true);
        var builtInVerb = patterns.Select(p => BuiltInVerb(p.Verb)).FirstOrDefault(v => v != null);
        if (t.Notes.Length > 0 && builtInVerb != null)
        {
            // Half a command would be worse than the engine's own version of it.
            warnings.Add($"Command \"{name}\" ({pattern}) couldn't be fully converted, so the built-in \"{builtInVerb}\" is used instead.");
            return;
        }
        // Words that are built-in verbs extend those verbs (keeping their grammar); others become new commands.
        foreach (var (verb, grammar) in patterns)
        {
            var id = BuiltInVerb(verb) ?? verb;
            var def = a.Vocabulary.Verbs.FirstOrDefault(v => v.Id == id);
            if (def == null) a.Vocabulary.Verbs.Add(def = new VerbDefinition { Id = id });
            if (!def.Words.Contains(verb) && BuiltInVerb(verb) == null) def.Words.Add(verb);
            if (!def.Grammar.Contains(grammar)) def.Grammar.Add(grammar);
        }
        t.Verb = string.Join('|', patterns.Select(p => p.Verb).Distinct());
        t.Noun1 = patterns.Any(p => p.Grammar.Contains("{noun}")) ? "*" : null;
        if (!patterns.Any(p => p.Grammar.Contains("{noun2}"))) t.Noun2 = "-";
        t.Actions.Add(new GameAction(ActionType.Done));
        a.Triggers.Add(t);
    }

    private static readonly Dictionary<string, string> BuiltInVerbWords = Core.Parsing.BuiltInLexicon.VerbTable
        .Select(row => row.Split('|'))
        .SelectMany(cols => cols[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(w => !w.Contains(' ')).Select(w => (w, cols[0].Trim().TrimEnd('!'))))
        .GroupBy(x => x.w).ToDictionary(g => g.Key, g => g.First().Item2, StringComparer.OrdinalIgnoreCase);

    private static string? BuiltInVerb(string word) => BuiltInVerbWords.TryGetValue(word, out var id) ? id : null;

    private void TurnScript(XElement ts)
    {
        if (ts.Element("script") is not { } script) return;
        var room = ts.Parent?.Name.LocalName == "object" && ts.Parent.Attribute("name")?.Value is { } rn && roomIds.TryGetValue(rn, out var rid) ? rid : null;
        var name = ts.Attribute("name")?.Value ?? $"turnscript{a.Triggers.Count}";
        var t = ScriptTrigger(Unique("ts_" + Slug(name)), $"Turn script {name}", TriggerEvent.EveryTurn, script, null, roomId: room);
        t.Enabled = ts.Element("enabled") != null && Flag(ts, "enabled");
        if (room != null) t.Conditions.Insert(0, new Condition(ConditionType.PlayerIn, room));
        turnScriptIds.Add(name);
        a.Triggers.Add(t);
    }

    private void Timer(XElement tm)
    {
        if (tm.Element("script") is not { } script) return;
        var name = tm.Attribute("name")?.Value ?? $"timer{a.Triggers.Count}";
        var t = ScriptTrigger(Unique("timer_" + Slug(name)), $"Timer {name} (seconds became turns)", TriggerEvent.Timer, script, null);
        t.Interval = int.TryParse(Text(tm, "interval"), out var seconds) ? Math.Max(1, seconds) : 1;
        t.Enabled = Flag(tm, "enabled");
        turnScriptIds.Add(name);
        warnings.Add($"Timer \"{name}\" ran every {t.Interval} seconds in Quest; here it runs every {t.Interval} turns.");
        a.Triggers.Add(t);
    }

    // ================================================================= text

    /// <summary>Quest's text processor commands, simplified: formatting is dropped and conditional text keeps its first choice.</summary>
    private string TextProcessor(string text)
    {
        for (int guard = 0; guard < 20; guard++)
        {
            var next = Regex.Replace(text, @"\{([^{}]*)\}", m =>
            {
                var inner = m.Groups[1].Value;
                int colon = inner.IndexOf(':');
                if (colon < 0) return inner;
                var cmd = inner[..colon].Trim();
                var rest = inner[(colon + 1)..];
                switch (cmd.Split(' ')[0])
                {
                    case "i": case "b": case "u": case "s":
                        return rest;
                    case "colour": case "color": case "back": case "font": case "size": case "popup": case "object": case "exit": case "page": case "command":
                        // {object:name} / {object:name:shown text}: the last part is what is shown.
                        return rest.Contains(':') ? rest[(rest.LastIndexOf(':') + 1)..] : rest;
                    case "once": return rest;
                    case "notfirst": Skip("Text shown only after the first time ({notfirst:…})"); return "";
                    case "either": case "if": case "random":
                    {
                        Skip($"Conditional or random text ({{{cmd.Split(' ')[0]}:…}})");
                        var options = rest.Split(cmd.StartsWith("random") ? ':' : '|');
                        return options[0];
                    }
                    default: return rest;
                }
            });
            if (next == text) break;
            text = next;
        }
        return text.Replace("<br/>", "\n").Replace("<br>", "\n");
    }
}
