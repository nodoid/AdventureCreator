using System.Text.Json.Serialization;

namespace AdventureCreator.Core.Model;

/// <summary>
/// The complete, serialisable definition of an adventure game. Runtime state lives in
/// <see cref="Engine.GameState"/>; this class never changes while a game is being played.
/// </summary>
public sealed class Adventure
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Title { get; set; } = "Untitled Adventure";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string Description { get; set; } = "";

    /// <summary>Text shown when the game starts, before the first room description.</summary>
    public string Introduction { get; set; } = "";
    public string? IntroPictureId { get; set; }
    public string? IntroSoundId { get; set; }

    public string StartRoomId { get; set; } = "";

    public GameSettings Settings { get; set; } = new();
    public List<Room> Rooms { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public List<Puzzle> Puzzles { get; set; } = new();
    public List<Trigger> Triggers { get; set; } = new();
    public List<Variable> Variables { get; set; } = new();
    public List<Picture> Pictures { get; set; } = new();
    public List<SoundAsset> Sounds { get; set; } = new();
    public Vocabulary Vocabulary { get; set; } = new();

    /// <summary>Overrides for the engine's built-in messages, keyed by <see cref="Engine.Msg"/> ids.</summary>
    public Dictionary<string, string> Messages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Free-form notes, e.g. warnings generated while importing a legacy game.</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>Binary assets (bitmap images, audio) keyed by asset name. Stored as separate zip entries in a package.</summary>
    [JsonIgnore]
    public Dictionary<string, byte[]> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Room? FindRoom(string? id) => id is null ? null : Rooms.Find(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
    public Item? FindItem(string? id) => id is null ? null : Items.Find(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
    public Picture? FindPicture(string? id) => id is null ? null : Pictures.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    public SoundAsset? FindSound(string? id) => id is null ? null : Sounds.Find(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    public Puzzle? FindPuzzle(string? id) => id is null ? null : Puzzles.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    public Trigger? FindTrigger(string? id) => id is null ? null : Triggers.Find(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    public Variable? FindVariable(string? name) => name is null ? null : Variables.Find(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Maximum achievable score: explicit setting if non-zero, otherwise derived from puzzles, items and score actions.</summary>
    public int ComputeMaxScore()
    {
        if (Settings.MaxScore > 0) return Settings.MaxScore;
        int total = Puzzles.Sum(p => Math.Max(0, p.Points)) + Items.Sum(i => Math.Max(0, i.ScoreOnTake)) + Rooms.Sum(r => Math.Max(0, r.ScoreOnFirstVisit));
        foreach (var t in Triggers)
            foreach (var a in t.Actions)
                if (a.Type == ActionType.AwardScore && a.N > 0) total += a.N;
        return total;
    }

    /// <summary>Generates an id unique across rooms, items, puzzles, triggers, pictures and sounds.</summary>
    public string NewId(string prefix)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rooms) used.Add(r.Id);
        foreach (var i in Items) used.Add(i.Id);
        foreach (var p in Puzzles) used.Add(p.Id);
        foreach (var t in Triggers) used.Add(t.Id);
        foreach (var p in Pictures) used.Add(p.Id);
        foreach (var s in Sounds) used.Add(s.Id);
        for (int n = 1; ; n++)
        {
            var id = $"{prefix}{n}";
            if (!used.Contains(id)) return id;
        }
    }
}

public sealed class GameSettings
{
    /// <summary>0 = compute automatically.</summary>
    public int MaxScore { get; set; }
    public int MaxCarriedItems { get; set; } = 10;
    /// <summary>0 = unlimited.</summary>
    public int MaxCarriedWeight { get; set; }
    public bool ShowPictures { get; set; } = true;
    public bool AutoListItems { get; set; } = true;
    public bool AutoListExits { get; set; } = true;
    public bool AllowUndo { get; set; } = true;
    /// <summary>Verbose: always print full room descriptions. Otherwise only on first visit.</summary>
    public bool Verbose { get; set; } = true;
    /// <summary>When non-zero only this many leading letters of each word are significant (PAWS/Quill style).</summary>
    public int SignificantLetters { get; set; }
    /// <summary>Correct probable typing mistakes against the vocabulary.</summary>
    public bool SpellingCorrection { get; set; } = true;
    /// <summary>Picture used when a room has no picture of its own (e.g. a title card). Optional.</summary>
    public string? DefaultPictureId { get; set; }
    /// <summary>Text colour / background for players, as #RRGGBB.</summary>
    public string TextColor { get; set; } = "#E8E8E8";
    public string BackgroundColor { get; set; } = "#101018";
    public string FontFamily { get; set; } = "";
    public string Prompt { get; set; } = "> ";

    /// <summary>
    /// PAWS/Quill/GAC table semantics: when any BeforeCommand trigger fired, the command counts as handled even
    /// without an explicit Done, and triggers default to continuing the scan. Set by importers.
    /// </summary>
    public bool LegacyTableSemantics { get; set; }
    /// <summary>If set, the current room is dark whenever this variable is non-zero (Quill/PAWS flag 0).</summary>
    public string? DarknessVariable { get; set; }
    /// <summary>
    /// GAC/Quill style: a movement command that matches an exit of the current room moves the player before any
    /// BeforeCommand trigger is considered.
    /// </summary>
    public bool ExitsBeforeTriggers { get; set; }
    /// <summary>Text shown in the status/title bar of players.</summary>
    public bool ShowStatusBar { get; set; } = true;
}

public sealed class Room
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Optional shorter description used after the first visit when not in verbose mode.</summary>
    public string ShortDescription { get; set; } = "";
    public bool IsDark { get; set; }
    public string? PictureId { get; set; }
    /// <summary>Ambient sound played while in the room.</summary>
    public string? SoundId { get; set; }
    public bool LoopSound { get; set; } = true;
    public List<Exit> Exits { get; set; } = new();
    /// <summary>Points awarded the first time the player enters.</summary>
    public int ScoreOnFirstVisit { get; set; }
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : $"{Name} ({Id})";
}

public sealed class Exit
{
    /// <summary>Canonical direction (north, south, up, in, ...) or any custom word such as "portal".</summary>
    public string Direction { get; set; } = "north";
    public string TargetRoomId { get; set; } = "";
    /// <summary>Door or gate item; the exit is blocked while it is closed.</summary>
    public string? DoorItemId { get; set; }
    /// <summary>Additional conditions that must hold for the exit to be usable.</summary>
    public List<Condition> Conditions { get; set; } = new();
    public string BlockedMessage { get; set; } = "";
    /// <summary>Hidden exits are not listed but can still be used.</summary>
    public bool Hidden { get; set; }
    /// <summary>Message printed when the exit is used successfully.</summary>
    public string TravelMessage { get; set; } = "";
    public override string ToString() => $"{Direction} -> {TargetRoomId}";
}

public static class Locations
{
    public const string Carried = "@carried";
    public const string Worn = "@worn";
    public const string Nowhere = "";
    /// <summary>Pseudo-location used by actions: the player's current room.</summary>
    public const string Here = "@here";
    public static bool IsNowhere(string? loc) => string.IsNullOrEmpty(loc);
}

public sealed class Item
{
    public string Id { get; set; } = "";
    /// <summary>Short name shown in lists, e.g. "a brass lamp" is formed from the article + name.</summary>
    public string Name { get; set; } = "";
    /// <summary>"a", "an", "some", "the" or empty for proper names.</summary>
    public string Article { get; set; } = "a";
    /// <summary>Words that refer to the item (lower case). Multi-word nouns such as "control panel" are allowed.</summary>
    public List<string> Nouns { get; set; } = new();
    public List<string> Adjectives { get; set; } = new();
    public string Description { get; set; } = "";
    /// <summary>Sentence used when listing the item in a room ("A lamp hangs from a hook.").</summary>
    public string RoomDescription { get; set; } = "";
    /// <summary>Room id, container/supporter item id, <see cref="Locations.Carried"/>, <see cref="Locations.Worn"/> or empty for nowhere.</summary>
    public string Location { get; set; } = "";
    public bool Portable { get; set; } = true;
    /// <summary>Scenery is never listed in room descriptions and cannot be taken.</summary>
    public bool Scenery { get; set; }
    public bool Wearable { get; set; }
    public bool Edible { get; set; }
    public bool Drinkable { get; set; }
    public bool Container { get; set; }
    public bool Supporter { get; set; }
    public bool Transparent { get; set; }
    public bool Openable { get; set; }
    public bool IsOpen { get; set; } = true;
    public bool Lockable { get; set; }
    public bool IsLocked { get; set; }
    public string? KeyItemId { get; set; }
    public bool LightSource { get; set; }
    public bool IsLit { get; set; }
    public bool Switchable { get; set; }
    public bool Readable { get; set; }
    public string ReadText { get; set; } = "";
    public int Weight { get; set; } = 1;
    public int Capacity { get; set; } = 10;
    public int ScoreOnTake { get; set; }
    public bool IsCharacter { get; set; }
    public bool Plural { get; set; }
    public string? PictureId { get; set; }
    /// <summary>Conversation topics for characters (ask/tell about).</summary>
    public List<Topic> Topics { get; set; } = new();
    /// <summary>Custom per-verb responses: verb id -> text. A quick alternative to writing a trigger.</summary>
    public Dictionary<string, string> VerbResponses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string PrimaryNoun => Nouns.Count > 0 ? Nouns[0] : Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant() ?? Id;

    public string WithArticle() => string.IsNullOrEmpty(Article) ? Name : $"{Article} {Name}";
    public string WithDefinite() => string.IsNullOrEmpty(Article) ? Name : $"the {Name}";
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : $"{Name} ({Id})";
}

public sealed class Topic
{
    /// <summary>Words that select this topic ("king", "crown", "throne").</summary>
    public List<string> Keywords { get; set; } = new();
    public string Response { get; set; } = "";
    public List<Condition> Conditions { get; set; } = new();
    public List<GameAction> Actions { get; set; } = new();
}

public sealed class Variable
{
    public string Name { get; set; } = "";
    public int InitialValue { get; set; }
    public string Description { get; set; } = "";
    public override string ToString() => $"{Name} = {InitialValue}";
}

public sealed class Puzzle
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Points { get; set; }
    /// <summary>Room(s) where hints are relevant; empty = anywhere.</summary>
    public List<string> HintRoomIds { get; set; } = new();
    /// <summary>Progressive hints, revealed one per HINT request.</summary>
    public List<string> Hints { get; set; } = new();
    /// <summary>All conditions must hold at the end of a turn for the puzzle to be solved.</summary>
    public List<Condition> SolvedWhen { get; set; } = new();
    public List<GameAction> OnSolved { get; set; } = new();
    public string SolvedMessage { get; set; } = "";
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

public sealed class SoundAsset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Name of the entry in <see cref="Adventure.Assets"/> (e.g. "sounds/wind.mp3").</summary>
    public string AssetName { get; set; } = "";
    public double Volume { get; set; } = 1.0;
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}
