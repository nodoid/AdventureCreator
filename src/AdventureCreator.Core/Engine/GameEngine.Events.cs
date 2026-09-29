using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Engine;

/// <summary>Random events, room flags (flooding), traps.</summary>
public sealed partial class GameEngine
{
    public const string FloodedFlag = "flooded";

    // ================================================================ room flags and flooding

    public bool RoomHasFlag(string? roomId, string flag) =>
        roomId != null && State.RoomFlags.TryGetValue(roomId, out var flags) && flags.Contains(flag);

    public void SetRoomFlag(string roomId, string flag, bool on)
    {
        if (!State.RoomFlags.TryGetValue(roomId, out var flags))
            State.RoomFlags[roomId] = flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (on) flags.Add(flag); else flags.Remove(flag);
    }

    public bool IsFlooded(string? roomId) => RoomHasFlag(roomId, FloodedFlag);

    private bool PlayerCanSwim() => Carried().Any(i => i.AllowsWater);

    public void Flood(string roomId, bool on, CommandContext ctx)
    {
        if (IsFlooded(roomId) == on) return;
        SetRoomFlag(roomId, FloodedFlag, on);
        bool here = string.Equals(roomId, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase);
        if (!on)
        {
            if (here) Say(Msg(Engine.Msg.Drained, ctx));
            return;
        }
        // Water puts out lights left lying in the room.
        foreach (var item in ItemsAt(roomId).Where(i => i.LightSource && IsLit(i))) State.ItemLit[item.Id] = false;
        if (!here) return;
        Say(Msg(Engine.Msg.FloodRises, ctx));
        if (PlayerCanSwim()) return;
        // Swept out through an exit to a dry room, or drowned if there is nowhere to go.
        var escape = ExitsOf(roomId).FirstOrDefault(e => Adventure.FindRoom(e.TargetRoomId) != null && !IsFlooded(e.TargetRoomId) &&
                                                          (e.DoorItemId == null || Adventure.FindItem(e.DoorItemId) is not { } d || IsOpen(d)));
        if (escape == null)
        {
            EndGame(false, Msg(Engine.Msg.Drowned, ctx), ctx);
            return;
        }
        Say(Msg(Engine.Msg.SweptOut, ctx, ("room", Adventure.FindRoom(escape.TargetRoomId)!.Name)));
        MovePlayer(escape.TargetRoomId, ctx);
    }

    // ================================================================ traps

    public void SetTrap(string roomId, TrapState trap) => State.Traps[roomId] = trap;

    /// <summary>On entering: an uneasy feeling if there is an undiscovered trap.</summary>
    private void SenseTrap(CommandContext ctx)
    {
        if (State.Traps.TryGetValue(State.CurrentRoomId, out var trap) && !trap.Revealed && !IsDark())
            Say(Msg(Engine.Msg.TrapSense, ctx));
    }

    /// <summary>SEARCH with no object: finds traps.</summary>
    private bool RevealTraps(CommandContext ctx)
    {
        if (!State.Traps.TryGetValue(State.CurrentRoomId, out var trap)) return false;
        if (trap.Revealed) { Say(Msg(Engine.Msg.TrapAlreadyFound, ctx)); return true; }
        trap.Revealed = true;
        Say(string.IsNullOrWhiteSpace(trap.RevealMessage) ? Msg(Engine.Msg.TrapFound, ctx) : Format(trap.RevealMessage, ctx));
        return true;
    }

