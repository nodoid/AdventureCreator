namespace AdventureSystem.Core.Model;

/// <summary>When a trigger is considered by the engine.</summary>
public enum TriggerEvent
{
    /// <summary>Checked when a command is entered, before the built-in behaviour (like PAWS/Quill response tables).</summary>
    BeforeCommand,
    /// <summary>Checked after the built-in behaviour of a command succeeded.</summary>
    AfterCommand,
    /// <summary>Every turn, after the command (like PAWS process tables).</summary>
    EveryTurn,
    /// <summary>Once, at the start of the game.</summary>
    GameStart,
    /// <summary>When the player enters a room (optionally restricted by <see cref="Trigger.RoomId"/>).</summary>
    EnterRoom,
    /// <summary>When the player leaves a room.</summary>
    LeaveRoom,
    /// <summary>When the given turn number is reached (<see cref="Trigger.Turn"/>), or every <see cref="Trigger.Interval"/> turns.</summary>
    Timer,
    ItemTaken,
    ItemDropped,
    PuzzleSolved,
    /// <summary>Never fired automatically: run with the <see cref="ActionType.RunTrigger"/> action.</summary>
    Subroutine,
    /// <summary>When the command is not understood by the parser or no handler applies.</summary>
    Unhandled,
    /// <summary>Right after a room description has been printed (PAWS Process 1).</summary>
    AfterDescribe,
    /// <summary>An NPC (Subject, or any) arrives in the player's room.</summary>
    NpcArrives,
    /// <summary>An NPC (Subject, or any) leaves the player's room.</summary>
    NpcLeaves,
    /// <summary>An NPC (Subject) is defeated in combat.</summary>
    NpcDefeated,
    /// <summary>The player gives an item to a character (Subject = character, Noun1 = item).</summary>
    ItemGiven,
    /// <summary>The player has been hurt (by an NPC, a trap or HurtPlayer).</summary>
    PlayerHurt,
    /// <summary>The player arrives in a room (optionally restricted by <see cref="Trigger.RoomId"/>), before it is described.</summary>
    BeforeEnterRoom,
}

/// <summary>
/// An event handler: when the event occurs and the pattern and all conditions match, the actions run in order.
/// Command patterns use verb ids (see <see cref="Vocabulary"/>) and item ids or words for nouns.
/// "*" matches anything; "-" requires the slot to be empty; null/empty ignores the slot.
/// </summary>
public sealed class Trigger
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TriggerEvent Event { get; set; } = TriggerEvent.BeforeCommand;
    public bool Enabled { get; set; } = true;
    public bool OnceOnly { get; set; }
    /// <summary>Higher runs first. Triggers with equal priority run in list order.</summary>
    public int Priority { get; set; }

    public string? Verb { get; set; }
    public string? Noun1 { get; set; }
    public string? Noun2 { get; set; }
    public string? Adverb { get; set; }
    public string? Preposition { get; set; }
    /// <summary>Restrict to one room (or the room entered/left for room events).</summary>
    public string? RoomId { get; set; }
    /// <summary>Item for ItemTaken/ItemDropped, or puzzle id for PuzzleSolved.</summary>
    public string? Subject { get; set; }
    public int Turn { get; set; }
    public int Interval { get; set; }

    public List<Condition> Conditions { get; set; } = new();
    public List<GameAction> Actions { get; set; } = new();

    /// <summary>
    /// For command triggers: if true (default) the command is considered handled when this trigger fires,
    /// unless an action explicitly requests <see cref="ActionType.Continue"/>.
    /// </summary>
    public bool StopsCommand { get; set; } = true;

    /// <summary>
    /// Optional table name. With <see cref="GameSettings.LegacyTableSemantics"/>, a Done action ends the remaining triggers
    /// of the same group only (like DONE ending one PAWS process table).
    /// </summary>
    public string? Group { get; set; }

    public string Notes { get; set; } = "";
    public override string ToString()
    {
        var pattern = Event is TriggerEvent.BeforeCommand or TriggerEvent.AfterCommand
            ? $" [{Verb ?? "*"} {Noun1} {Noun2}]".TrimEnd()
            : "";
        return (string.IsNullOrEmpty(Name) ? Id : Name) + $" ({Event}{pattern})";
    }
}

