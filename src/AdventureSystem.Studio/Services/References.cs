using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Parsing;

namespace AdventureSystem.Studio.Services;

public enum RefKind { None, Room, Item, Location, Variable, Puzzle, Trigger, Picture, Sound, Verb, Direction, Adverb, Preposition, Message, Npc, RandomEvent, NpcProperty, RoomFlag }

/// <summary>Works out what kind of id a field holds, and lists the choices for pick lists.</summary>
public static class References
{
    public static RefKind KindOf(object owner, string property)
    {
        switch (owner)
        {
            case Condition c:
                if (property == nameof(Condition.A))
                    return c.Type switch
                    {
                        ConditionType.PlayerIn or ConditionType.RoomVisited or ConditionType.ExitOpen => RefKind.Room,
                        ConditionType.ItemCarried or ConditionType.ItemWorn or ConditionType.ItemPresent or ConditionType.ItemIn or ConditionType.ItemExists
                            or ConditionType.ItemOpen or ConditionType.ItemLocked or ConditionType.ItemLit or ConditionType.Noun1Is or ConditionType.Noun2Is => RefKind.Item,
                        ConditionType.VarEquals or ConditionType.VarGreater or ConditionType.VarLess
                            or ConditionType.VarEqualsVar or ConditionType.VarGreaterVar or ConditionType.VarLessVar => RefKind.Variable,
                        ConditionType.PuzzleSolved => RefKind.Puzzle,
                        ConditionType.TriggerFired => RefKind.Trigger,
                        ConditionType.NpcFollowing or ConditionType.NpcHostile or ConditionType.NpcDefeated or ConditionType.NpcIn or ConditionType.NpcHasItem => RefKind.Npc,
                        ConditionType.RoomHasFlag or ConditionType.RoomFlooded or ConditionType.RoomTrapped => RefKind.Room,
                        ConditionType.EventHappened => RefKind.RandomEvent,
                        ConditionType.AdverbUsed => RefKind.Adverb,
                        ConditionType.PrepositionIs => RefKind.Preposition,
                        _ => RefKind.None,
                    };
                if (property == nameof(Condition.B))
                    return c.Type switch
                    {
                        ConditionType.ItemIn => RefKind.Location,
                        ConditionType.VarEqualsVar or ConditionType.VarGreaterVar or ConditionType.VarLessVar => RefKind.Variable,
                        ConditionType.ExitOpen => RefKind.Direction,
                        ConditionType.NpcIn => RefKind.Location,
                        ConditionType.NpcHasItem => RefKind.Item,
                        ConditionType.RoomHasFlag => RefKind.RoomFlag,
                        _ => RefKind.None,
                    };
                break;
            case GameAction a:
                if (property == nameof(GameAction.A))
                    return a.Type switch
                    {
                        ActionType.GoTo or ActionType.SetRoomDescription or ActionType.SetDark or ActionType.SetExit => RefKind.Room,
                        ActionType.MoveItem or ActionType.TakeItem or ActionType.DropItem or ActionType.WearItem or ActionType.UnwearItem
                            or ActionType.DestroyItem or ActionType.CreateItem or ActionType.SwapItems or ActionType.SetOpen or ActionType.SetLocked
                            or ActionType.SetLit or ActionType.SetItemDescription => RefKind.Item,
                        ActionType.SetVar or ActionType.AddVar or ActionType.CopyVar or ActionType.RandomVar => RefKind.Variable,
                        ActionType.SolvePuzzle => RefKind.Puzzle,
                        ActionType.PlaySound => RefKind.Sound,
                        ActionType.ShowPicture => RefKind.Picture,
                        ActionType.EnableTrigger or ActionType.DisableTrigger or ActionType.RunTrigger => RefKind.Trigger,
                        ActionType.SetNpc or ActionType.NpcGoTo or ActionType.NpcSay => RefKind.Npc,
                        ActionType.SetRoomFlag or ActionType.Flood or ActionType.SetTrap or ActionType.ClearTrap => RefKind.Room,
                        ActionType.RunRandomEvent => RefKind.RandomEvent,
                        _ => RefKind.None,
                    };
                if (property == nameof(GameAction.B))
                    return a.Type switch
                    {
                        ActionType.MoveItem => RefKind.Location,
                        ActionType.SwapItems => RefKind.Item,
                        ActionType.CopyVar => RefKind.Variable,
                        ActionType.SetExit => RefKind.Direction,
                        ActionType.SetNpc => RefKind.NpcProperty,
                        ActionType.NpcGoTo => RefKind.Room,
                        ActionType.SetRoomFlag => RefKind.RoomFlag,
                        _ => RefKind.None,
                    };
                break;
            case Trigger t:
                return property switch
                {
                    nameof(Trigger.Verb) => RefKind.Verb,
                    nameof(Trigger.Noun1) or nameof(Trigger.Noun2) => RefKind.Item,
                    nameof(Trigger.RoomId) => RefKind.Room,
                    nameof(Trigger.Adverb) => RefKind.Adverb,
                    nameof(Trigger.Preposition) => RefKind.Preposition,
                    nameof(Trigger.Subject) => t.Event switch
                    {
                        TriggerEvent.PuzzleSolved => RefKind.Puzzle,
                        TriggerEvent.NpcArrives or TriggerEvent.NpcLeaves or TriggerEvent.NpcDefeated or TriggerEvent.ItemGiven or TriggerEvent.PlayerHurt => RefKind.Npc,
                        TriggerEvent.BeforeCommand or TriggerEvent.AfterCommand => RefKind.Npc,
                        _ => RefKind.Item,
                    },
                    _ => RefKind.None,
                };
            case Exit:
                return property switch
                {
                    nameof(Exit.TargetRoomId) => RefKind.Room,
                    nameof(Exit.DoorItemId) => RefKind.Item,
                    nameof(Exit.Direction) => RefKind.Direction,
                    _ => RefKind.None,
                };
            case Item:
                return property switch
                {
                    nameof(Item.Location) => RefKind.Location,
                    nameof(Item.KeyItemId) => RefKind.Item,
                    nameof(Item.PictureId) => RefKind.Picture,
                    _ => RefKind.None,
                };
            case DrawCommand when property == nameof(DrawCommand.SubPictureId):
                return RefKind.Picture;
        }

        return property switch
        {
            "StartRoomId" => RefKind.Room,
            "PictureId" or "IntroPictureId" or "DefaultPictureId" => RefKind.Picture,
            "SoundId" or "IntroSoundId" => RefKind.Sound,
            _ => RefKind.None,
        };
    }