    private static readonly HashSet<string> SafeInTrapRooms = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "look", "examine", "lookunder", "lookbehind", "listen", "smell", "wait", "inventory", "disarm",
    };

    /// <summary>End of turn: an undiscovered trap springs if the player did anything careless in its room.</summary>
    private void CheckTrap(CommandContext ctx)
    {
        var room = State.CurrentRoomId;
        if (!State.Traps.TryGetValue(room, out var trap) || trap.Revealed) return;
        if (State.RoomEnteredTurn >= State.Turns - 1) return;              // the turn the player arrived: just the uneasy feeling
        if (ctx.Command?.ActionId is { } a && SafeInTrapRooms.Contains(a)) return;
        State.Traps.Remove(room);
        var message = string.IsNullOrWhiteSpace(trap.Message) ? Msg(Engine.Msg.TrapSprung, ctx) : Format(trap.Message, ctx);
        if (trap.Deadly) { EndGame(false, message, ctx); return; }
        HurtPlayer(trap.Damage, message, ctx);
    }

    // ================================================================ random events

    private void RunRandomEvents(CommandContext ctx)
    {
        foreach (var ev in Adventure.RandomEvents)
        {
            if (State.GameOver) return;
            if (!ev.Enabled || State.Turns < ev.EarliestTurn || (ev.LatestTurn > 0 && State.Turns > ev.LatestTurn)) continue;
            State.EventCounts.TryGetValue(ev.Id, out var count);
            if (ev.MaxTimes > 0 && count >= ev.MaxTimes) continue;
            if (State.EventLastTurn.TryGetValue(ev.Id, out var last) && State.Turns - last < Math.Max(0, ev.Cooldown)) continue;
            if (Random.Next(1000) >= ev.Chance * 10 + ev.ChancePerThousand) continue;
            TryFireEvent(ev, ctx, force: false);
        }
    }

    /// <summary>Picks a room for the event, checks its conditions and runs it. Returns true if it happened.</summary>
    public bool TryFireEvent(RandomEvent ev, CommandContext ctx, bool force)
    {
        var candidates = ev.Where switch
        {
            EventLocation.Global => new List<string?> { null },
            EventLocation.PlayerRoom => new List<string?> { State.CurrentRoomId },
            _ => (ev.Rooms.Count > 0 ? ev.Rooms : Adventure.Rooms.Select(r => r.Id))
                 .Where(r => Adventure.FindRoom(r) != null)
                 .Where(r => ev.Where != EventLocation.AwayFromPlayer || !string.Equals(r, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
                 .Select(r => (string?)r).ToList(),
        };
        if (ev.Where == EventLocation.PlayerRoom && ev.Rooms.Count > 0 && !ev.Rooms.Contains(State.CurrentRoomId, StringComparer.OrdinalIgnoreCase))
            candidates.Clear();

        var possible = candidates.Where(room => CheckConditions(ev.Conditions, EventCtx(ctx, room))).ToList();
        if (possible.Count == 0) return false;
        var chosen = possible[Random.Next(possible.Count)];

        State.EventCounts[ev.Id] = State.EventCounts.GetValueOrDefault(ev.Id) + 1;
        State.EventLastTurn[ev.Id] = State.Turns;
        var ectx = EventCtx(ctx, chosen);
        bool witnessed = chosen == null || string.Equals(chosen, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase);
        var text = witnessed ? ev.WitnessMessage : ev.DistantMessage;
        if (!string.IsNullOrWhiteSpace(text)) Say(Format(text, ectx));
        ExecuteActions(ev.Actions, ectx);
        return true;
    }

    private CommandContext EventCtx(CommandContext ctx, string? room) =>
        new(this, ctx.Command) { EventRoomId = room, InRandomEvent = true, Item1 = ctx.Item1, Word1 = ctx.Word1 };

    /// <summary>Resolves room arguments: ids, @here, @eventroom and @randomroom.</summary>
    private string? RoomArg(string? arg, CommandContext ctx) => arg switch
    {
        null or "" => null,
        Locations.Here => State.CurrentRoomId,
        Locations.EventRoom => ctx.EventRoomId ?? State.CurrentRoomId,
        Locations.RandomRoom => Adventure.Rooms.Count == 0 ? null : Adventure.Rooms[Random.Next(Adventure.Rooms.Count)].Id,
        "$npcroom" => ctx.Npc != null ? Loc(ctx.Npc) : null,
        _ => arg,
    };
}