public enum ConditionType
{
    Always,
    PlayerIn,          // A = room
    ItemCarried,       // A = item (held or worn)
    ItemWorn,          // A = item
    ItemPresent,       // A = item (visible to player: here, carried or in an open container here)
    ItemIn,            // A = item, B = location (room/item/@carried/@worn/@here or empty for nowhere)
    ItemExists,        // A = item (not nowhere)
    ItemOpen,          // A = item
    ItemLocked,        // A = item
    ItemLit,           // A = item
    VarEquals,         // A = variable, N
    VarGreater,        // A = variable, N
    VarLess,           // A = variable, N
    VarEqualsVar,      // A, B variables: A == B + N (N is an optional offset, usually 0)
    VarGreaterVar,     // A, B variables: A > B + N
    VarLessVar,        // A, B variables: A < B + N
    Chance,            // N = percent
    TurnsAtLeast,      // N
    ScoreAtLeast,      // N
    RoomVisited,       // A = room
    PuzzleSolved,      // A = puzzle
    AdverbUsed,        // A = adverb (e.g. "quietly") – any adverb if empty
    AdjectiveUsed,     // A = adjective typed by the player
    WordUsed,          // A = any word appearing in the command
    PrepositionIs,     // A
    Noun1Is,           // A = item id or word
    Noun2Is,           // A = item id or word
    CarriedCountAtLeast, // N
    IsDark,            // current room is dark with no light source
    ExitOpen,          // A = room, B = direction
    TriggerFired,      // A = trigger id (has fired at least once)
    NpcFollowing,      // A = npc
    NpcHostile,        // A = npc
    NpcDefeated,       // A = npc
    NpcIn,             // A = npc, B = room (or @here)
    NpcHasItem,        // A = npc, B = item
    HealthAtLeast,     // N
    RoomHasFlag,       // A = room (or @here / @eventroom), B = flag name
    RoomFlooded,       // A = room
    RoomTrapped,       // A = room
    EventHappened,     // A = random event id (has happened at least once)
}

public sealed class Condition
{
    public ConditionType Type { get; set; }
    public bool Negate { get; set; }
    public string? A { get; set; }
    public string? B { get; set; }
    public int N { get; set; }

    public Condition() { }
    public Condition(ConditionType type, string? a = null, int n = 0, string? b = null, bool negate = false)
    {
        Type = type; A = a; N = n; B = b; Negate = negate;
    }

    public override string ToString()
    {
        var s = Type switch
        {
            ConditionType.Always => "always",
            ConditionType.VarEquals => $"{A} == {N}",
            ConditionType.VarGreater => $"{A} > {N}",
            ConditionType.VarLess => $"{A} < {N}",
            ConditionType.VarEqualsVar => $"{A} == {B}{Offset(N)}",
            ConditionType.VarGreaterVar => $"{A} > {B}{Offset(N)}",
            ConditionType.VarLessVar => $"{A} < {B}{Offset(N)}",
            ConditionType.Chance => $"chance {N}%",
            ConditionType.ItemIn => $"{A} in {(string.IsNullOrEmpty(B) ? "nowhere" : B)}",
            _ => $"{Type} {A} {B}{(N != 0 ? " " + N : "")}".TrimEnd(),
        };
        return Negate ? "NOT " + s : s;

        static string Offset(int n) => n > 0 ? $" + {n}" : n < 0 ? $" - {-n}" : "";
    }
}

