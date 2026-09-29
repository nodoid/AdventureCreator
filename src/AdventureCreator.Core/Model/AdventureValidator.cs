using AdventureCreator.Core.Parsing;

namespace AdventureCreator.Core.Model;

public enum IssueSeverity { Error, Warning, Info }

public sealed record ValidationIssue(IssueSeverity Severity, string Where, string Message)
{
    public override string ToString() => $"{Severity}: {Where}: {Message}";
}

/// <summary>Checks an adventure for broken references and common authoring mistakes.</summary>
public static class AdventureValidator
{
    public static List<ValidationIssue> Validate(Adventure a)
    {
        var issues = new List<ValidationIssue>();
        void Error(string where, string msg) => issues.Add(new(IssueSeverity.Error, where, msg));
        void Warn(string where, string msg) => issues.Add(new(IssueSeverity.Warning, where, msg));

        if (a.Rooms.Count == 0) Error("Game", "There are no rooms.");
        if (a.Rooms.Count > 0 && a.FindRoom(a.StartRoomId) == null) Error("Game", $"Start room \"{a.StartRoomId}\" does not exist.");

        void Duplicates<T>(IEnumerable<T> list, Func<T, string> id, string kind)
        {
            foreach (var g in list.GroupBy(id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Error(kind, $"The id \"{g.Key}\" is used {g.Count()} times.");
            foreach (var x in list.Where(x => string.IsNullOrWhiteSpace(id(x))))
                Error(kind, "An entry has no id.");
        }
        Duplicates(a.Rooms, r => r.Id, "Rooms");
        Duplicates(a.Items, i => i.Id, "Items");
        Duplicates(a.Puzzles, p => p.Id, "Puzzles");
        Duplicates(a.Triggers, t => t.Id, "Triggers");
        Duplicates(a.Pictures, p => p.Id, "Pictures");
        Duplicates(a.Sounds, s => s.Id, "Sounds");
        foreach (var clash in a.Rooms.Select(r => r.Id).Intersect(a.Items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase))
            Error("Items", $"\"{clash}\" is used as both a room id and an item id.");

        void CheckPicture(string where, string? id)
        {
            if (!string.IsNullOrEmpty(id) && a.FindPicture(id) == null) Error(where, $"Picture \"{id}\" does not exist.");
        }
        void CheckSound(string where, string? id)
        {
            if (!string.IsNullOrEmpty(id) && a.FindSound(id) == null) Error(where, $"Sound \"{id}\" does not exist.");
        }

        CheckPicture("Game", a.IntroPictureId);
        CheckSound("Game", a.IntroSoundId);
        CheckPicture("Game", a.Settings.DefaultPictureId);

        foreach (var r in a.Rooms)
        {
            var where = $"Room {r.Id}";
            CheckPicture(where, r.PictureId);
            CheckSound(where, r.SoundId);
            foreach (var e in r.Exits)
            {
                if (a.FindRoom(e.TargetRoomId) == null) Error(where, $"Exit {e.Direction} leads to missing room \"{e.TargetRoomId}\".");
                if (e.DoorItemId != null && a.FindItem(e.DoorItemId) == null) Error(where, $"Exit {e.Direction} uses missing door \"{e.DoorItemId}\".");
                CheckConditions(where + $" exit {e.Direction}", e.Conditions);
            }
            foreach (var g in r.Exits.GroupBy(e => e.Direction, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Warn(where, $"There are {g.Count()} exits {g.Key}; only the first is used.");
            if (string.IsNullOrWhiteSpace(r.Description)) Warn(where, "Room has no description.");
        }

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        if (a.FindRoom(a.StartRoomId) != null) { queue.Enqueue(a.StartRoomId); reachable.Add(a.StartRoomId); }
        while (queue.Count > 0)
        {
            var room = a.FindRoom(queue.Dequeue());
            if (room == null) continue;
            foreach (var e in room.Exits)
                if (reachable.Add(e.TargetRoomId)) queue.Enqueue(e.TargetRoomId);
        }
        var teleportTargets = a.Triggers.SelectMany(t => t.Actions).Concat(a.Puzzles.SelectMany(p => p.OnSolved))
            .Where(x => x.Type is ActionType.GoTo).Select(x => x.A).Concat(a.Triggers.SelectMany(t => t.Actions).Where(x => x.Type == ActionType.SetExit).Select(x => x.Text))
            .Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in a.Rooms.Where(r => !reachable.Contains(r.Id) && !teleportTargets.Contains(r.Id)))
            issues.Add(new(IssueSeverity.Info, $"Room {r.Id}", "No exit or GoTo action leads here."));

        foreach (var i in a.Items)
        {
            var where = $"Item {i.Id}";
            if (!Locations.IsNowhere(i.Location) && i.Location is not (Locations.Carried or Locations.Worn) &&
                a.FindRoom(i.Location) == null && a.FindItem(i.Location) == null)
                Error(where, $"Location \"{i.Location}\" is not a room or item.");
            if (string.Equals(i.Location, i.Id, StringComparison.OrdinalIgnoreCase)) Error(where, "An item cannot be inside itself.");
            if (i.KeyItemId != null && a.FindItem(i.KeyItemId) == null) Error(where, $"Key \"{i.KeyItemId}\" does not exist.");
            if (i.Lockable && i.KeyItemId == null && i.IsLocked) Warn(where, "Locked, but no key item is set.");
            CheckPicture(where, i.PictureId);
            if (string.IsNullOrWhiteSpace(i.Name)) Warn(where, "Item has no name.");
            foreach (var t in i.Topics) { CheckConditions(where + " topic", t.Conditions); CheckActions(where + " topic", t.Actions); }
        }

        foreach (var npc in a.Items.Where(i => i.Npc != null))
        {
            var where = $"NPC {npc.Id}";
            var b = npc.Npc!;
            if (!npc.IsCharacter) Warn(where, "Has NPC behaviour but 'Is Character' is off, so it won't act.");
            foreach (var r in b.Route.Concat(b.AllowedRooms))
                if (a.FindRoom(r) == null) Error(where, $"Room \"{r}\" (route / allowed rooms) does not exist.");
            if (b.Movement == NpcMovement.Patrol && b.Route.Count < 2) Warn(where, "Patrol needs at least two rooms in its Route.");
            foreach (var w in b.Wants.Concat(b.StealsItems).Concat(b.CollectsOnly))
                if (a.FindItem(w) == null) Error(where, $"Item \"{w}\" does not exist.");
            if ((b.Hostile || b.RetaliatesWhenAttacked) && a.Settings.PlayerHealth <= 0)
                Warn(where, "Hostile, but Game › Settings › Player Health is 0, so its attacks do no harm.");
            if (b.Movement == NpcMovement.Patrol)
                for (int k = 0; k + 1 < b.Route.Count; k++)
                    if (a.FindRoom(b.Route[k]) is { } rk && !rk.Exits.Any(e => string.Equals(e.TargetRoomId, b.Route[k + 1], StringComparison.OrdinalIgnoreCase)))
                        issues.Add(new(IssueSeverity.Info, where, $"No direct exit from {b.Route[k]} to {b.Route[k + 1]}; it will walk the shortest route if there is one (NPCs only move through exits)."));
            CheckActions(where, b.OnAccept.Concat(b.OnDefeat));
        }

        foreach (var ev in a.RandomEvents)
        {
            var where = $"Random event {ev.Id}";
            foreach (var r in ev.Rooms) if (a.FindRoom(r) == null) Error(where, $"Room \"{r}\" does not exist.");
            if (ev.Chance <= 0 && ev.ChancePerThousand <= 0) Warn(where, "Chance is 0, so it will only happen through RunRandomEvent.");
            if (ev.Actions.Count == 0 && string.IsNullOrWhiteSpace(ev.WitnessMessage) && string.IsNullOrWhiteSpace(ev.DistantMessage)) Warn(where, "Does nothing.");
            CheckConditions(where, ev.Conditions);
            CheckActions(where, ev.Actions);
        }
        Duplicates(a.RandomEvents, e => e.Id, "Random events");

        foreach (var t in a.Triggers)
        {
            var where = $"Trigger {t.Id}";
            if (!string.IsNullOrEmpty(t.RoomId) && a.FindRoom(t.RoomId) == null) Error(where, $"Room \"{t.RoomId}\" does not exist.");
            if (t.Actions.Count == 0 && t.Event != TriggerEvent.Subroutine) Warn(where, "Trigger has no actions.");
            CheckConditions(where, t.Conditions);
            CheckActions(where, t.Actions);
        }
        foreach (var p in a.Puzzles)
        {
            var where = $"Puzzle {p.Id}";
            if (p.SolvedWhen.Count == 0 && !a.Triggers.Any(t => t.Actions.Any(x => x.Type == ActionType.SolvePuzzle && x.A == p.Id)))
                Warn(where, "Puzzle has no 'solved when' conditions and no trigger solves it.");
            CheckConditions(where, p.SolvedWhen);
            CheckActions(where, p.OnSolved);
        }
        foreach (var s in a.Sounds)
            if (!a.Assets.ContainsKey(s.AssetName)) Error($"Sound {s.Id}", $"Audio file \"{s.AssetName}\" is missing.");
        foreach (var p in a.Pictures)
        {
            if (!string.IsNullOrEmpty(p.BitmapAsset) && !a.Assets.ContainsKey(p.BitmapAsset)) Error($"Picture {p.Id}", $"Image \"{p.BitmapAsset}\" is missing.");
            foreach (var c in p.Commands.Where(c => c.Op == DrawOp.Call))
                if (a.FindPicture(c.SubPictureId) == null) Error($"Picture {p.Id}", $"Calls missing picture \"{c.SubPictureId}\".");
        }

        foreach (var v in a.Vocabulary.Verbs)
        {
            if (string.IsNullOrWhiteSpace(v.Id)) Error("Commands", "A command has no id.");
            try
            {
                foreach (var g in v.Grammar) GrammarLine.Parse(g, v.Id, v.Words);
            }
            catch (FormatException ex)
            {
                Error($"Command {v.Id}", ex.Message);
            }
        }
        return issues;

        void CheckConditions(string where, IEnumerable<Condition> conditions)
        {
            foreach (var c in conditions)
            {
                switch (c.Type)
                {
                    case ConditionType.PlayerIn:
                    case ConditionType.RoomVisited:
                        foreach (var r in (c.A ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
                            if (!IsRoomPseudo(r.Trim()) && a.FindRoom(r.Trim()) == null) Error(where, $"Condition {c.Type}: room \"{r}\" does not exist.");
                        break;
                    case ConditionType.ItemCarried or ConditionType.ItemWorn or ConditionType.ItemPresent or ConditionType.ItemIn
                        or ConditionType.ItemExists or ConditionType.ItemOpen or ConditionType.ItemLocked or ConditionType.ItemLit:
                        if (!IsDynamic(c.A) && a.FindItem(c.A) == null) Error(where, $"Condition {c.Type}: item \"{c.A}\" does not exist.");
                        break;
                    case ConditionType.PuzzleSolved:
                        if (a.FindPuzzle(c.A) == null) Error(where, $"Condition PuzzleSolved: puzzle \"{c.A}\" does not exist.");
                        break;
                    case ConditionType.VarEquals or ConditionType.VarGreater or ConditionType.VarLess:
                        if (!IsVariable(c.A)) Warn(where, $"Variable \"{c.A}\" is not declared (it starts at 0).");
                        break;
                }
            }
        }

        void CheckActions(string where, IEnumerable<GameAction> actions)
        {
            foreach (var x in actions)
            {
                switch (x.Type)
                {
                    case ActionType.GoTo:
                        if (!IsRoomPseudo(x.A) && a.FindRoom(x.A) == null) Error(where, $"GoTo: room \"{x.A}\" does not exist.");
                        break;
                    case ActionType.SetNpc or ActionType.NpcGoTo or ActionType.NpcSay:
                        if (!IsDynamic(x.A) && a.FindItem(x.A) is not { IsCharacter: true }) Error(where, $"{x.Type}: character \"{x.A}\" does not exist.");
                        if (x.Type == ActionType.NpcGoTo && !IsRoomPseudo(x.B) && a.FindRoom(x.B) == null) Error(where, $"NpcGoTo: room \"{x.B}\" does not exist.");
                        break;
                    case ActionType.Flood or ActionType.SetTrap or ActionType.ClearTrap or ActionType.SetRoomFlag or ActionType.SetDark:
                        if (!IsRoomPseudo(x.A) && a.FindRoom(x.A) == null) Error(where, $"{x.Type}: room \"{x.A}\" does not exist.");
                        break;
                    case ActionType.RunRandomEvent:
                        if (a.FindRandomEvent(x.A) == null) Error(where, $"RunRandomEvent: event \"{x.A}\" does not exist.");
                        break;
                    case ActionType.MoveItem or ActionType.TakeItem or ActionType.DropItem or ActionType.WearItem or ActionType.UnwearItem
                        or ActionType.DestroyItem or ActionType.CreateItem or ActionType.SwapItems or ActionType.SetOpen
                        or ActionType.SetLocked or ActionType.SetLit or ActionType.SetItemDescription:
                        if (!IsDynamic(x.A) && a.FindItem(x.A) == null) Error(where, $"{x.Type}: item \"{x.A}\" does not exist.");
                        break;
                    case ActionType.PlaySound:
                        CheckSound(where, x.A);
                        break;
                    case ActionType.ShowPicture:
                        CheckPicture(where, x.A);
                        break;
                    case ActionType.SolvePuzzle:
                        if (a.FindPuzzle(x.A) == null) Error(where, $"SolvePuzzle: puzzle \"{x.A}\" does not exist.");
                        break;
                    case ActionType.RunTrigger or ActionType.EnableTrigger or ActionType.DisableTrigger:
                        if (!a.Triggers.Any(t => string.Equals(t.Id, x.A, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, x.A, StringComparison.OrdinalIgnoreCase)))
                            Error(where, $"{x.Type}: trigger \"{x.A}\" does not exist.");
                        break;
                }
            }
        }

        bool IsDynamic(string? id) => id != null && id.StartsWith('$');
        bool IsRoomPseudo(string? id) => id is Locations.Here or Locations.EventRoom or Locations.RandomRoom or "$npcroom";
        bool IsVariable(string? name) => name != null && (name.StartsWith('@') || a.FindVariable(name) != null);
    }
}
