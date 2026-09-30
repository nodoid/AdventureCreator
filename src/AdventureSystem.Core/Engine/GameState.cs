using System.Text.Json;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Core.Engine;

/// <summary>Everything that changes while playing. Serialised for save games and undo.</summary>
public sealed class GameState
{
    public string CurrentRoomId { get; set; } = "";
    public int Turns { get; set; }
    public int Score { get; set; }
    public bool GameOver { get; set; }
    public bool Won { get; set; }
    public bool Verbose { get; set; } = true;

    public Dictionary<string, string> ItemLocations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ItemOpen { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ItemLocked { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ItemLit { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ItemDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> VisitedRooms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AwardKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SolvedPuzzles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FiredTriggers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> TriggerEnabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RoomDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> RoomDark { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>"room|direction" -> target room ("" = exit removed).</summary>
    public Dictionary<string, string> ExitOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> HintsShown { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> TakenOnce { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NpcState> Npcs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Health { get; set; }
    public int RoomEnteredTurn { get; set; }
    /// <summary>Z-code stories: the Z-machine's saved state (base 64).</summary>
    public string? ZState { get; set; }
    /// <summary>Room id -> flags such as "flooded".</summary>
    public Dictionary<string, HashSet<string>> RoomFlags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TrapState> Traps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> EventCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> EventLastTurn { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> It { get; set; } = new();
    public List<string> Them { get; set; } = new();
    public string? Him { get; set; }
    public string? Her { get; set; }
    public string? CurrentSoundId { get; set; }
    public string? CurrentPictureId { get; set; }

    private static readonly JsonSerializerOptions options = new() { WriteIndented = false };

    public string Serialize() => JsonSerializer.Serialize(this, options);
    public static GameState Deserialize(string json) => JsonSerializer.Deserialize<GameState>(json, options) ?? new GameState();
    public GameState Clone() => Deserialize(Serialize());

    public static GameState Initial(Adventure adventure)
    {
        var s = new GameState
        {
            CurrentRoomId = !string.IsNullOrEmpty(adventure.StartRoomId) ? adventure.StartRoomId : adventure.Rooms.FirstOrDefault()?.Id ?? "",
            Verbose = adventure.Settings.Verbose,
        };
        foreach (var item in adventure.Items)
        {
            s.ItemLocations[item.Id] = item.Location ?? "";
            s.ItemOpen[item.Id] = item.IsOpen;
            s.ItemLocked[item.Id] = item.IsLocked;
            s.ItemLit[item.Id] = item.IsLit;
        }
        foreach (var v in adventure.Variables) s.Variables[v.Name] = v.InitialValue;
        foreach (var t in adventure.Triggers) s.TriggerEnabled[t.Id] = t.Enabled;
        s.Health = adventure.Settings.PlayerHealth;
        foreach (var npc in adventure.Items.Where(i => i.Npc != null))
            s.Npcs[npc.Id] = new NpcState { Health = npc.Npc!.Health, Following = npc.Npc.Movement == NpcMovement.Follow };
        return s;
    }
}

public enum OutputKind
{
    Text,
    Picture,
    PlaySound,
    StopSound,
    ClearScreen,
    Pause,
    Beep,
    GameOver,
    Quit,
    Restart,
    /// <summary>Location / score changed; hosts refresh their status bar.</summary>
    Status,
    /// <summary>The player typed SAVE without a name and the host shows its own save dialog.</summary>
    SaveRequested,
    /// <summary>The player typed RESTORE without a name and the host shows its own load dialog.</summary>
    RestoreRequested,
}

public enum TextStyle { Normal, RoomTitle, Emphasis, System, Echo, Error }

public sealed record OutputEvent(OutputKind Kind, string? Text = null, string? Id = null, TextStyle Style = TextStyle.Normal, bool Loop = false, int Milliseconds = 0)
{
    public override string ToString() => Kind == OutputKind.Text ? Text ?? "" : $"[{Kind} {Id}{Text}]";
}

public sealed class TurnResult
{
    public List<OutputEvent> Events { get; } = new();
    public bool GameOver { get; set; }
    public bool Won { get; set; }

    /// <summary>All text output joined together (for tests and simple hosts).</summary>
    public string Text => string.Concat(Events.Where(e => e.Kind == OutputKind.Text).Select(e => e.Text));
}