public enum ActionType
{
    Message,          // Text (supports {placeholders}); N = 1 suppresses the trailing line break
    Look,             // redescribe the room
    GoTo,             // A = room
    MoveItem,         // A = item, B = location
    TakeItem,         // A = item – with the usual checks (weight, count)
    DropItem,         // A = item
    DropAll,          // drop everything carried (not worn) in the current room
    WearItem,         // A = item
    UnwearItem,       // A = item
    DestroyItem,      // A = item
    CreateItem,       // A = item, B = optional location (default: the current room, or @eventroom in random events)
    SwapItems,        // A, B items exchange locations
    SetOpen,          // A = item, N = 0/1
    SetLocked,        // A = item, N = 0/1
    SetLit,           // A = item, N = 0/1
    SetVar,           // A = variable, N
    AddVar,           // A = variable, N (may be negative)
    CopyVar,          // A = destination, B = source
    RandomVar,        // A = variable, N = max (1..N)
    AwardScore,       // N points, A = optional unique key so the award is given once
    SolvePuzzle,      // A = puzzle
    PlaySound,        // A = sound, N = 1 to loop
    StopSound,
    ShowPicture,      // A = picture (empty = room picture); N = 1 draws it over the picture shown (8-bit games draw frames that way)
    ClearScreen,
    Inventory,
    ShowScore,
    ShowTurns,
    Pause,            // N = milliseconds (hint for players)
    Win,              // Text
    Lose,             // Text
    Quit,
    Save,
    Restore,
    Restart,
    /// <summary>Stop executing this trigger's remaining actions and mark the command handled.</summary>
    Done,
    /// <summary>Print the "OK" message and stop.</summary>
    Ok,
    /// <summary>Allow the built-in behaviour to run after this trigger.</summary>
    Continue,
    SetExit,          // A = room, B = direction, Text = target room (empty removes the exit)
    SetRoomDescription, // A = room, Text
    SetItemDescription, // A = item, Text
    EnableTrigger,    // A = trigger
    DisableTrigger,   // A = trigger
    /// <summary>A = name: runs every Subroutine trigger whose Id or Name equals A, in list order (a PAWS process table).</summary>
    RunTrigger,
    SetDark,          // A = room, N = 0/1
    SetPlayerAlias,   // unused placeholder for future versions
    Beep,
    /// <summary>A = npc, B = Movement|Hostile|Following|Blocking|Active, N = 1/0, Text = movement mode name.</summary>
    SetNpc,
    /// <summary>
    /// Sends an NPC walking to a room (A = npc, B = room): it travels through real exits, one room per turn, and gives up if
    /// there is no route. Use MoveItem to teleport it instead.
    /// </summary>
    NpcGoTo,
    /// <summary>The NPC says Text (prefixed with its name) if the player is with it. A = npc.</summary>
    NpcSay,
    HurtPlayer,       // N = damage, Text = message
    HealPlayer,       // N = amount (0 = fully)
    SetRoomFlag,      // A = room, B = flag, N = 1/0
    Flood,            // A = room, N = 1 flood / 0 drain
    SetTrap,          // A = room, N = damage (-1 = deadly), Text = message when sprung, B = message when found
    ClearTrap,        // A = room
    RunRandomEvent,   // A = random event id: makes it happen now (ignoring chance and cooldown)
}

public sealed class GameAction
{
    public ActionType Type { get; set; }
    public string? A { get; set; }
    public string? B { get; set; }
    public int N { get; set; }
    public string? Text { get; set; }

    public GameAction() { }
    public GameAction(ActionType type, string? a = null, int n = 0, string? b = null, string? text = null)
    {
        Type = type; A = a; N = n; B = b; Text = text;
    }

    public static GameAction Say(string text) => new(ActionType.Message, text: text);

    public override string ToString() => Type switch
    {
        ActionType.Message => $"say \"{Truncate(Text)}\"",
        ActionType.SetVar => $"{A} = {N}",
        ActionType.AddVar => $"{A} += {N}",
        ActionType.AwardScore => $"score +{N}",
        ActionType.MoveItem => $"move {A} to {(string.IsNullOrEmpty(B) ? "nowhere" : B)}",
        ActionType.Win or ActionType.Lose => $"{Type} \"{Truncate(Text)}\"",
        _ => $"{Type} {A} {B}{(N != 0 ? " " + N : "")}".TrimEnd(),
    };

    private static string Truncate(string? s) => s is null ? "" : s.Length > 40 ? s[..40] + "…" : s;
}
