using AdventureCreator.Core.Model;
using AdventureCreator.Core.Parsing;

namespace AdventureCreator.Core.Engine;

/// <summary>Everything the engine knows about the command currently being processed.</summary>
public sealed class CommandContext
{
    public CommandContext(GameEngine engine, ParsedCommand? command)
    {
        Engine = engine;
        Command = command;
    }

    public GameEngine Engine { get; }
    public ParsedCommand? Command { get; }
    public string? ActionId => Command?.ActionId;
    public Item? Item1 { get; set; }
    public Item? Item2 { get; set; }
    /// <summary>Head noun the player typed for each object (even when no item matched).</summary>
    public string? Word1 { get; set; }
    public string? Word2 { get; set; }
    public bool Object1Missing { get; set; }
    public bool Object2Missing { get; set; }
    public bool Self1 { get; set; }
    public Item? Actor { get; set; }
    /// <summary>Room entered/left, item taken/dropped or puzzle solved for event triggers.</summary>
    public string? EventRoomId { get; set; }
    public string? EventSubject { get; set; }
    public string? UnknownWord { get; set; }
    /// <summary>The NPC acting (for NPC messages and triggers).</summary>
    public Item? Npc { get; set; }
    /// <summary>True while a random event's actions run (CreateItem then defaults to @eventroom).</summary>
    public bool InRandomEvent { get; set; }
}

public sealed partial class GameEngine
{
    // ================================================================ locations and scope

    public string Loc(Item item) => State.ItemLocations.TryGetValue(item.Id, out var l) ? l : item.Location ?? "";
    public void SetLoc(Item item, string location) => State.ItemLocations[item.Id] = location ?? "";
    public bool IsOpen(Item item) => State.ItemOpen.TryGetValue(item.Id, out var v) ? v : item.IsOpen;
    public bool IsLocked(Item item) => State.ItemLocked.TryGetValue(item.Id, out var v) ? v : item.IsLocked;
    public bool IsLit(Item item) => State.ItemLit.TryGetValue(item.Id, out var v) ? v : item.IsLit;
    public bool IsCarried(Item item) => Loc(item) is Locations.Carried or Locations.Worn;
    public bool IsWorn(Item item) => Loc(item) == Locations.Worn;

    public IEnumerable<Item> ItemsAt(string location) =>
        Adventure.Items.Where(i => string.Equals(Loc(i), location, StringComparison.OrdinalIgnoreCase));

    /// <summary>Items carried or worn by the player (not including contents of carried containers).</summary>
    public IEnumerable<Item> Carried() => Adventure.Items.Where(IsCarried);

    /// <summary>The room an item is ultimately in (following containers), or null.</summary>
    public string? RoomOf(Item item)
    {
        var loc = Loc(item);
        for (int guard = 0; guard < 32; guard++)
        {
            if (Locations.IsNowhere(loc)) return null;
            if (loc is Locations.Carried or Locations.Worn) return State.CurrentRoomId;
            if (Adventure.FindRoom(loc) != null) return loc;
            var parent = Adventure.FindItem(loc);
            if (parent == null) return null;
            loc = Loc(parent);
        }
        return null;
    }

    /// <summary>True if the item is carried directly or inside something carried.</summary>
    public bool IsHeldIndirectly(Item item)
    {
        var loc = Loc(item);
        for (int guard = 0; guard < 32; guard++)
        {
            if (loc is Locations.Carried or Locations.Worn) return true;
            var parent = Adventure.FindItem(loc);
            if (parent == null) return false;
            loc = Loc(parent);
        }
        return false;
    }

    private bool ContentsVisible(Item container) =>
        container.Supporter || (container.Container && (IsOpen(container) || container.Transparent));

