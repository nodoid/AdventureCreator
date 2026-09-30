namespace AdventureSystem.Core.Model;

/// <summary>Where a random event may take place.</summary>
public enum EventLocation
{
    /// <summary>Any room (or any of <see cref="RandomEvent.Rooms"/> if given).</summary>
    Anywhere,
    /// <summary>The player's current room.</summary>
    PlayerRoom,
    /// <summary>Any room except the player's (things happening out of sight).</summary>
    AwayFromPlayer,
    /// <summary>No particular room (global events such as a storm or a power cut).</summary>
    Global,
}

/// <summary>
/// Something that may happen by chance during play, possibly somewhere the player isn't: a cave-in, a flood, a
/// power cut, a thief moving things, a trap being set. Each turn, every enabled event rolls its <see cref="Chance"/>;
/// if it happens, a room is picked (see <see cref="Where"/>), the <see cref="Conditions"/> are checked, and the
/// <see cref="Actions"/> run with <c>@eventroom</c> meaning the chosen room.
/// </summary>
public sealed class RandomEvent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Percent chance per turn (can be fractional through <see cref="ChancePerThousand"/>).</summary>
    public int Chance { get; set; } = 5;
    /// <summary>Additional chance in thousandths for rare events (Chance 0, ChancePerThousand 5 = 0.5%).</summary>
    public int ChancePerThousand { get; set; }
    public EventLocation Where { get; set; } = EventLocation.Anywhere;
    /// <summary>Candidate rooms (empty = every room).</summary>
    public List<string> Rooms { get; set; } = new();
    /// <summary>Not before this turn.</summary>
    public int EarliestTurn { get; set; }
    /// <summary>Not after this turn (0 = no limit).</summary>
    public int LatestTurn { get; set; }
    /// <summary>At least this many turns between occurrences.</summary>
    public int Cooldown { get; set; } = 5;
    /// <summary>Maximum number of times it can happen (0 = unlimited).</summary>
    public int MaxTimes { get; set; }
    /// <summary>All must be true (evaluated with @eventroom = the candidate room).</summary>
    public List<Condition> Conditions { get; set; } = new();
    public List<GameAction> Actions { get; set; } = new();
    /// <summary>Shown if the player is in the event's room (or for Global events).</summary>
    public string WitnessMessage { get; set; } = "";
    /// <summary>Shown if the player is elsewhere ("You hear a distant rumble."). Empty = the player notices nothing.</summary>
    public string DistantMessage { get; set; } = "";
    public string Notes { get; set; } = "";
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

/// <summary>A trap waiting in a room.</summary>
public sealed class TrapState
{
    /// <summary>Printed when the trap springs.</summary>
    public string Message { get; set; } = "";
    /// <summary>Damage to the player's health (0 = none).</summary>
    public int Damage { get; set; }
    /// <summary>Kills the player outright.</summary>
    public bool Deadly { get; set; }
    /// <summary>Spotted by searching; revealed traps are stepped around safely.</summary>
    public bool Revealed { get; set; }
    /// <summary>Printed when the trap is found by searching.</summary>
    public string RevealMessage { get; set; } = "";
}