    /// <summary>(value, label) choices for a reference kind.</summary>
    public static List<(string Value, string Label)> Choices(Adventure a, RefKind kind)
    {
        var list = new List<(string, string)>();
        switch (kind)
        {
            case RefKind.Room:
                list.Add((Locations.Here, "The player's current room (@here)"));
                list.Add((Locations.EventRoom, "The random event's room (@eventroom)"));
                list.Add((Locations.RandomRoom, "A random room (@randomroom)"));
                list.AddRange(a.Rooms.Select(r => (r.Id, $"{r.Name} ({r.Id})")));
                break;
            case RefKind.Npc:
                list.Add(("*", "* (any character)"));
                list.Add(("$npc", "The NPC involved in the event ($npc)"));
                list.AddRange(a.Items.Where(i => i.IsCharacter).Select(i => (i.Id, $"{i.Name} ({i.Id}){(i.Npc == null ? "" : " – NPC")}")));
                break;
            case RefKind.RandomEvent:
                list.AddRange(a.RandomEvents.Select(e => (e.Id, $"{e.Name} ({e.Id})")));
                break;
            case RefKind.NpcProperty:
                list.AddRange(new[] { ("Movement", "Movement (Text = Stationary/Wander/Patrol/Follow/Seek/Flee)"), ("Hostile", "Hostile (N = 1/0)"),
                    ("Following", "Following the player (N = 1/0)"), ("Blocking", "Blocking exits (N = 1/0)"), ("Active", "Active (N = 1/0)"), ("Health", "Health (N)") });
                break;
            case RefKind.RoomFlag:
                list.Add(("flooded", "flooded (blocks entry without a boat; same as Flood)"));
                list.AddRange(a.Triggers.SelectMany(t => t.Actions).Where(x => x.Type == ActionType.SetRoomFlag && !string.IsNullOrEmpty(x.B)).Select(x => x.B!).Distinct().Select(f => (f, f)));
                break;
            case RefKind.Item:
                list.Add(("$noun1", "The player's first object ($noun1)"));
                list.Add(("$noun2", "The player's second object ($noun2)"));
                list.Add(("$randomitem", "A random portable item in the (event) room"));
                list.Add(("$randomcarried", "A random item the player carries"));
                list.AddRange(a.Items.Select(i => (i.Id, $"{i.Name} ({i.Id})")));
                break;
            case RefKind.Location:
                list.Add((Locations.Carried, "Carried by the player"));
                list.Add((Locations.Worn, "Worn by the player"));
                list.Add((Locations.Here, "The player's current room"));
                list.Add((Locations.EventRoom, "The random event's room (@eventroom)"));
                list.Add(("", "Nowhere (not in the game)"));
                list.AddRange(a.Rooms.Select(r => (r.Id, $"Room: {r.Name} ({r.Id})")));
                list.AddRange(a.Items.Where(i => i.Container || i.Supporter).Select(i => (i.Id, $"{(i.Supporter ? "On" : "In")}: {i.Name} ({i.Id})")));
                break;
            case RefKind.Variable:
                list.AddRange(a.Variables.Select(v => (v.Name, v.Name)));
                list.Add(("@score", "@score (the score)"));
                list.Add(("@turns", "@turns (turns taken)"));
                list.Add(("@room", "@room (index of current room)"));
                list.Add(("@carried", "@carried (number of items held)"));
                list.Add(("@health", "@health (player health)"));
                break;
            case RefKind.Puzzle:
                list.AddRange(a.Puzzles.Select(p => (p.Id, $"{p.Name} ({p.Id})")));
                break;
            case RefKind.Trigger:
                list.AddRange(a.Triggers.Select(t => (t.Id, t.ToString())));
                list.AddRange(a.Triggers.Where(t => t.Event == TriggerEvent.Subroutine && !string.IsNullOrEmpty(t.Name)).Select(t => t.Name).Distinct().Select(n => (n, $"Subroutine group \"{n}\"")));
                break;
            case RefKind.Picture:
                list.AddRange(a.Pictures.Select(p => (p.Id, $"{p.Name} ({p.Id})")));
                break;
            case RefKind.Sound:
                list.AddRange(a.Sounds.Select(s => (s.Id, $"{s.Name} ({s.Id})")));
                break;
            case RefKind.Verb:
                list.Add(("*", "* (any verb)"));
                list.AddRange(new Lexicon(a).Verbs.Values.Where(v => v.Words.Count > 0 || !v.IsBuiltIn).OrderBy(v => v.Id)
                    .Select(v => (v.Id, $"{v.Id} — {string.Join(", ", v.Words.Take(5))}")));
                break;
            case RefKind.Direction:
                list.AddRange(BuiltInLexicon.Directions.Select(d => (d.Canonical, d.Canonical)));
                list.AddRange(a.Vocabulary.Directions.Values.Distinct().Where(d => BuiltInLexicon.Directions.All(b => b.Canonical != d)).Select(d => (d, d)));
                break;
            case RefKind.Adverb:
                list.Add(("*", "* (any adverb)"));
                list.Add(("-", "- (no adverb)"));
                list.AddRange(BuiltInLexicon.Adverbs.Concat(a.Vocabulary.Adverbs).Distinct().OrderBy(x => x).Select(x => (x, x)));
                break;
            case RefKind.Preposition:
                list.AddRange(BuiltInLexicon.Prepositions.Concat(a.Vocabulary.Prepositions).Distinct().OrderBy(x => x).Select(x => (x, x)));
                break;
            case RefKind.Message:
                list.AddRange(Msg.Defaults.Keys.OrderBy(k => k).Select(k => (k, k)));
                break;
        }
        return list;
    }