    /// <summary>Items the player can currently see or refer to.</summary>
    public List<Item> Scope()
    {
        var result = new List<Item>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddWithContents(Item item, int depth)
        {
            if (!seen.Add(item.Id)) return;
            result.Add(item);
            if (depth > 8 || !(ContentsVisible(item) || item.IsCharacter)) return;
            foreach (var inner in ItemsAt(item.Id)) AddWithContents(inner, depth + 1);
        }

        foreach (var i in Carried()) AddWithContents(i, 0);
        if (!IsDark())
            foreach (var i in ItemsAt(State.CurrentRoomId)) AddWithContents(i, 0);
        return result;
    }

    public bool IsPresent(Item item) => Scope().Any(i => i.Id == item.Id);

    public bool IsRoomDark(string roomId)
    {
        var room = Adventure.FindRoom(roomId);
        if (room == null) return false;
        return State.RoomDark.TryGetValue(roomId, out var d) ? d : room.IsDark;
    }

    /// <summary>True if the current room is dark and no light source is available.</summary>
    public bool IsDark()
    {
        bool dark = IsRoomDark(State.CurrentRoomId);
        var darkVar = Adventure.Settings.DarknessVariable;
        if (!string.IsNullOrEmpty(darkVar) && GetVar(darkVar) != 0) dark = true;
        if (!dark) return false;
        foreach (var item in Adventure.Items)
        {
            if (!item.LightSource || !IsLit(item)) continue;
            var loc = Loc(item);
            if (loc is Locations.Carried or Locations.Worn || string.Equals(loc, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase) || IsHeldIndirectly(item))
                return false;
        }
        return true;
    }

    public List<string> NounsOf(Item item)
    {
        if (item.Nouns.Count > 0) return item.Nouns;
        var words = item.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? new List<string> { words[^1] } : new List<string> { item.Id.ToLowerInvariant() };
    }

    // ================================================================ exits

