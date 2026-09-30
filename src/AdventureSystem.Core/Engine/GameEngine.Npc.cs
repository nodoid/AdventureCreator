using AdventureSystem.Core.Model;

namespace AdventureSystem.Core.Engine;

/// <summary>Non-player characters: movement through exits, reactions, giving, stealing, blocking, combat and orders.</summary>
public sealed partial class GameEngine
{
    private static readonly Dictionary<string, string> Opposites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = "south", ["south"] = "north", ["east"] = "west", ["west"] = "east",
        ["northeast"] = "southwest", ["southwest"] = "northeast", ["northwest"] = "southeast", ["southeast"] = "northwest",
        ["up"] = "below", ["down"] = "above", ["in"] = "outside", ["out"] = "inside",
    };

    // ================================================================ state helpers

    public IEnumerable<Item> Npcs() => Adventure.Items.Where(i => i.IsCharacter && i.Npc != null);

    public NpcState NpcStateOf(Item npc)
    {
        if (!State.Npcs.TryGetValue(npc.Id, out var s))
        {
            s = new NpcState { Health = npc.Npc?.Health ?? 0, Following = npc.Npc?.Movement == NpcMovement.Follow };
            State.Npcs[npc.Id] = s;
        }
        return s;
    }

    public bool NpcActive(Item npc)
    {
        if (npc.Npc == null) return false;
        var s = NpcStateOf(npc);
        return !s.Defeated && (s.Active ?? npc.Npc.Active) && Adventure.FindRoom(Loc(npc)) != null;
    }

    public bool NpcHostile(Item npc) => npc.Npc != null && !NpcStateOf(npc).Defeated && (NpcStateOf(npc).Hostile ?? npc.Npc.Hostile);

    private bool NpcBlocking(Item npc) =>
        npc.Npc != null && npc.Npc.BlocksExits.Count > 0 && NpcActive(npc) && (NpcStateOf(npc).Blocking ?? true);

    private NpcMovement MovementOf(Item npc)
    {
        var s = NpcStateOf(npc);
        if (s.Following) return NpcMovement.Follow;
        var m = s.Movement ?? npc.Npc!.Movement;
        return m == NpcMovement.Follow ? NpcMovement.Stationary : m; // Follow mode that was switched off ("stay")
    }

    private CommandContext NpcCtx(Item npc, CommandContext? from = null, Item? thing = null) =>
        new(this, from?.Command) { Npc = npc, Item1 = thing ?? from?.Item1, Word1 = from?.Word1, EventRoomId = from?.EventRoomId, InRandomEvent = from?.InRandomEvent ?? false };

    private string NpcText(Item npc, string custom, string fallback, CommandContext ctx, params (string, string)[] extra) =>
        Format(string.IsNullOrWhiteSpace(custom) ? fallback : custom, ctx, extra);

    private bool WithPlayer(Item npc) => string.Equals(Loc(npc), State.CurrentRoomId, StringComparison.OrdinalIgnoreCase);

    // ================================================================ exits and routes (NPCs only use real exits)

    /// <summary>The exits an NPC can use from a room right now.</summary>
    private List<Exit> NpcExits(Item npc, string roomId, bool respectAllowedRooms)
    {
        var result = new List<Exit>();
        var allowed = npc.Npc!.AllowedRooms;
        foreach (var e in ExitsOf(roomId))
        {
            if (Adventure.FindRoom(e.TargetRoomId) == null) continue;
            if (respectAllowedRooms && allowed.Count > 0 && !allowed.Contains(e.TargetRoomId, StringComparer.OrdinalIgnoreCase)) continue;
            if (IsFlooded(e.TargetRoomId)) continue;
            if (e.DoorItemId != null && Adventure.FindItem(e.DoorItemId) is { } door && !IsOpen(door))
                if (IsLocked(door) || !npc.Npc.OpensDoors) continue;
            if (e.Conditions.Count > 0 && !CheckConditions(e.Conditions, NpcCtx(npc))) continue;
            result.Add(e);
        }
        return result;
    }

    /// <summary>First exit on the shortest route from the NPC's room to the target room, or null if there is no route.</summary>
    private Exit? NextStep(Item npc, string target, bool respectAllowedRooms)
    {
        var start = Loc(npc);
        if (string.Equals(start, target, StringComparison.OrdinalIgnoreCase)) return null;
        var firstStep = new Dictionary<string, Exit>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var room = queue.Dequeue();
            foreach (var e in NpcExits(npc, room, respectAllowedRooms))
            {
                if (!seen.Add(e.TargetRoomId)) continue;
                firstStep[e.TargetRoomId] = room == start ? e : firstStep[room];
                if (string.Equals(e.TargetRoomId, target, StringComparison.OrdinalIgnoreCase)) return firstStep[e.TargetRoomId];
                queue.Enqueue(e.TargetRoomId);
            }
        }
        return null;
    }

    /// <summary>Moves an NPC through an exit of its current room, with messages if the player can see it.</summary>
    private void MoveNpc(Item npc, Exit exit, CommandContext ctx)
    {
        var from = Loc(npc);
        var to = exit.TargetRoomId;
        var nctx = NpcCtx(npc, ctx);
        var state = NpcStateOf(npc);

        if (exit.DoorItemId != null && Adventure.FindItem(exit.DoorItemId) is { } door && !IsOpen(door))
        {
            State.ItemOpen[door.Id] = true;
            if (string.Equals(from, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase) || string.Equals(to, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
                Say(Format($"{{The npc}} opens {door.WithDefinite()}.", nctx));
        }

        if (string.Equals(from, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            Say(NpcText(npc, npc.Npc!.DepartureMessage, "{The npc} leaves, heading {direction}.", nctx, ("direction", exit.Direction)));
            state.LastExit = exit.Direction;
            state.LastExitRoom = from;
            RunEventTriggers(TriggerEvent.NpcLeaves, new CommandContext(this, ctx.Command) { EventSubject = npc.Id, Npc = npc, EventRoomId = from });
        }

        SetLoc(npc, to);

        if (string.Equals(to, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase) && !IsDark())
        {
            var fromDir = Opposites.TryGetValue(exit.Direction, out var o) ? o : "";
            var fallback = fromDir.Length == 0 ? "{The npc} arrives." : fromDir is "above" or "below" or "inside" or "outside" ? "{The npc} arrives from {direction}." : "{The npc} arrives from the {direction}.";
            Say(NpcText(npc, npc.Npc!.ArrivalMessage, fallback, nctx, ("direction", fromDir)));
            Greet(npc, nctx);
            RunEventTriggers(TriggerEvent.NpcArrives, new CommandContext(this, ctx.Command) { EventSubject = npc.Id, Npc = npc, EventRoomId = to });
        }
    }

    private void Greet(Item npc, CommandContext ctx)
    {
        var s = NpcStateOf(npc);
        if (s.Greeted || string.IsNullOrWhiteSpace(npc.Npc?.GreetingMessage) || !NpcActive(npc)) return;
        s.Greeted = true;
        Say(Format(npc.Npc.GreetingMessage, NpcCtx(npc, ctx)));
    }

    /// <summary>Called when the player arrives somewhere: NPCs greet them.</summary>
    private void NpcsNoticePlayer(CommandContext ctx)
    {
        if (IsDark()) return;
        foreach (var npc in Npcs().Where(WithPlayer).ToList()) Greet(npc, ctx);
    }

    /// <summary>Called after the player moves: followers come along if there is an exit they can use.</summary>
    private void NpcsFollowPlayer(string fromRoom, string toRoom, CommandContext ctx)
    {
        foreach (var npc in Npcs().ToList())
        {
            if (!NpcActive(npc) || MovementOf(npc) != NpcMovement.Follow) continue;
            if (!string.Equals(Loc(npc), fromRoom, StringComparison.OrdinalIgnoreCase)) continue;
            var exit = NpcExits(npc, fromRoom, respectAllowedRooms: false)
                .FirstOrDefault(e => string.Equals(e.TargetRoomId, toRoom, StringComparison.OrdinalIgnoreCase));
            if (exit == null) continue; // no exit between the rooms (teleport, one-way): it tries to catch up later
            SetLoc(npc, toRoom);
            Say(NpcText(npc, npc.Npc!.FollowMessage, "{The npc} follows you.", NpcCtx(npc, ctx)));
        }
    }

    // ================================================================ the NPC turn

    private void RunNpcs(CommandContext ctx)
    {
        foreach (var npc in Npcs().ToList())
        {
            if (State.GameOver) return;
            if (!NpcActive(npc)) continue;
            var b = npc.Npc!;
            var s = NpcStateOf(npc);
            var nctx = NpcCtx(npc, ctx);
            bool moved = false;

            // 1. Movement (always through exits of the current room)
            if (s.Destination != null)
            {
                var step = NextStep(npc, s.Destination, respectAllowedRooms: false);
                if (step != null) { MoveNpc(npc, step, ctx); moved = true; }
                if (step == null || string.Equals(Loc(npc), s.Destination, StringComparison.OrdinalIgnoreCase)) s.Destination = null;
            }
            else if (State.Turns % Math.Max(1, b.MoveEvery) == 0)
            {
                var mode = MovementOf(npc);
                bool together = WithPlayer(npc);
                bool roll = Random.Next(100) < Math.Clamp(b.MoveChance, 0, 100);
                Exit? exit = null;
                switch (mode)
                {
                    case NpcMovement.Wander when roll:
                        var options = NpcExits(npc, Loc(npc), respectAllowedRooms: true);
                        if (options.Count > 0) exit = options[Random.Next(options.Count)];
                        break;
                    case NpcMovement.Patrol when b.Route.Count > 0:
                        exit = PatrolStep(npc, s);
                        break;
                    case NpcMovement.Follow when !together:
                    case NpcMovement.Seek when !together && roll:
                        exit = NextStep(npc, State.CurrentRoomId, respectAllowedRooms: mode == NpcMovement.Seek);
                        break;
                    case NpcMovement.Flee when together && roll:
                        var away = NpcExits(npc, Loc(npc), respectAllowedRooms: true);
                        if (away.Count > 0) exit = away[Random.Next(away.Count)];
                        break;
                }
                if (exit != null) { MoveNpc(npc, exit, ctx); moved = true; }
            }
            if (State.GameOver) return;

            // 2. Collecting things lying around
            if (b.CollectsItems)
            {
                var loot = ItemsAt(Loc(npc)).Where(i => i.Portable && !i.Scenery && !i.IsCharacter &&
                    (b.CollectsOnly.Count == 0 || b.CollectsOnly.Contains(i.Id, StringComparer.OrdinalIgnoreCase))).ToList();
                if (loot.Count > 0)
                {
                    var item = loot[Random.Next(loot.Count)];
                    SetLoc(item, npc.Id);
                    if (WithPlayer(npc) && !IsDark()) Say(Format("{The npc} picks up {the noun1}.", NpcCtx(npc, ctx, item)));
                }
            }

            // 3. Interacting with the player
            if (!WithPlayer(npc) || IsDark()) continue;
            if (NpcHostile(npc) && Random.Next(100) < Math.Clamp(b.AttackChance, 0, 100))
            {
                AttackPlayer(npc, ctx);
                if (State.GameOver) return;
                continue;
            }
            if (b.StealChance > 0 && Random.Next(100) < b.StealChance)
            {
                var targets = ItemsAt(Locations.Carried).Where(i => b.StealsItems.Count == 0 || b.StealsItems.Contains(i.Id, StringComparer.OrdinalIgnoreCase)).ToList();
                if (targets.Count > 0)
                {
                    var item = targets[Random.Next(targets.Count)];
                    SetLoc(item, npc.Id);
                    Say(NpcText(npc, b.StealMessage, "{The npc} snatches {the noun1} from you!", NpcCtx(npc, ctx, item)));
                    continue;
                }
            }
            if (!moved && b.IdleMessages.Count > 0 && Random.Next(100) < Math.Clamp(b.IdleChance, 0, 100))
                Say(Format(b.IdleMessages[Random.Next(b.IdleMessages.Count)], nctx));
        }
    }

    private Exit? PatrolStep(Item npc, NpcState s)
    {
        var route = npc.Npc!.Route;
        s.RouteIndex = Math.Clamp(s.RouteIndex, 0, route.Count - 1);
        if (string.Equals(Loc(npc), route[s.RouteIndex], StringComparison.OrdinalIgnoreCase))
        {
            if (route.Count == 1) return null;
            if (npc.Npc.RouteLoops) s.RouteIndex = (s.RouteIndex + 1) % route.Count;
            else
            {
                if (s.RouteIndex + s.RouteStep < 0 || s.RouteIndex + s.RouteStep >= route.Count) s.RouteStep = -s.RouteStep;
                s.RouteIndex += s.RouteStep;
            }
        }
        return NextStep(npc, route[s.RouteIndex], respectAllowedRooms: false);
    }

    // ================================================================ combat and health

    public void HurtPlayer(int damage, string? message, CommandContext ctx, Item? attacker = null)
    {
        if (!string.IsNullOrWhiteSpace(message)) Say(Format(message, ctx));
        if (Adventure.Settings.PlayerHealth <= 0 || damage <= 0) return;
        State.Health = Math.Max(0, State.Health - damage);
        RunEventTriggers(TriggerEvent.PlayerHurt, new CommandContext(this, ctx.Command) { Npc = attacker, EventSubject = attacker?.Id });
        if (State.Health <= 0 && !State.GameOver)
        {
            var death = attacker?.Npc != null ? NpcText(attacker, attacker.Npc.KillMessage, "{The npc} has killed you.", NpcCtx(attacker, ctx))
                : Format(Adventure.Settings.DeathMessage, ctx);
            EndGame(false, death, ctx);
        }
        else if (State.Health <= Math.Max(1, Adventure.Settings.PlayerHealth / 4))
            Say(Msg(Engine.Msg.BadlyHurt, ctx), TextStyle.System);
    }

    private void AttackPlayer(Item npc, CommandContext ctx)
    {
        var nctx = NpcCtx(npc, ctx);
        HurtPlayer(npc.Npc!.Damage, NpcText(npc, npc.Npc.AttackMessage, "{The npc} attacks you!", nctx), nctx, npc);
    }

    /// <summary>The player attacks an NPC that can be hurt. Returns false if the NPC can't be fought.</summary>
    private bool AttackNpc(Item npc, Item? weapon, CommandContext ctx)
    {
        var b = npc.Npc;
        if (b == null || b.Health <= 0) return false;
        var s = NpcStateOf(npc);
        var nctx = NpcCtx(npc, ctx);
        if (s.Defeated) { Say(Format("{The npc} is in no state to fight.", nctx)); return true; }
        weapon ??= Carried().Where(i => i.Damage > 0).OrderByDescending(i => i.Damage).FirstOrDefault();
        int damage = 1 + (weapon?.Damage ?? 0);
        s.Health -= damage;
        Say(NpcText(npc, b.HitMessage, weapon != null ? $"You strike {{the npc}} with {weapon.WithDefinite()}." : "You hit {the npc}.", nctx));
        if (b.RetaliatesWhenAttacked) s.Hostile = true;
        if (s.Health <= 0) DefeatNpc(npc, ctx);
        return true;
    }

    public void DefeatNpc(Item npc, CommandContext ctx)
    {
        var s = NpcStateOf(npc);
        var nctx = NpcCtx(npc, ctx);
        s.Defeated = true;
        s.Following = false;
        var room = Loc(npc);
        foreach (var held in ItemsAt(npc.Id).ToList()) SetLoc(held, room);
        Say(NpcText(npc, npc.Npc!.DefeatMessage, "{The npc} collapses.", nctx));
        if (npc.Npc.RemoveWhenDefeated) SetLoc(npc, Locations.Nowhere);
        ExecuteActions(npc.Npc.OnDefeat, nctx);
        RunEventTriggers(TriggerEvent.NpcDefeated, new CommandContext(this, ctx.Command) { EventSubject = npc.Id, Npc = npc });
    }

    // ================================================================ giving, asking, orders

    /// <summary>The player gives an item to an NPC. Returns false if the NPC has no behaviour (use the default reply).</summary>
    private bool GiveToNpc(Item item, Item npc, CommandContext ctx)
    {
        var b = npc.Npc;
        if (b == null) return false;
        var nctx = NpcCtx(npc, ctx, item);
        if (!b.Wants.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
        {
            Say(NpcText(npc, b.RefuseMessage, "{The npc} doesn't want {the noun1}.", nctx));
            return true;
        }
        if (!EnsureHeld(item, ctx)) return true;
        var s = NpcStateOf(npc);
        SetLoc(item, npc.Id);
        s.Received.Add(item.Id);
        Say(NpcText(npc, b.AcceptMessage, "{The npc} gratefully accepts {the noun1}.", nctx));
        if (b.FollowsWhenGiven) s.Following = true;
        if (b.PacifiedWhenGiven) { s.Hostile = false; s.Blocking = false; }
        ExecuteActions(b.OnAccept, nctx);
        RunEventTriggers(TriggerEvent.ItemGiven, new CommandContext(this, ctx.Command) { EventSubject = npc.Id, Npc = npc, Item1 = item, Word1 = ctx.Word1 });
        return true;
    }

    private bool AskNpcFor(Item npc, Item item, CommandContext ctx)
    {
        var nctx = NpcCtx(npc, ctx, item);
        if (!string.Equals(Loc(item), npc.Id, StringComparison.OrdinalIgnoreCase))
        {
            Say(Format("{The npc} doesn't have {the noun1}.", nctx));
            return false;
        }
        if (npc.Npc != null && (NpcHostile(npc) || npc.Npc.StealChance > 0 || npc.Npc.CollectsItems))
        {
            Say(Format("{The npc} clutches {the noun1} and refuses to part with it.", nctx));
            return false;
        }
        if (TooManyCarried(extra: 1)) { Say(Msg(Engine.Msg.TooMany, ctx)); return false; }
        SetLoc(item, Locations.Carried);
        Say(Format("{The npc} hands you {the noun1}.", nctx));
        return true;
    }

    /// <summary>"robot, go north" for NPCs that obey orders. Returns true if the order was understood.</summary>
    private bool ExecuteNpcOrder(Item npc, CommandContext ctx)
    {
        var cmd = ctx.Command!;
        var nctx = NpcCtx(npc, ctx);
        var s = NpcStateOf(npc);
        string ok = npc.Npc!.ObeyMessage;
        void Obey(string fallback) => Say(NpcText(npc, ok, fallback, nctx));

        switch (cmd.ActionId)
        {
            case "go":
            case "enter":
            case "exit":
            {
                var dir = cmd.Direction ?? (cmd.ActionId == "exit" ? "out" : cmd.ActionId == "enter" ? "in" : null);
                var exit = dir == null ? null : NpcExits(npc, Loc(npc), respectAllowedRooms: false)
                    .FirstOrDefault(e => string.Equals(e.Direction, dir, StringComparison.OrdinalIgnoreCase));
                if (exit == null) { Say(Format("{The npc} can't go that way.", nctx)); return true; }
                s.Following = false;
                MoveNpc(npc, exit, ctx);
                return true;
            }
            case "follow":
                s.Following = true;
                s.Destination = null;
                Obey("{The npc} falls in behind you.");
                return true;
            case "wait":
            case "stand":
            case "sit":
                s.Following = false;
                s.Movement = NpcMovement.Stationary;
                s.Destination = null;
                Obey("{The npc} stays where {npc} is.");
                return true;
            case "take":
                if (ctx.Item1 == null || !ctx.Item1.Portable || ctx.Item1.IsCharacter) break;
                SetLoc(ctx.Item1, npc.Id);
                Say(Format("{The npc} picks up {the noun1}.", NpcCtx(npc, ctx, ctx.Item1)));
                return true;
            case "drop":
                if (ctx.Item1 == null || !string.Equals(Loc(ctx.Item1), npc.Id, StringComparison.OrdinalIgnoreCase)) break;
                SetLoc(ctx.Item1, Loc(npc));
                Say(Format("{The npc} puts down {the noun1}.", NpcCtx(npc, ctx, ctx.Item1)));
                return true;
            case "give":
                if (ctx.Item1 == null) break;
                return AskNpcFor(npc, ctx.Item1, ctx) || true;
            case "open":
            case "close":
                if (ctx.Item1 is not { Openable: true } thing || IsLocked(thing)) break;
                State.ItemOpen[thing.Id] = cmd.ActionId == "open";
                Say(Format($"{{The npc}} {(cmd.ActionId == "open" ? "opens" : "closes")} {{the noun1}}.", NpcCtx(npc, ctx, thing)));
                return true;
        }
        Say(Format("{The npc} doesn't know how to do that.", nctx));
        return true;
    }

    /// <summary>FOLLOW an NPC that has just left: go the way it went.</summary>
    private bool FollowNpc(string? word, CommandContext ctx)
    {
        foreach (var npc in Npcs())
        {
            if (word != null && !NounsOf(npc).Any(n => Lexicon.Equivalent(n, word))) continue;
            var s = NpcStateOf(npc);
            if (s.LastExit != null && string.Equals(s.LastExitRoom, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
                return Go(s.LastExit, ctx);
        }
        Say("You're not sure which way it went.");
        return false;
    }

    /// <summary>An NPC blocking the exit in this direction, if any.</summary>
    private Item? BlockerFor(string direction)
    {
        foreach (var npc in Npcs())
        {
            if (!WithPlayer(npc) || !NpcBlocking(npc)) continue;
            var blocks = npc.Npc!.BlocksExits;
            if (blocks.Contains("*") || blocks.Any(d => string.Equals(d, direction, StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(Lexicon.Direction(d), direction, StringComparison.OrdinalIgnoreCase)))
                return npc;
        }
        return null;
    }

    private void SetNpcProperty(Item npc, string? property, int n, string? text)
    {
        var s = NpcStateOf(npc);
        switch ((property ?? "").ToLowerInvariant())
        {
            case "movement":
                if (Enum.TryParse<NpcMovement>(text, true, out var m)) { s.Movement = m; s.Following = m == NpcMovement.Follow; }
                break;
            case "hostile": s.Hostile = n != 0; break;
            case "following": s.Following = n != 0; break;
            case "blocking": s.Blocking = n != 0; break;
            case "active": s.Active = n != 0; break;
            case "health": s.Health = n; if (n > 0) s.Defeated = false; break;
        }
    }
}
