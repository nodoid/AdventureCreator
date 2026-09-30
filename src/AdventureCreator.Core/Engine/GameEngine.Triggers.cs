using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Engine;

public enum ActionFlow
{
    /// <summary>All actions ran.</summary>
    Normal,
    /// <summary>Stop; the command has been dealt with.</summary>
    Done,
    /// <summary>Let the built-in behaviour and later triggers run.</summary>
    Continue,
}

public readonly record struct TriggerRunResult(bool AnyFired, bool Handled, bool ContinueRequested);

public sealed partial class GameEngine
{
    private List<Trigger>? sortedTriggers;

    private List<Trigger> SortedTriggers =>
        sortedTriggers ??= Adventure.Triggers
            .Select((t, i) => (t, i))
            .OrderByDescending(x => x.t.Priority).ThenBy(x => x.i)
            .Select(x => x.t).ToList();

    public bool IsTriggerEnabled(Trigger t) => State.TriggerEnabled.TryGetValue(t.Id, out var e) ? e : t.Enabled;

    // ================================================================ running triggers

    /// <summary>Runs BeforeCommand or AfterCommand triggers for a parsed command.</summary>
    internal TriggerRunResult RunCommandTriggers(TriggerEvent ev, CommandContext ctx) =>
        RunTriggers(ev, ctx, t => MatchesCommand(t, ctx), isCommand: true);