    /// <summary>A short hint of what the A/B/N/Text fields mean for a condition or action type.</summary>
    public static string Hint(object owner) => owner switch
    {
        Condition c => c.Type switch
        {
            ConditionType.ItemIn => "A = item, B = location",
            ConditionType.VarEquals or ConditionType.VarGreater or ConditionType.VarLess => "A = variable, N = value",
            ConditionType.VarEqualsVar => "A, B = variables; true when A = B + N (N is an optional offset, usually 0)",
            ConditionType.VarGreaterVar => "A, B = variables; true when A > B + N (N is an optional offset, usually 0)",
            ConditionType.VarLessVar => "A, B = variables; true when A < B + N (N is an optional offset, usually 0)",
            ConditionType.Chance => "N = percent chance",
            ConditionType.TurnsAtLeast or ConditionType.ScoreAtLeast or ConditionType.CarriedCountAtLeast => "N = number",
            ConditionType.ExitOpen => "A = room, B = direction",
            ConditionType.AdverbUsed or ConditionType.AdjectiveUsed or ConditionType.WordUsed or ConditionType.PrepositionIs => "A = word(s), separate alternatives with |",
            ConditionType.Always or ConditionType.IsDark => "no arguments",
            ConditionType.NpcFollowing or ConditionType.NpcHostile or ConditionType.NpcDefeated => "A = NPC",
            ConditionType.NpcIn => "A = NPC, B = room",
            ConditionType.NpcHasItem => "A = NPC, B = item",
            ConditionType.HealthAtLeast => "N = health",
            ConditionType.RoomHasFlag => "A = room (@here, @eventroom…), B = flag name",
            ConditionType.RoomFlooded or ConditionType.RoomTrapped => "A = room (@here, @eventroom…)",
            ConditionType.EventHappened => "A = random event",
            _ => "A = id",
        },
        GameAction a => a.Type switch
        {
            ActionType.Message => "Text; {noun1}, {adverb}, {score}, {var:name}… are replaced. N = 1: no line break",
            ActionType.MoveItem => "A = item, B = location",
            ActionType.SwapItems => "A, B = items",
            ActionType.SetOpen or ActionType.SetLocked or ActionType.SetLit or ActionType.SetDark => "A = id, N = 1 (yes) or 0 (no)",
            ActionType.SetVar or ActionType.AddVar => "A = variable, N = value",
            ActionType.CopyVar => "A = destination, B = source variable",
            ActionType.RandomVar => "A = variable, N = maximum (1..N)",
            ActionType.AwardScore => "N = points, A = optional key so it is only awarded once",
            ActionType.PlaySound => "A = sound, N = 1 to loop",
            ActionType.Pause => "N = milliseconds",
            ActionType.Win or ActionType.Lose => "Text = final message",
            ActionType.SetExit => "A = room, B = direction, Text = target room (empty removes the exit)",
            ActionType.SetRoomDescription or ActionType.SetItemDescription => "A = id, Text = new description",
            ActionType.GoTo => "A = room, Text = optional travel message",
            ActionType.RunTrigger => "A = subroutine trigger id or group name",
            ActionType.CreateItem => "A = item, B = optional location (default: here, or the event's room)",
            ActionType.SetNpc => "A = NPC, B = Movement/Hostile/Following/Blocking/Active/Health, N = value, Text = movement mode",
            ActionType.NpcGoTo => "A = NPC, B = room – walks there through exits, one room per turn",
            ActionType.NpcSay => "A = NPC, Text = what it says (only heard if the player is there)",
            ActionType.HurtPlayer => "N = damage, Text = message",
            ActionType.HealPlayer => "N = amount (0 = full), Text = message",
            ActionType.SetRoomFlag => "A = room, B = flag, N = 1 on / 0 off",
            ActionType.Flood => "A = room, N = 1 flood / 0 drain",
            ActionType.SetTrap => "A = room, N = damage (-1 = deadly), Text = when sprung, B = when found",
            ActionType.ClearTrap => "A = room",
            ActionType.RunRandomEvent => "A = random event (happens now if its conditions allow)",
            _ => "A = id",
        },
        _ => "",
    };
}