    public List<Exit> ExitsOf(string roomId)
    {
        var room = Adventure.FindRoom(roomId);
        var exits = new List<Exit>();
        var overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, target) in State.ExitOverrides)
        {
            var bar = key.IndexOf('|');
            if (bar < 0 || !string.Equals(key[..bar], roomId, StringComparison.OrdinalIgnoreCase)) continue;
            var dir = key[(bar + 1)..];
            overridden.Add(dir);
            if (target.Length == 0) continue;
            var original = room?.Exits.FirstOrDefault(e => string.Equals(e.Direction, dir, StringComparison.OrdinalIgnoreCase));
            exits.Add(new Exit
            {
                Direction = dir,
                TargetRoomId = target,
                DoorItemId = original?.DoorItemId,
                Hidden = original?.Hidden ?? false,
                TravelMessage = original?.TravelMessage ?? "",
            });
        }
        if (room != null)
            exits.AddRange(room.Exits.Where(e => !overridden.Contains(e.Direction)));
        return exits;
    }

    public Exit? FindExit(string roomId, string direction) =>
        ExitsOf(roomId).FirstOrDefault(e => string.Equals(e.Direction, direction, StringComparison.OrdinalIgnoreCase)
                                            || Lexicon.Equivalent(e.Direction, direction)
                                            || string.Equals(Lexicon.Direction(e.Direction), direction, StringComparison.OrdinalIgnoreCase));

    // ================================================================ describing

    public void Describe(CommandContext? ctx, bool forceFull)
    {
        var room = CurrentRoom;
        if (room == null)
        {
            Say("[The game has no rooms yet.]", TextStyle.System);
            return;
        }

        var picture = room.PictureId ?? Adventure.Settings.DefaultPictureId;
        if (picture != null) ShowPicture(picture);

        if (IsDark())
        {
            Say(Msg(Engine.Msg.Dark, ctx));
            RunEventTriggers(TriggerEvent.AfterDescribe, ctx ?? new CommandContext(this, null));
            return;
        }

        if (!string.IsNullOrWhiteSpace(room.Name)) Say(room.Name, TextStyle.RoomTitle);

        var desc = State.RoomDescriptions.TryGetValue(room.Id, out var d) ? d : room.Description;
        bool brief = !forceFull && !State.Verbose && State.VisitedRooms.Contains(room.Id);
        if (brief && !string.IsNullOrWhiteSpace(room.ShortDescription)) desc = room.ShortDescription;
        if (!brief || !string.IsNullOrWhiteSpace(room.ShortDescription))
            if (!string.IsNullOrWhiteSpace(desc)) Say(Format(desc, ctx));
        if (IsFlooded(room.Id)) Say(Msg(Engine.Msg.FloodedHere, ctx));

        if (Adventure.Settings.AutoListItems)
        {
            var listed = new List<string>();
            foreach (var item in ItemsAt(room.Id))
            {
                if (item.Scenery)
                {
                    // Things on/in scenery (the can on the table) are still worth mentioning.
                    if (ContentsVisible(item) && !item.IsCharacter)
                    {
                        var inside = ItemsAt(item.Id).Where(i => !i.Scenery).Select(i => i.WithArticle()).ToList();
                        if (inside.Count > 0)
                            Say(Msg(item.Supporter ? Engine.Msg.On : Engine.Msg.Inside, WithItem(ctx ?? new CommandContext(this, null), item), ("list", JoinList(inside))));
                    }
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(item.RoomDescription) && !State.TakenOnce.Contains(item.Id))
                {
                    Say(Format(item.RoomDescription, ctx));
                    continue;
                }
                listed.Add(DescribeInList(item));
            }
            if (listed.Count > 0)
                Say(Msg(Engine.Msg.YouCanSee, ctx, ("list", JoinList(listed))));
        }

        if (Adventure.Settings.AutoListExits) ListExits(ctx);
        RunEventTriggers(TriggerEvent.AfterDescribe, ctx ?? new CommandContext(this, null));
    }

    private string DescribeInList(Item item)
    {
        var s = item.WithArticle();
        if (item.LightSource && IsLit(item)) s += " (providing light)";
        if (ContentsVisible(item))
        {
            var inside = ItemsAt(item.Id).Where(i => !i.Scenery).Select(i => i.WithArticle()).ToList();
            if (inside.Count > 0) s += (item.Supporter ? " (on which is " : " (containing ") + JoinList(inside) + ")";
        }
        else if (item.Container && item.Openable && !IsOpen(item)) s += " (closed)";
        if (item.IsCharacter)
        {
            var held = ItemsAt(item.Id).Select(i => i.WithArticle()).ToList();
            if (held.Count > 0) s += " (carrying " + JoinList(held) + ")";
            if (item.Npc != null && NpcStateOf(item).Defeated && !item.Npc.RemoveWhenDefeated) s += " (defeated)";
        }
        return s;
    }

    public void ListExits(CommandContext? ctx)
    {
        var exits = ExitsOf(State.CurrentRoomId).Where(e => !e.Hidden).Select(e => e.Direction).Distinct().ToList();
        Say(exits.Count == 0 ? Msg(Engine.Msg.NoExits, ctx) : Msg(Engine.Msg.Exits, ctx, ("list", JoinList(exits))));
    }

    public void ShowInventory(CommandContext? ctx)
    {
        var held = ItemsAt(Locations.Carried).Select(DescribeInList).ToList();
        var worn = ItemsAt(Locations.Worn).Select(i => i.WithArticle()).ToList();
        if (held.Count == 0 && worn.Count == 0)
        {
            Say(Msg(Engine.Msg.CarryingNothing, ctx));
            return;
        }
        if (held.Count > 0) Say(Msg(Engine.Msg.Carrying, ctx, ("list", JoinList(held))));
        if (worn.Count > 0) Say(Msg(Engine.Msg.Wearing, ctx, ("list", JoinList(worn))));
    }

    // ================================================================ moving

    public void EnterRoom(string roomId, CommandContext? ctx, bool initial = false)
    {
        var room = Adventure.FindRoom(roomId);
        ctx ??= new CommandContext(this, null);
        State.CurrentRoomId = roomId;
        State.RoomEnteredTurn = State.Turns;
        // Triggers that prepare the room (or send the player on elsewhere) before it is described.
        RunEventTriggers(TriggerEvent.BeforeEnterRoom, new CommandContext(this, ctx.Command) { EventRoomId = roomId, Item1 = ctx.Item1, Word1 = ctx.Word1 });
        if (!string.Equals(State.CurrentRoomId, roomId, StringComparison.OrdinalIgnoreCase) || State.GameOver) return;
        Describe(ctx, forceFull: false);
        bool first = State.VisitedRooms.Add(roomId);
        NpcsNoticePlayer(ctx);
        SenseTrap(ctx);

        if (room != null)
        {
            if (room.SoundId != null)
            {
                if (!string.Equals(State.CurrentSoundId, room.SoundId, StringComparison.OrdinalIgnoreCase))
                    PlaySound(room.SoundId, room.LoopSound);
            }
            else if (State.CurrentSoundId != null && !initial) StopSound();

            if (first && room.ScoreOnFirstVisit != 0) AwardScore(room.ScoreOnFirstVisit, "visit:" + room.Id);
        }

        var enterCtx = new CommandContext(this, ctx.Command) { EventRoomId = roomId, Item1 = ctx.Item1, Word1 = ctx.Word1 };
        RunEventTriggers(TriggerEvent.EnterRoom, enterCtx);
    }

    public void MovePlayer(string roomId, CommandContext? ctx, string? travelMessage = null)
    {
        if (Adventure.FindRoom(roomId) == null)
        {
            Say($"[Missing room \"{roomId}\".]", TextStyle.Error);
            return;
        }
        var leaveCtx = new CommandContext(this, ctx?.Command) { EventRoomId = State.CurrentRoomId };
        var left = RunEventTriggers(TriggerEvent.LeaveRoom, leaveCtx);
        if (left.Handled || State.GameOver) return;
        if (!string.IsNullOrWhiteSpace(travelMessage)) Say(Format(travelMessage, ctx));
        var from = State.CurrentRoomId;
        EnterRoom(roomId, ctx);
        if (!State.GameOver) NpcsFollowPlayer(from, roomId, ctx ?? new CommandContext(this, null));
    }

    // ================================================================ scoring

    public void AwardScore(int points, string? key)
    {
        if (points == 0) return;
        if (!string.IsNullOrEmpty(key) && !State.AwardKeys.Add(key)) return;
        State.Score += points;
        if (!Adventure.Settings.LegacyTableSemantics)
            Say(points > 0 ? $"[Your score has gone up by {points} point{(points == 1 ? "" : "s")}.]"
                           : $"[Your score has gone down by {-points} point{(points == -1 ? "" : "s")}.]", TextStyle.System);
    }

    public void SolvePuzzle(Puzzle puzzle, CommandContext? ctx)
    {
        if (!State.SolvedPuzzles.Add(puzzle.Id)) return;
        if (!string.IsNullOrWhiteSpace(puzzle.SolvedMessage)) Say(Format(puzzle.SolvedMessage, ctx));
        if (puzzle.Points != 0) AwardScore(puzzle.Points, "puzzle:" + puzzle.Id);
        ctx ??= new CommandContext(this, null);
        ExecuteActions(puzzle.OnSolved, ctx);
        RunEventTriggers(TriggerEvent.PuzzleSolved, new CommandContext(this, ctx.Command) { EventSubject = puzzle.Id });
    }

    public void EndGame(bool won, string? text, CommandContext? ctx)
    {
        if (!string.IsNullOrWhiteSpace(text)) Say(Format(text, ctx));
        Say(Msg(won ? Engine.Msg.GameOverWin : Engine.Msg.GameOverLose, ctx), TextStyle.Emphasis);
        Say(Msg(Engine.Msg.Score, ctx));
        State.GameOver = true;
        State.Won = won;
        Emit(new OutputEvent(OutputKind.GameOver, won ? "won" : "lost"));
    }
}