    /// <summary>Runs triggers for non-command events (room entry, every turn, …).</summary>
    public TriggerRunResult RunEventTriggers(TriggerEvent ev, CommandContext ctx)
    {
        return RunTriggers(ev, ctx, t => ev switch
        {
            TriggerEvent.EnterRoom or TriggerEvent.LeaveRoom or TriggerEvent.BeforeEnterRoom =>
                string.IsNullOrEmpty(t.RoomId) || string.Equals(t.RoomId, ctx.EventRoomId, StringComparison.OrdinalIgnoreCase),
            TriggerEvent.ItemTaken or TriggerEvent.ItemDropped or TriggerEvent.PuzzleSolved =>
                IsAny(t.Subject) || string.Equals(t.Subject, ctx.EventSubject, StringComparison.OrdinalIgnoreCase),
            TriggerEvent.NpcArrives or TriggerEvent.NpcLeaves or TriggerEvent.NpcDefeated or TriggerEvent.PlayerHurt =>
                (IsAny(t.Subject) || string.Equals(t.Subject, ctx.EventSubject, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(t.RoomId) || string.Equals(t.RoomId, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase)),
            TriggerEvent.ItemGiven =>
                (IsAny(t.Subject) || string.Equals(t.Subject, ctx.EventSubject, StringComparison.OrdinalIgnoreCase)) &&
                (IsAny(t.Noun1) || (ctx.Item1 != null && (string.Equals(t.Noun1, ctx.Item1.Id, StringComparison.OrdinalIgnoreCase) || NounsOf(ctx.Item1).Any(n => Lexicon.Equivalent(n, t.Noun1))))),
            TriggerEvent.Timer =>
                (t.Turn > 0 && State.Turns == t.Turn) || (t.Interval > 0 && State.Turns > 0 && State.Turns % t.Interval == 0),
            TriggerEvent.EveryTurn => (string.IsNullOrEmpty(t.RoomId) || string.Equals(t.RoomId, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
                                      && MatchesCommand(t, ctx, allowNoCommand: true),
            _ => string.IsNullOrEmpty(t.RoomId) || string.Equals(t.RoomId, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase),
        }, isCommand: false);
    }

    private TriggerRunResult RunTriggers(TriggerEvent ev, CommandContext ctx, Func<Trigger, bool> match, bool isCommand)
    {
        bool anyFired = false, handled = false, continueRequested = false;
        string? stoppedGroup = null;
        bool stoppedAll = false;
        if (++triggerDepth > 32)
        {
            triggerDepth--;
            Say("[Trigger recursion limit reached.]", TextStyle.Error);
            return default;
        }
        try
        {
            foreach (var t in SortedTriggers.ToList())
            {
                if (t.Event != ev || !IsTriggerEnabled(t)) continue;
                if (stoppedAll) break;
                if (stoppedGroup != null && string.Equals(t.Group, stoppedGroup, StringComparison.OrdinalIgnoreCase)) continue;
                if (t.OnceOnly && State.FiredTriggers.Contains(t.Id)) continue;
                if (!match(t)) continue;
                if (!CheckConditions(t.Conditions, ctx)) continue;

                anyFired = true;
                State.FiredTriggers.Add(t.Id);
                var flow = ExecuteActions(t.Actions, ctx);
                if (State.GameOver) return new TriggerRunResult(true, true, false);

                if (flow == ActionFlow.Done)
                {
                    handled = true;
                    if (isCommand) break;
                    if (Adventure.Settings.LegacyTableSemantics)
                    {
                        if (t.Group == null) stoppedAll = true; else stoppedGroup = t.Group;
                    }
                    continue;
                }
                if (flow == ActionFlow.Continue) { continueRequested = true; continue; }
                if (isCommand && t.StopsCommand && !Adventure.Settings.LegacyTableSemantics)
                {
                    handled = true;
                    break;
                }
            }
        }
        finally
        {
            triggerDepth--;
        }
        return new TriggerRunResult(anyFired, handled, continueRequested);
    }

    private static bool IsAny(string? pattern) => string.IsNullOrEmpty(pattern) || pattern == "*";

    private bool MatchesCommand(Trigger t, CommandContext ctx, bool allowNoCommand = false)
    {
        var cmd = ctx.Command;
        if (cmd == null) return allowNoCommand || (IsAny(t.Verb) && IsAny(t.Noun1) && IsAny(t.Noun2));

        // Orders to characters: only triggers naming the character (or "*") see them.
        if (ctx.Actor != null)
        {
            if (string.IsNullOrEmpty(t.Subject)) return false;
            if (t.Subject != "*" && !string.Equals(t.Subject, ctx.Actor.Id, StringComparison.OrdinalIgnoreCase)) return false;
        }
        else if (!string.IsNullOrEmpty(t.Subject) && t.Event is TriggerEvent.BeforeCommand or TriggerEvent.AfterCommand) return false;

        if (!string.IsNullOrEmpty(t.RoomId) && !string.Equals(t.RoomId, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase)) return false;

        if (!IsAny(t.Verb))
        {
            if (t.Verb == "-") { if (cmd.VerbId != null) return false; }
            else if (!VerbMatches(t.Verb!, cmd)) return false;
        }
        if (!NounMatches(t.Noun1, ctx.Item1, ctx.Word1, cmd.Object1 != null || ctx.Self1, cmd)) return false;
        if (!NounMatches(t.Noun2, ctx.Item2, ctx.Word2, cmd.Object2 != null, cmd)) return false;
        if (!WordSlotMatches(t.Adverb, cmd.Adverbs)) return false;
        if (!WordSlotMatches(t.Preposition, cmd.Prepositions)) return false;
        return true;
    }

    private bool VerbMatches(string pattern, Parsing.ParsedCommand cmd)
    {
        foreach (var alt in pattern.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(alt, cmd.ActionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alt, cmd.VerbId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alt, cmd.VerbWords, StringComparison.OrdinalIgnoreCase))
                return true;
            // Direction verbs: a trigger on "north" matches "go north" / "n".
            if (cmd.Direction != null && (string.Equals(alt, cmd.Direction, StringComparison.OrdinalIgnoreCase) || Lexicon.Direction(alt) == cmd.Direction) && cmd.ActionId == "go")
                return true;
            if (Lexicon.Equivalent(alt, cmd.VerbWords.Split(' ')[0]) && Adventure.Settings.LegacyTableSemantics)
                return true;
            // Legacy vocabularies may use the same word number for a verb as a direction noun ("n" as verb).
            if (Adventure.Settings.LegacyTableSemantics && cmd.VerbId == null && cmd.Object1?.Noun is { } n && Lexicon.Equivalent(alt, n))
                return true;
        }
        return false;
    }

    private bool NounMatches(string? pattern, Item? item, string? word, bool present, Parsing.ParsedCommand cmd)
    {
        if (IsAny(pattern)) return true;
        if (pattern == "-") return !present;
        if (!present && item == null && word == null)
        {
            // Directions typed as nouns in legacy games ("go north": noun north)
            if (cmd.Direction != null && (Lexicon.Equivalent(pattern, cmd.Direction) || Lexicon.Direction(pattern!) == cmd.Direction)) return true;
            return false;
        }
        foreach (var alt in pattern!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (item != null)
            {
                if (string.Equals(alt, item.Id, StringComparison.OrdinalIgnoreCase)) return true;
                if (NounsOf(item).Any(n => Lexicon.Equivalent(n, alt))) return true;
            }
            if (word != null && Lexicon.Equivalent(word, alt)) return true;
        }
        return false;
    }

    private bool WordSlotMatches(string? pattern, List<string> used)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (pattern == "*") return used.Count > 0;
        if (pattern == "-") return used.Count == 0;
        return pattern.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(alt => used.Any(u => Lexicon.Equivalent(u, alt)));
    }

    // ================================================================ conditions

    public bool CheckConditions(IEnumerable<Condition> conditions, CommandContext ctx)
    {
        foreach (var c in conditions)
            if (Evaluate(c, ctx) == c.Negate) return false;
        return true;
    }

    private string? ItemArg(string? arg, CommandContext ctx) => arg switch
    {
        "$noun1" => ctx.Item1?.Id ?? FindItemByWord(ctx.Word1)?.Id,
        "$noun2" => ctx.Item2?.Id ?? FindItemByWord(ctx.Word2)?.Id,
        "$actor" => ctx.Actor?.Id,
        "$npc" => ctx.Npc?.Id,
        "$randomitem" => RandomItemIn(ctx.EventRoomId ?? State.CurrentRoomId),
        "$randomcarried" => ItemsAt(Locations.Carried).ToList() is { Count: > 0 } held ? held[Random.Next(held.Count)].Id : null,
        _ => arg,
    };

    private string? RandomItemIn(string room)
    {
        var items = ItemsAt(room).Where(i => i.Portable && !i.Scenery && !i.IsCharacter).ToList();
        return items.Count == 0 ? null : items[Random.Next(items.Count)].Id;
    }

    private string LocationArg(string? arg, CommandContext ctx) => arg switch
    {
        null or "" => Locations.Nowhere,
        Locations.Here => State.CurrentRoomId,
        Locations.EventRoom or Locations.RandomRoom or "$npcroom" => RoomArg(arg, ctx) ?? Locations.Nowhere,
        "$noun1" or "$noun2" or "$npc" => ItemArg(arg, ctx) ?? Locations.Nowhere,
        _ => arg,
    };

    /// <summary>Finds the item a legacy noun word refers to: present items first, then anywhere.</summary>
    private Item? FindItemByWord(string? word)
    {
        if (string.IsNullOrEmpty(word)) return null;
        var scope = Scope();
        return scope.FirstOrDefault(i => NounsOf(i).Any(n => Lexicon.Equivalent(n, word)))
               ?? Adventure.Items.FirstOrDefault(i => IsCarried(i) && NounsOf(i).Any(n => Lexicon.Equivalent(n, word)))
               ?? Adventure.Items.FirstOrDefault(i => NounsOf(i).Any(n => Lexicon.Equivalent(n, word)));
    }

    private bool Evaluate(Condition c, CommandContext ctx)
    {
        Item? ItemA() => Adventure.FindItem(ItemArg(c.A, ctx));
        switch (c.Type)
        {
            case ConditionType.Always: return true;
            case ConditionType.PlayerIn:
                return c.A?.Split('|').Any(r => string.Equals(RoomArg(r.Trim(), ctx), State.CurrentRoomId, StringComparison.OrdinalIgnoreCase)) ?? false;
            case ConditionType.ItemCarried: return ItemA() is { } i1 && IsCarried(i1);
            case ConditionType.ItemWorn: return ItemA() is { } i2 && IsWorn(i2);
            case ConditionType.ItemPresent: return ItemA() is { } i3 && (IsCarried(i3) || IsPresent(i3) || string.Equals(Loc(i3), State.CurrentRoomId, StringComparison.OrdinalIgnoreCase));
            case ConditionType.ItemIn:
            {
                var item = ItemA();
                if (item == null) return false;
                var where = LocationArg(c.B, ctx);
                return string.Equals(Loc(item), where, StringComparison.OrdinalIgnoreCase);
            }
            case ConditionType.ItemExists: return ItemA() is { } i4 && !Locations.IsNowhere(Loc(i4));
            case ConditionType.ItemOpen: return ItemA() is { } i5 && IsOpen(i5);
            case ConditionType.ItemLocked: return ItemA() is { } i6 && IsLocked(i6);
            case ConditionType.ItemLit: return ItemA() is { } i7 && IsLit(i7);
            case ConditionType.VarEquals: return GetVar(c.A ?? "") == c.N;
            case ConditionType.VarGreater: return GetVar(c.A ?? "") > c.N;
            case ConditionType.VarLess: return GetVar(c.A ?? "") < c.N;
            case ConditionType.VarEqualsVar: return GetVar(c.A ?? "") == GetVar(c.B ?? "") + c.N;
            case ConditionType.VarGreaterVar: return GetVar(c.A ?? "") > GetVar(c.B ?? "") + c.N;
            case ConditionType.VarLessVar: return GetVar(c.A ?? "") < GetVar(c.B ?? "") + c.N;
            case ConditionType.Chance: return Random.Next(100) < c.N;
            case ConditionType.TurnsAtLeast: return State.Turns >= c.N;
            case ConditionType.ScoreAtLeast: return State.Score >= c.N;
            case ConditionType.RoomVisited: return c.A != null && State.VisitedRooms.Contains(c.A);
            case ConditionType.PuzzleSolved: return c.A != null && State.SolvedPuzzles.Contains(c.A);
            case ConditionType.AdverbUsed:
                return ctx.Command != null && (string.IsNullOrEmpty(c.A) ? ctx.Command.Adverbs.Count > 0 : WordSlotMatches(c.A, ctx.Command.Adverbs));
            case ConditionType.AdjectiveUsed:
            {
                if (ctx.Command == null) return false;
                var adjs = (ctx.Command.Object1?.Simple().SelectMany(p => p.Adjectives) ?? Enumerable.Empty<string>())
                    .Concat(ctx.Command.Object2?.Simple().SelectMany(p => p.Adjectives) ?? Enumerable.Empty<string>()).ToList();
                return string.IsNullOrEmpty(c.A) ? adjs.Count > 0 : WordSlotMatches(c.A, adjs);
            }
            case ConditionType.WordUsed: return ctx.Command != null && WordSlotMatches(c.A, ctx.Command.Words);
            case ConditionType.PrepositionIs: return ctx.Command != null && WordSlotMatches(c.A, ctx.Command.Prepositions);
            case ConditionType.Noun1Is: return ctx.Command != null && NounMatches(c.A, ctx.Item1, ctx.Word1, ctx.Command.Object1 != null, ctx.Command);
            case ConditionType.Noun2Is: return ctx.Command != null && NounMatches(c.A, ctx.Item2, ctx.Word2, ctx.Command.Object2 != null, ctx.Command);
            case ConditionType.CarriedCountAtLeast: return ItemsAt(Locations.Carried).Count() >= c.N;
            case ConditionType.IsDark: return IsDark();
            case ConditionType.ExitOpen: return c.A != null && c.B != null && FindExit(c.A, c.B) is { } e &&
                                                (e.DoorItemId == null || (Adventure.FindItem(e.DoorItemId) is { } door && IsOpen(door)));
            case ConditionType.TriggerFired: return c.A != null && State.FiredTriggers.Contains(c.A);
            case ConditionType.NpcFollowing: return ItemA() is { } f && f.Npc != null && MovementOf(f) == NpcMovement.Follow && NpcActive(f);
            case ConditionType.NpcHostile: return ItemA() is { } h && NpcHostile(h);
            case ConditionType.NpcDefeated: return ItemA() is { } d && d.Npc != null && NpcStateOf(d).Defeated;
            case ConditionType.NpcIn: return ItemA() is { } w && string.Equals(Loc(w), LocationArg(c.B, ctx), StringComparison.OrdinalIgnoreCase);
            case ConditionType.NpcHasItem:
                return ItemA() is { } holder && Adventure.FindItem(ItemArg(c.B, ctx)) is { } thing && string.Equals(Loc(thing), holder.Id, StringComparison.OrdinalIgnoreCase);
            case ConditionType.HealthAtLeast: return State.Health >= c.N;
            case ConditionType.RoomHasFlag: return RoomHasFlag(RoomArg(c.A, ctx), c.B ?? "");
            case ConditionType.RoomFlooded: return IsFlooded(RoomArg(c.A, ctx));
            case ConditionType.RoomTrapped: return RoomArg(c.A, ctx) is { } tr && State.Traps.ContainsKey(tr);
            case ConditionType.EventHappened: return c.A != null && State.EventCounts.GetValueOrDefault(c.A) > 0;
        }
        return false;
    }

    // ================================================================ actions

    public ActionFlow ExecuteActions(IEnumerable<GameAction> actions, CommandContext ctx)
    {
        var flow = ActionFlow.Normal;
        foreach (var a in actions)
        {
            var f = Execute(a, ctx);
            if (State.GameOver) return ActionFlow.Done;
            if (f == ActionFlow.Done) return ActionFlow.Done;
            if (f == ActionFlow.Continue) flow = ActionFlow.Continue;
        }
        return flow;
    }

    private ActionFlow Execute(GameAction a, CommandContext ctx)
    {
        Item? ItemA() => Adventure.FindItem(ItemArg(a.A, ctx));
        Item? ItemB() => Adventure.FindItem(ItemArg(a.B, ctx));

        switch (a.Type)
        {
            case ActionType.Message:
                Say(Format(a.Text ?? "", ctx), newline: a.N != 1);
                break;
            case ActionType.Look:
                Describe(ctx, forceFull: true);
                break;
            case ActionType.GoTo:
                if (RoomArg(a.A, ctx) is { } goRoom) MovePlayer(goRoom, ctx, a.Text);
                break;
            case ActionType.MoveItem:
                if (ItemA() is { } mi)
                {
                    var target = LocationArg(a.B, ctx);
                    SetLoc(mi, target);
                }
                else MissingItem(a.A);
                break;
            case ActionType.TakeItem:
            {
                var item = ItemA();
                if (item == null) { Say(Msg(Engine.Msg.NotHere, ctx)); return ActionFlow.Done; }
                var itemCtx = WithItem(ctx, item);
                if (!TryTake(item, itemCtx, silent: true)) return ActionFlow.Done;
                break;
            }
            case ActionType.DropItem:
            {
                var item = ItemA();
                if (item == null || !IsCarried(item))
                {
                    Say(Msg(Engine.Msg.NotCarrying, item != null ? WithItem(ctx, item) : ctx));
                    return ActionFlow.Done;
                }
                SetLoc(item, State.CurrentRoomId);
                RunEventTriggers(TriggerEvent.ItemDropped, new CommandContext(this, ctx.Command) { EventSubject = item.Id, Item1 = item });
                break;
            }
            case ActionType.DropAll:
                foreach (var item in ItemsAt(Locations.Carried).ToList()) SetLoc(item, State.CurrentRoomId);
                break;
            case ActionType.WearItem:
            {
                var item = ItemA();
                if (item == null || !IsCarried(item)) { Say(Msg(Engine.Msg.NotCarrying, item != null ? WithItem(ctx, item) : ctx)); return ActionFlow.Done; }
                if (IsWorn(item)) { Say(Msg(Engine.Msg.CantDoThat, ctx)); return ActionFlow.Done; }
                if (!item.Wearable && !Adventure.Settings.LegacyTableSemantics) { Say(Msg(Engine.Msg.NotWearable, WithItem(ctx, item))); return ActionFlow.Done; }
                SetLoc(item, Locations.Worn);
                break;
            }
            case ActionType.UnwearItem:
            {
                var item = ItemA();
                if (item == null || !IsWorn(item)) { Say(Msg(Engine.Msg.NotWorn, item != null ? WithItem(ctx, item) : ctx)); return ActionFlow.Done; }
                if (TooManyCarried(extra: 1)) { Say(Msg(Engine.Msg.TooMany, ctx)); return ActionFlow.Done; }
                SetLoc(item, Locations.Carried);
                break;
            }
            case ActionType.DestroyItem:
                if (ItemA() is { } di) SetLoc(di, Locations.Nowhere); else MissingItem(a.A);
                break;
            case ActionType.CreateItem:
                if (ItemA() is { } ci)
                    SetLoc(ci, !string.IsNullOrEmpty(a.B) ? LocationArg(a.B, ctx) : ctx.InRandomEvent && ctx.EventRoomId != null ? ctx.EventRoomId : State.CurrentRoomId);
                else MissingItem(a.A);
                break;
            case ActionType.SwapItems:
            {
                var x = ItemA();
                var y = ItemB();
                if (x != null && y != null)
                {
                    var lx = Loc(x);
                    SetLoc(x, Loc(y));
                    SetLoc(y, lx);
                }
                break;
            }
            case ActionType.SetOpen:
                if (ItemA() is { } oi) State.ItemOpen[oi.Id] = a.N != 0;
                break;
            case ActionType.SetLocked:
                if (ItemA() is { } li) State.ItemLocked[li.Id] = a.N != 0;
                break;
            case ActionType.SetLit:
                if (ItemA() is { } lti) State.ItemLit[lti.Id] = a.N != 0;
                break;
            case ActionType.SetVar:
                SetVar(a.A ?? "", a.N);
                break;
            case ActionType.AddVar:
                SetVar(a.A ?? "", Math.Clamp((long)GetVar(a.A ?? "") + a.N, int.MinValue, int.MaxValue) is var sum ? (int)sum : 0);
                if (Adventure.Settings.LegacyTableSemantics && !(a.A ?? "").StartsWith('@'))
                    SetVar(a.A ?? "", Math.Clamp(GetVar(a.A ?? ""), 0, 255));
                break;
            case ActionType.CopyVar:
                SetVar(a.A ?? "", GetVar(a.B ?? ""));
                break;
            case ActionType.RandomVar:
                SetVar(a.A ?? "", Random.Next(1, Math.Max(1, a.N) + 1));
                break;
            case ActionType.AwardScore:
                AwardScore(a.N, a.A);
                break;
            case ActionType.SolvePuzzle:
                if (Adventure.FindPuzzle(a.A) is { } pz) SolvePuzzle(pz, ctx);
                break;
            case ActionType.PlaySound:
                PlaySound(a.A, a.N == 1);
                break;
            case ActionType.StopSound:
                StopSound();
                break;
            case ActionType.ShowPicture:
                ShowPicture(string.IsNullOrEmpty(a.A) ? CurrentRoom?.PictureId : a.A);
                break;
            case ActionType.ClearScreen:
                Emit(new OutputEvent(OutputKind.ClearScreen));
                break;
            case ActionType.Inventory:
                ShowInventory(ctx);
                break;
            case ActionType.ShowScore:
                Say(Msg(Engine.Msg.Score, ctx));
                break;
            case ActionType.ShowTurns:
                Say(Msg(Engine.Msg.Turns, ctx));
                break;
            case ActionType.Pause:
                Emit(new OutputEvent(OutputKind.Pause, Milliseconds: a.N));
                break;
            case ActionType.Beep:
                Emit(new OutputEvent(OutputKind.Beep));
                break;
            case ActionType.Win:
                EndGame(true, a.Text, ctx);
                return ActionFlow.Done;
            case ActionType.Lose:
                EndGame(false, a.Text, ctx);
                return ActionFlow.Done;
            case ActionType.Quit:
                State.GameOver = true;
                Emit(new OutputEvent(OutputKind.Quit));
                return ActionFlow.Done;
            case ActionType.Save:
                DoSave(a.Text);
                break;
            case ActionType.Restore:
                DoRestore(a.Text);
                return ActionFlow.Done;
            case ActionType.Restart:
                DoRestart();
                return ActionFlow.Done;
            case ActionType.Done:
                return ActionFlow.Done;
            case ActionType.Ok:
                Say(Msg(Engine.Msg.Ok, ctx));
                return ActionFlow.Done;
            case ActionType.Continue:
                return ActionFlow.Continue;
            case ActionType.SetExit:
                if (RoomArg(a.A, ctx) is { } exitRoom && a.B != null)
                {
                    var dir = Lexicon.Direction(a.B) ?? a.B.ToLowerInvariant();
                    State.ExitOverrides[$"{exitRoom}|{dir}"] = RoomArg(a.Text, ctx) ?? "";
                }
                break;
            case ActionType.SetRoomDescription:
                if (RoomArg(a.A, ctx) is { } descRoom) State.RoomDescriptions[descRoom] = a.Text ?? "";
                break;
            case ActionType.SetItemDescription:
                if (ItemA() is { } sdi) State.ItemDescriptions[sdi.Id] = a.Text ?? "";
                break;
            case ActionType.EnableTrigger:
                if (a.A != null) State.TriggerEnabled[a.A] = true;
                break;
            case ActionType.DisableTrigger:
                if (a.A != null) State.TriggerEnabled[a.A] = false;
                break;
            case ActionType.RunTrigger:
            {
                if (a.A == null) break;
                var r = RunTriggers(TriggerEvent.Subroutine, ctx,
                    t => (string.Equals(t.Id, a.A, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, a.A, StringComparison.OrdinalIgnoreCase))
                         && MatchesCommand(t, ctx, allowNoCommand: true),
                    isCommand: false);
                // In PAWS, DONE inside a sub-process ends the sub-process only.
                _ = r;
                break;
            }
            case ActionType.SetDark:
                if (RoomArg(a.A, ctx) is { } darkRoom)
                {
                    bool wasDark = IsDark();
                    State.RoomDark[darkRoom] = a.N != 0;
                    if (string.Equals(darkRoom, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase) && wasDark != IsDark())
                        Say(IsDark() ? Msg(Engine.Msg.Dark, ctx) : "Light floods the room.");
                }
                break;
            case ActionType.SetNpc:
                if (ItemA() is { Npc: not null } npcToSet) SetNpcProperty(npcToSet, a.B, a.N, a.Text);
                break;
            case ActionType.NpcGoTo:
                if (ItemA() is { Npc: not null } walker && RoomArg(a.B, ctx) is { } dest)
                {
                    var ns = NpcStateOf(walker);
                    ns.Destination = dest;
                    ns.Following = false;
                }
                break;
            case ActionType.NpcSay:
                if (ItemA() is { } speaker && WithPlayer(speaker) && !IsDark())
                    Say($"{Cap(speaker.WithDefinite())} says, \u201C{Format(a.Text ?? "", NpcCtx(speaker, ctx))}\u201D");
                break;
            case ActionType.HurtPlayer:
                HurtPlayer(a.N, a.Text, ctx, ctx.Npc);
                break;
            case ActionType.HealPlayer:
                if (Adventure.Settings.PlayerHealth > 0)
                    State.Health = a.N <= 0 ? Adventure.Settings.PlayerHealth : Math.Min(Adventure.Settings.PlayerHealth, State.Health + a.N);
                if (!string.IsNullOrWhiteSpace(a.Text)) Say(Format(a.Text, ctx));
                break;
            case ActionType.SetRoomFlag:
                if (RoomArg(a.A, ctx) is { } flagRoom && !string.IsNullOrEmpty(a.B))
                {
                    if (string.Equals(a.B, FloodedFlag, StringComparison.OrdinalIgnoreCase)) Flood(flagRoom, a.N != 0, ctx);
                    else SetRoomFlag(flagRoom, a.B, a.N != 0);
                }
                break;
            case ActionType.Flood:
                if (RoomArg(a.A, ctx) is { } floodRoom) Flood(floodRoom, a.N != 0, ctx);
                break;
            case ActionType.SetTrap:
                if (RoomArg(a.A, ctx) is { } trapRoom)
                    SetTrap(trapRoom, new TrapState { Damage = Math.Max(0, a.N), Deadly = a.N < 0, Message = a.Text ?? "", RevealMessage = a.B ?? "" });
                break;
            case ActionType.ClearTrap:
                if (RoomArg(a.A, ctx) is { } clearRoom) State.Traps.Remove(clearRoom);
                break;
            case ActionType.RunRandomEvent:
                if (Adventure.FindRandomEvent(a.A) is { } ev) TryFireEvent(ev, ctx, force: true);
                break;
        }
        return ActionFlow.Normal;
    }

    private void MissingItem(string? id)
    {
        if (!Adventure.Settings.LegacyTableSemantics) Say($"[Unknown item \"{id}\".]", TextStyle.Error);
    }

    private CommandContext WithItem(CommandContext ctx, Item item) =>
        new(this, ctx.Command) { Item1 = item, Item2 = ctx.Item2, Word1 = ctx.Word1, Word2 = ctx.Word2, Actor = ctx.Actor };
}
