using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Engine;

/// <summary>Default behaviour of the built-in verbs (used when no trigger handles a command).</summary>
public sealed partial class GameEngine
{
    /// <summary>Returns true when the action succeeded (AfterCommand triggers then run).</summary>
    private bool BuiltIn(CommandContext ctx)
    {
        var cmd = ctx.Command!;
        var item = ctx.Item1;
        var second = ctx.Item2;
        string action = cmd.ActionId ?? cmd.VerbId ?? "";

        bool Need1()
        {
            if (item != null || ctx.Self1) return true;
            Say($"What do you want to {cmd.VerbWords}?");
            return false;
        }

        bool Say1(string text) { Say(Format(text, ctx)); return true; }

        switch (action)
        {
            // ---------------------------------------------------------- observation
            case "look":
                if (cmd.Direction != null) return Say1("You see nothing unusual in that direction.");
                Describe(ctx, forceFull: true);
                return true;

            case "examine":
                if (!Need1()) return false;
                if (ctx.Self1 || item == null) return Say1("As good-looking as ever.");
                if (IsDark() && !IsCarried(item)) { Say(Msg(Msg_.CantSeeInDark, ctx)); return false; }
                {
                    var desc = State.ItemDescriptions.TryGetValue(item.Id, out var d) ? d : item.Description;
                    if (!string.IsNullOrWhiteSpace(desc)) Say(Format(desc, ctx));
                    else if (!(item.Container || item.Supporter)) Say(Msg(Msg_.NothingSpecial, ctx));
                    if (item.Switchable || item.LightSource) Say(Cap(item.WithDefinite()) + (IsLit(item) ? " is on." : " is off."));
                    if (item.Container && item.Openable && !(ContentsVisible(item)))
                        Say(Cap(item.WithDefinite()) + " is closed.");
                    else if (item.Container || item.Supporter) ListContents(item, ctx, sayEmpty: string.IsNullOrWhiteSpace(desc));
                    if (item.PictureId != null) ShowPicture(item.PictureId);
                }
                return true;

            case "search" when item == null && !ctx.Self1:
                if (!RevealTraps(ctx)) Say(Msg(Msg_.NothingFound, ctx));
                return true;
            case "disarm":
                if (State.Traps.TryGetValue(State.CurrentRoomId, out var trapHere) && trapHere.Revealed)
                {
                    State.Traps.Remove(State.CurrentRoomId);
                    return Say1("Working very carefully, you disarm the trap.");
                }
                return Say1("You haven't found anything to disarm.") && false;
            case "diagnose":
                if (Adventure.Settings.PlayerHealth <= 0) return Say1("You feel fine.");
                Say(Msg(Engine.Msg.Health, ctx));
                return true;
            case "search":
            case "lookunder":
            case "lookbehind":
                if (!Need1() || item == null) return false;
                if (item.Container || item.Supporter)
                {
                    if (item.Container && !ContentsVisible(item)) { Say(Cap(item.WithDefinite()) + " is closed."); return false; }
                    ListContents(item, ctx, sayEmpty: true);
                    return true;
                }
                return Say1("You find nothing of interest.");

            case "inventory":
                ShowInventory(ctx);
                return true;

            case "exits":
                ListExits(ctx);
                return true;

            case "listen":
                return Say1(item == null ? "You hear nothing unexpected." : "{The noun1} makes no sound.");
            case "smell":
                return Say1(item == null ? "You smell nothing unexpected." : "{The noun1} smells as you'd expect.");
            case "touch":
                if (!Need1()) return false;
                return Say1(item!.IsCharacter ? "{The noun1} might not appreciate that." : "You feel nothing unexpected.");

            // ---------------------------------------------------------- movement
            case "go":
                if (cmd.Direction != null) return Go(cmd.Direction, ctx);
                if (item != null) return EnterItem(item, ctx);
                Say("Which way do you want to go?");
                return false;

            case "enter":
                if (item != null) return EnterItem(item, ctx);
                return Go("in", ctx);

            case "exit":
                return Go("out", ctx);

            case "climb":
                if (item == null) return Go(cmd.Prepositions.Contains("down") ? "down" : "up", ctx);
                if (FindDoorExit(item) is { } climbExit) return Go(climbExit.Direction, ctx);
                return Say1("You can't climb {the noun1}.");

            case "swim":
                if (cmd.Direction != null) return Go(cmd.Direction, ctx);
                return Say1("There's no water deep enough for swimming here.");

            case "follow":
                if (item?.Npc != null && WithPlayer(item)) return Say1("{The noun1} is right here.");
                return FollowNpc(ctx.Word1, ctx);

            // ---------------------------------------------------------- manipulation
            case "take":
            case "pick":
                if (!Need1()) return false;
                if (ctx.Self1) return Say1("You're already yourself.");
                if (second != null && !string.Equals(Loc(item!), second.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Say(Format($"{Cap(item!.WithDefinite())} isn't in {{the noun2}}.", ctx));
                    return false;
                }
                return TryTake(item!, ctx, silent: false);

            case "drop":
                if (!Need1() || item == null) return false;
                if (!IsCarried(item)) { Say(Msg(Msg_.NotCarrying, ctx)); return false; }
                if (IsWorn(item)) Say($"(first taking off {item.WithDefinite()})", TextStyle.System);
                SetLoc(item, State.CurrentRoomId);
                Say(Msg(Msg_.Dropped, ctx));
                RunEventTriggers(TriggerEvent.ItemDropped, new CommandContext(this, cmd) { EventSubject = item.Id, Item1 = item });
                return true;

            case "throw":
                if (!Need1() || item == null) return false;
                if (!EnsureHeld(item, ctx)) return false;
                SetLoc(item, State.CurrentRoomId);
                if (second != null) return Say1(second.IsCharacter ? "{The noun2} ducks as {the noun1} sails past and lands on the floor." : "{The noun1} bounces off {the noun2} and lands on the floor.");
                return Say1("You throw {the noun1}. It lands on the floor.");

            case "insert":
            case "puton":
            {
                if (!Need1() || item == null) return false;
                if (second == null) { Say($"What do you want to put {item.WithDefinite()} {(action == "insert" ? "in" : "on")}?"); return false; }
                if (second == item) return Say1("You can't put something inside itself.") && false;
                bool into = action == "insert" && !second.Supporter || (action == "puton" && second.Container && !second.Supporter && false);
                if (into && !second.Container) { Say(Msg(Msg_.NotContainer, ctx)); return false; }
                if (!into && !second.Supporter)
                {
                    if (second.Container) into = true;
                    else { Say(Format("You can't put things on {the noun2}.", ctx)); return false; }
                }
                if (into && !IsOpen(second)) { Say(Msg(Msg_.ContainerClosed, ctx)); return false; }
                if (ItemsAt(second.Id).Count() >= Math.Max(1, second.Capacity)) { Say(Msg(Msg_.ContainerFull, ctx)); return false; }
                if (IsInside(second, item)) { Say("You can't do that – it would be inside itself."); return false; }
                if (!EnsureHeld(item, ctx)) return false;
                SetLoc(item, second.Id);
                Say(Msg(into ? Msg_.PutIn : Msg_.PutOn, ctx));
                return true;
            }

            case "wear":
                if (!Need1() || item == null) return false;
                if (!item.Wearable) { Say(Msg(Msg_.NotWearable, ctx)); return false; }
                if (IsWorn(item)) return Say1("You're already wearing {the noun1}.") && false;
                if (!EnsureHeld(item, ctx)) return false;
                SetLoc(item, Locations.Worn);
                Say(Msg(Msg_.Worn, ctx));
                return true;

            case "remove":
                if (!Need1() || item == null) return false;
                if (!IsWorn(item))
                {
                    if (!IsCarried(item) && item.Portable) return TryTake(item, ctx, silent: false);
                    Say(Msg(Msg_.NotWorn, ctx));
                    return false;
                }
                SetLoc(item, Locations.Carried);
                Say(Msg(Msg_.Removed, ctx));
                return true;

            case "open":
                if (!Need1() || item == null) return false;
                if (!item.Openable) { Say(Msg(Msg_.NotOpenable, ctx)); return false; }
                if (IsOpen(item)) { Say(Msg(Msg_.AlreadyOpen, ctx)); return false; }
                if (IsLocked(item)) { Say(Msg(Msg_.IsLocked, ctx)); return false; }
                State.ItemOpen[item.Id] = true;
                Say(Msg(Msg_.Opened, ctx));
                if (item.Container) ListContents(item, ctx, sayEmpty: false);
                return true;

            case "close":
                if (!Need1() || item == null) return false;
                if (!item.Openable) { Say(Msg(Msg_.NotOpenable, ctx)); return false; }
                if (!IsOpen(item)) { Say(Msg(Msg_.AlreadyClosed, ctx)); return false; }
                State.ItemOpen[item.Id] = false;
                Say(Msg(Msg_.Closed, ctx));
                return true;

            case "unlock":
            case "lock":
            {
                if (!Need1() || item == null) return false;
                bool unlocking = action == "unlock";
                if (!item.Lockable) { Say(Format("{The noun1} doesn't have a lock.", ctx)); return false; }
                if (unlocking && !IsLocked(item)) return Say1("{The noun1} isn't locked.") && false;
                if (!unlocking && IsLocked(item)) return Say1("{The noun1} is already locked.") && false;
                if (!unlocking && IsOpen(item)) return Say1("You'll have to close {the noun1} first.") && false;
                var key = second;
                if (key == null && item.KeyItemId != null && Adventure.FindItem(item.KeyItemId) is { } k && IsCarried(k))
                {
                    key = k;
                    ctx.Item2 = key;
                    Say($"(with {key.WithDefinite()})", TextStyle.System);
                }
                if (key == null) { Say(Msg(Msg_.NoKey, ctx)); return false; }
                if (!string.Equals(item.KeyItemId, key.Id, StringComparison.OrdinalIgnoreCase)) { Say(Msg(Msg_.WrongKey, ctx)); return false; }
                if (!EnsureHeld(key, ctx)) return false;
                State.ItemLocked[item.Id] = !unlocking;
                Say(Msg(unlocking ? Msg_.Unlocked : Msg_.Locked, ctx));
                return true;
            }

            case "switchon":
            case "switchoff":
            case "switch":
            case "light":
            case "extinguish":
            {
                if (!Need1() || item == null) return false;
                bool on = action switch { "switchon" or "light" => true, "switchoff" or "extinguish" => false, _ => !IsLit(item) };
                if (!item.Switchable && !item.LightSource) { Say(Msg(Msg_.NotSwitchable, ctx)); return false; }
                if (IsLit(item) == on) return Say1(on ? "{The noun1} is already on." : "{The noun1} is already off.") && false;
                bool wasDark = IsDark();
                State.ItemLit[item.Id] = on;
                Say(Msg(on ? Msg_.SwitchedOn : Msg_.SwitchedOff, ctx));
                if (wasDark && !IsDark()) Describe(ctx, forceFull: true);
                return true;
            }

            case "eat":
                if (!Need1() || item == null) return false;
                if (!item.Edible) { Say(Msg(Msg_.NotEdible, ctx)); return false; }
                if (!EnsureHeld(item, ctx)) return false;
                SetLoc(item, Locations.Nowhere);
                Say(Msg(Msg_.Eaten, ctx));
                return true;

            case "drink":
                if (!Need1() || item == null) return false;
                if (!item.Drinkable) return Say1("You can't drink that.") && false;
                SetLoc(item, Locations.Nowhere);
                Say(Msg(Msg_.Drunk, ctx));
                return true;

            case "read":
                if (!Need1() || item == null) return false;
                if (IsDark()) { Say(Msg(Msg_.CantSeeInDark, ctx)); return false; }
                if (item.Readable && !string.IsNullOrWhiteSpace(item.ReadText)) { Say(Format(item.ReadText, ctx)); return true; }
                if (!string.IsNullOrWhiteSpace(item.Description) && item.Readable) { Say(Format(item.Description, ctx)); return true; }
                Say(Msg(Msg_.NothingToRead, ctx));
                return false;

            case "fill":
                return Say1("There's nothing suitable to fill {the noun1} with.") && false;
            case "empty":
            {
                if (!Need1() || item == null) return false;
                if (!item.Container) return Say1("{The noun1} can't be emptied.") && false;
                if (!IsOpen(item)) { Say(Format("{The noun1} is closed.", ctx)); return false; }
                var contents = ItemsAt(item.Id).ToList();
                if (contents.Count == 0) { Say(Msg(Msg_.Empty, ctx)); return false; }
                var target = second != null && (second.Container || second.Supporter) ? second.Id : State.CurrentRoomId;
                foreach (var c in contents) SetLoc(c, target);
                return Say1("You empty {the noun1}.");
            }

            // ---------------------------------------------------------- people
            case "ask":
            case "tell":
            case "talk":
            case "answer":
            {
                var person = item ?? ctx.Item2;
                if (person == null)
                {
                    person = Scope().FirstOrDefault(i => i.IsCharacter);
                    if (person == null) { Say("There's nobody here to talk to."); return false; }
                    ctx.Item1 = person;
                }
                if (!person.IsCharacter) { Say(Format("{The noun1} isn't much of a conversationalist.", ctx)); return false; }
                var topicWords = (cmd.Text ?? (action == "talk" ? "hello" : "")).ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Lexicon.Normalize).ToList();
                if (action == "talk") topicWords.AddRange(new[] { "hello", "talk", "greeting", "hi" });
                foreach (var topic in person.Topics)
                {
                    if (!topic.Keywords.Any(k => topicWords.Any(w => Lexicon.Equivalent(w, k) || k.Contains(' ') && string.Join(' ', topicWords).Contains(k, StringComparison.OrdinalIgnoreCase)))) continue;
                    if (!CheckConditions(topic.Conditions, ctx)) continue;
                    if (!string.IsNullOrWhiteSpace(topic.Response)) Say(Format(topic.Response, ctx));
                    ExecuteActions(topic.Actions, ctx);
                    return true;
                }
                Say(action == "talk" ? Msg(Msg_.NoReply, ctx) : Format("{The noun1} doesn't seem to know anything about that.", ctx));
                return false;
            }

            case "askfor" when item?.Npc != null && second != null:
                return AskNpcFor(item, second, ctx);
            case "askfor":
                return Say1(item?.IsCharacter == true ? "{The noun1} doesn't seem inclined to give you anything." : "There's nobody to ask.") && false;

            case "give":
            case "show":
                if (!Need1() || item == null) return false;
                if (second == null) { Say($"Who do you want to {action} {item.WithDefinite()} to?"); return false; }
                if (!second.IsCharacter) return Say1("{The noun2} can't accept things.") && false;
                if (action == "give" && second.Npc != null && GiveToNpc(item, second, ctx)) return true;
                if (action == "give" && !EnsureHeld(item, ctx)) return false;
                return Say1(action == "give" ? "{The noun2} doesn't seem interested." : "{The noun2} glances at {the noun1} but says nothing.") && false;

            case "say":
            case "shout":
            case "sing":
                if (cmd.Text == null) return Say1(action == "sing" ? "You hum a little tune." : "You shout. Nothing happens.");
                if (Scope().FirstOrDefault(i => i.IsCharacter) is { } listener && listener.Topics.Count > 0)
                {
                    ctx.Item1 = listener;
                    foreach (var topic in listener.Topics)
                        if (topic.Keywords.Any(k => cmd.Text.Contains(k, StringComparison.OrdinalIgnoreCase)) && CheckConditions(topic.Conditions, ctx))
                        {
                            Say(Format(topic.Response, ctx));
                            ExecuteActions(topic.Actions, ctx);
                            return true;
                        }
                }
                return Say1("You say \"{text}\". Nothing happens.");

            case "kiss":
                return Say1(item?.IsCharacter == true ? "{The noun1} blushes." : "Keep your mind on the game.");
            case "wake":
                return Say1(item == null ? "The dreadful truth is, this is not a dream." : "{The noun1} is wide awake already.");

            // ---------------------------------------------------------- force
            case "attack":
            case "kick":
            case "break":
                if (!Need1()) return false;
                if (item?.Npc != null && AttackNpc(item, second is { Damage: > 0 } ? second : null, ctx)) return true;
                return Say1(cmd.Adverbs.Count > 0 ? "Even {adverb}, violence isn't the answer to this one." : Msg(Msg_.Violence, ctx));

            case "push":
            case "pull":
            case "turn":
                if (!Need1() || item == null) return false;
                if (item.IsCharacter) return Say1("{The noun1} might not like that.");
                if (!item.Portable || item.Scenery) return Say1("{The noun1} won't budge.");
                return Say1("Nothing obvious happens.");

            case "rub":
                return Need1() && Say1("You achieve nothing by this.");
            case "cut":
            case "tie":
            case "untie":
            case "dig":
            case "burn":
            case "blow":
            case "play":
            case "use":
            case "type":
            case "write":
            case "buy":
            case "knock":
            case "wave":
            case "fill ":
                return Say1(action switch
                {
                    "dig" => "The ground is too hard to dig here.",
                    "burn" => "That's not a good idea.",
                    "knock" => "Nobody answers.",
                    "wave" => "You wave. Nothing happens.",
                    "buy" => "Nothing here is for sale.",
                    "use" => item != null ? "You'll have to be more specific about how you want to use {the noun1}." : "Use what?",
                    "type" or "write" => "There's nothing to write on.",
                    _ => "You can't see how that would help.",
                }) && false;

            // ---------------------------------------------------------- miscellaneous
            case "wait":
                Say(Msg(Msg_.Waited, ctx));
                return true;
            case "sleep": return Say1("You aren't feeling especially drowsy.");
            case "jump": return Say1("You jump on the spot, fruitlessly.");
            case "sit": return Say1(item != null ? "You sit on {the noun1} for a while. It isn't very comfortable." : "You sit down for a moment, then get back up.");
            case "stand": return Say1("You are standing.");
            case "lie": return Say1("This is no time to lie down.");
            case "pray": return Say1("Nothing happens. Perhaps you should try harder.");
            case "think": return Say1("What a good idea.");
            case "dance": return Say1("You dance a little jig.");
            case "cry": return Say1("There, there. It can't be that bad.");
            case "laugh": return Say1("You laugh heartily.");
            case "hide": return Say1("There's nowhere good to hide.");
            case "yes": case "no": return Say1("That was a rhetorical question.");
            case "sorry": return Say1("Oh, don't apologise.");
            case "xyzzy": return Say1("A hollow voice says \"Fool.\"");

            // ---------------------------------------------------------- meta
            case "score":
                Say(Msg(Msg_.Score, ctx));
                ListPuzzleProgress();
                return true;
            case "turns":
                Say(Msg(Msg_.Turns, ctx));
                return true;
            case "hint":
                ShowHint(ctx);
                return true;
            case "help":
                ShowHelp();
                return true;
            case "vocabulary":
                ShowVocabulary();
                return true;
            case "verbose":
                State.Verbose = true;
                return Say1("Maximum verbosity: full room descriptions every time.");
            case "brief":
                State.Verbose = false;
                return Say1("Brief mode: full descriptions on the first visit only.");
            case "save":
                DoSave(cmd.Text);
                return true;
            case "restore":
                DoRestore(cmd.Text);
                return true;
            case "restart":
                DoRestart();
                return true;
            case "quit":
                if (HostHandlesSaveDialogs)
                {
                    // The host asks "Are you sure?" and then closes (or shows its quit screen).
                    Emit(new OutputEvent(OutputKind.Quit, QuitConfirm));
                    return true;
                }
                Say("Thanks for playing.", TextStyle.System);
                State.GameOver = true;
                Emit(new OutputEvent(OutputKind.Quit));
                return true;
        }

        Say(Msg(Msg_.CantDoThat, ctx));
        return false;
    }

    // alias so the switch reads nicely
    private static class Msg_
    {
        public const string CantSeeInDark = Engine.Msg.CantSeeInDark, NothingSpecial = Engine.Msg.NothingSpecial, NotCarrying = Engine.Msg.NotCarrying,
            Dropped = Engine.Msg.Dropped, NotContainer = Engine.Msg.NotContainer, ContainerClosed = Engine.Msg.ContainerClosed,
            ContainerFull = Engine.Msg.ContainerFull, PutIn = Engine.Msg.PutIn, PutOn = Engine.Msg.PutOn, NotWearable = Engine.Msg.NotWearable,
            Worn = Engine.Msg.Worn, NotWorn = Engine.Msg.NotWorn, Removed = Engine.Msg.Removed, NotOpenable = Engine.Msg.NotOpenable,
            AlreadyOpen = Engine.Msg.AlreadyOpen, IsLocked = Engine.Msg.IsLocked, Opened = Engine.Msg.Opened, AlreadyClosed = Engine.Msg.AlreadyClosed,
            Closed = Engine.Msg.Closed, NoKey = Engine.Msg.NoKey, WrongKey = Engine.Msg.WrongKey, Unlocked = Engine.Msg.Unlocked, Locked = Engine.Msg.Locked,
            NotSwitchable = Engine.Msg.NotSwitchable, SwitchedOn = Engine.Msg.SwitchedOn, SwitchedOff = Engine.Msg.SwitchedOff,
            NotEdible = Engine.Msg.NotEdible, Eaten = Engine.Msg.Eaten, Drunk = Engine.Msg.Drunk, NothingToRead = Engine.Msg.NothingToRead,
            Empty = Engine.Msg.Empty, NoReply = Engine.Msg.NoReply, Violence = Engine.Msg.Violence, Waited = Engine.Msg.Waited,
            Score = Engine.Msg.Score, Turns = Engine.Msg.Turns, CantDoThat = Engine.Msg.CantDoThat, NothingFound = Engine.Msg.NothingFound;
    }

    // ================================================================ helpers

    private void ListContents(Item container, CommandContext ctx, bool sayEmpty)
    {
        var inside = ItemsAt(container.Id).Where(i => !i.Scenery).Select(i => i.WithArticle()).ToList();
        var c = WithItem(ctx, container);
        if (inside.Count > 0) Say(Msg(container.Supporter ? Engine.Msg.On : Engine.Msg.Inside, c, ("list", JoinList(inside))));
        else if (sayEmpty) Say(Msg(Engine.Msg.Empty, c));
    }

    private bool IsInside(Item candidateInner, Item outer)
    {
        var loc = Loc(candidateInner);
        for (int guard = 0; guard < 32 && !Locations.IsNowhere(loc); guard++)
        {
            if (string.Equals(loc, outer.Id, StringComparison.OrdinalIgnoreCase)) return true;
            var parent = Adventure.FindItem(loc);
            if (parent == null) return false;
            loc = Loc(parent);
        }
        return false;
    }

    private bool TooManyCarried(int extra = 0)
    {
        int max = Adventure.Settings.MaxCarriedItems;
        return max > 0 && ItemsAt(Locations.Carried).Count() + extra > max;
    }

    private int CarriedWeight() =>
        Adventure.Items.Where(IsHeldIndirectly).Sum(i => i.Weight);

    /// <summary>Picks up an item with all the usual checks. Prints "Taken." unless silent.</summary>
    public bool TryTake(Item item, CommandContext ctx, bool silent)
    {
        if (IsCarried(item)) { Say(Msg(Engine.Msg.AlreadyCarrying, ctx)); return false; }
        if (!IsPresent(item) && !string.Equals(Loc(item), State.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            Say(Msg(Engine.Msg.NotHere, ctx));
            return false;
        }
        if (item.IsCharacter) { Say(Format("{The noun1} wouldn't care for that.", ctx)); return false; }
        if (Adventure.FindItem(Loc(item)) is { IsCharacter: true } holder)
        {
            Say(Format($"{Cap(holder.WithDefinite())} won't let you have {{the noun1}}.", ctx));
            return false;
        }
        if (!item.Portable || item.Scenery) { Say(Msg(Engine.Msg.CantTake, ctx)); return false; }
        if (TooManyCarried(extra: 1)) { Say(Msg(Engine.Msg.TooMany, ctx)); return false; }
        int maxWeight = Adventure.Settings.MaxCarriedWeight;
        if (maxWeight > 0 && !IsHeldIndirectly(item) && CarriedWeight() + TotalWeight(item) > maxWeight)
        {
            Say(Msg(Engine.Msg.TooHeavy, ctx));
            return false;
        }
        SetLoc(item, Locations.Carried);
        if (!silent) Say(Msg(Engine.Msg.Taken, ctx));
        if (State.TakenOnce.Add(item.Id) && item.ScoreOnTake != 0) AwardScore(item.ScoreOnTake, "take:" + item.Id);
        RunEventTriggers(TriggerEvent.ItemTaken, new CommandContext(this, ctx.Command) { EventSubject = item.Id, Item1 = item });
        return true;
    }

    private int TotalWeight(Item item) => item.Weight + ItemsAt(item.Id).Sum(TotalWeight);

    /// <summary>Implicitly picks up an item needed for an action ("(first taking the key)").</summary>
    private bool EnsureHeld(Item item, CommandContext ctx)
    {
        if (IsCarried(item)) return true;
        if (!item.Portable || item.Scenery || item.IsCharacter)
        {
            Say(Msg(Engine.Msg.NotCarrying, WithItem(ctx, item)));
            return false;
        }
        Say($"(first taking {item.WithDefinite()})", TextStyle.System);
        return TryTake(item, WithItem(ctx, item), silent: true);
    }

    private Exit? FindDoorExit(Item item) =>
        ExitsOf(State.CurrentRoomId).FirstOrDefault(e => string.Equals(e.DoorItemId, item.Id, StringComparison.OrdinalIgnoreCase));

    private bool EnterItem(Item item, CommandContext ctx)
    {
        if (FindDoorExit(item) is { } exit) return Go(exit.Direction, ctx);
        // Exit named after the item's noun (e.g. exit direction "portal")
        foreach (var n in NounsOf(item))
            if (FindExit(State.CurrentRoomId, n) is { } e2) return Go(e2.Direction, ctx);
        Say(Format("You can't go into {the noun1}.", ctx));
        return false;
    }

    public bool Go(string direction, CommandContext ctx)
    {
        var exit = FindExit(State.CurrentRoomId, direction);
        if (exit == null || string.IsNullOrEmpty(exit.TargetRoomId))
        {
            Say(Msg(Engine.Msg.CantGo, ctx, ("direction", direction)));
            return false;
        }
        if (BlockerFor(exit.Direction) is { } blocker)
        {
            Say(NpcText(blocker, blocker.Npc!.BlockMessage, "{The npc} blocks your way.", NpcCtx(blocker, ctx)));
            return false;
        }
        if (IsFlooded(exit.TargetRoomId) && !PlayerCanSwim())
        {
            Say(Msg(Engine.Msg.Flooded, ctx, ("room", Adventure.FindRoom(exit.TargetRoomId)?.Name ?? "That way")));
            return false;
        }
        if (exit.DoorItemId != null && Adventure.FindItem(exit.DoorItemId) is { } door && !IsOpen(door))
        {
            Say(IsLocked(door) ? Msg(Engine.Msg.IsLocked, WithItem(ctx, door))
                : Msg(Engine.Msg.DoorClosed, ctx, ("door", door.WithDefinite()), ("The door", Cap(door.WithDefinite()))));
            return false;
        }
        if (!CheckConditions(exit.Conditions, ctx))
        {
            Say(string.IsNullOrWhiteSpace(exit.BlockedMessage) ? Msg(Engine.Msg.CantGo, ctx) : Format(exit.BlockedMessage, ctx));
            return false;
        }
        MovePlayer(exit.TargetRoomId, ctx, exit.TravelMessage);
        return true;
    }

    private void ShowHint(CommandContext ctx)
    {
        var candidates = Adventure.Puzzles
            .Where(p => !State.SolvedPuzzles.Contains(p.Id) && p.Hints.Count > 0)
            .Where(p => p.HintRoomIds.Count == 0 || p.HintRoomIds.Contains(State.CurrentRoomId, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(p => p.HintRoomIds.Contains(State.CurrentRoomId, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) { Say(Msg(Engine.Msg.NoHints, ctx)); return; }
        var puzzle = candidates[0];
        State.HintsShown.TryGetValue(puzzle.Id, out var shown);
        var hint = puzzle.Hints[Math.Min(shown, puzzle.Hints.Count - 1)];
        State.HintsShown[puzzle.Id] = shown + 1;
        Say($"Hint ({(string.IsNullOrEmpty(puzzle.Name) ? "puzzle" : puzzle.Name)}, {Math.Min(shown + 1, puzzle.Hints.Count)}/{puzzle.Hints.Count}): {Format(hint, ctx)}", TextStyle.System);
    }

    private void ListPuzzleProgress()
    {
        if (Adventure.Puzzles.Count == 0) return;
        int solved = Adventure.Puzzles.Count(p => State.SolvedPuzzles.Contains(p.Id));
        Say($"Puzzles solved: {solved} of {Adventure.Puzzles.Count}.", TextStyle.System);
    }

    private void ShowHelp()
    {
        Say("Type commands in plain English, for example: LOOK, EXAMINE THE LAMP, TAKE ALL, PUT THE COIN IN THE SLOT, " +
            "UNLOCK THE DOOR WITH THE BRASS KEY, GO NORTH (or just N), ASK THE WIZARD ABOUT THE CROWN, QUIETLY OPEN THE DOOR. " +
            "You can chain commands: TAKE LAMP AND GO NORTH THEN LIGHT IT.", TextStyle.System);
        Say("Useful commands: INVENTORY (I), LOOK (L), EXITS, SCORE, HINT, SAVE, RESTORE, UNDO, AGAIN (G), OOPS <word>, VERBOSE, BRIEF, RESTART, QUIT, VOCABULARY.", TextStyle.System);
        var custom = Adventure.Vocabulary.Verbs.Where(v => !string.IsNullOrWhiteSpace(v.Help)).ToList();
        foreach (var v in custom) Say($"{v.Words.FirstOrDefault()?.ToUpperInvariant()}: {v.Help}", TextStyle.System);
    }

    private void ShowVocabulary()
    {
        var verbs = Lexicon.Verbs.Values.Where(v => v.Words.Count > 0 && !v.IsMeta)
            .Select(v => v.Words[0]).OrderBy(w => w).ToList();
        Say("Verbs I know: " + string.Join(", ", verbs) + ".", TextStyle.System);
    }

    // ================================================================ saving and loading

    public const string AutosaveSlot = "autosave";

    /// <summary>Text of a <see cref="OutputKind.Quit"/> event that the host should confirm with the player first.</summary>
    public const string QuitConfirm = "confirm";

    /// <summary>
    /// Set by hosts that show their own save/load dialogs: SAVE or RESTORE without a name then raises
    /// <see cref="OutputKind.SaveRequested"/> / <see cref="OutputKind.RestoreRequested"/> instead of using a default slot.
    /// </summary>
    public bool HostHandlesSaveDialogs { get; set; }

    /// <summary>Saves the current position to a named slot.</summary>
    public SaveGame SaveToSlot(string name)
    {
        name = CleanSlotName(name);
        if (story != null) State.ZState = Convert.ToBase64String(story.SaveState());
        var save = new SaveGame
        {
            Name = name, GameTitle = Adventure.Title, SavedAt = DateTime.Now, RoomName = LocationName,
            Score = State.Score, MaxScore = Adventure.ComputeMaxScore(), Turns = State.Turns, State = State.Serialize(),
        };
        SaveStorage.Save(name, save.Serialize());
        return save;
    }

    public SaveGame? ReadSave(string name)
    {
        var data = SaveStorage.Load(CleanSlotName(name));
        return data == null ? null : SaveGame.Parse(data, name);
    }

    public bool HasSave(string name) => ReadSave(name) != null;

    public void DeleteSave(string name) => SaveStorage.Delete(CleanSlotName(name));

    /// <summary>All saved positions, newest first. The autosave is included when <paramref name="includeAutosave"/> is set.</summary>
    public List<SaveGame> ListSaves(bool includeAutosave = true) =>
        SaveStorage.Slots().Select(ReadSave).Where(s => s != null).Select(s => s!)
            .Where(s => includeAutosave || !s.IsAutosave)
            .OrderByDescending(s => s.SavedAt).ToList();

    /// <summary>Saves silently to the autosave slot (hosts call this after each turn). Nothing is saved once the game is over.</summary>
    public void Autosave()
    {
        if (story is { HasQuit: true }) { try { SaveStorage.Delete(AutosaveSlot); } catch { } return; }
        try
        {
            if (State.GameOver) SaveStorage.Delete(AutosaveSlot);
            else SaveToSlot(AutosaveSlot);
        }
        catch { /* autosave is best effort */ }
    }

    private static string CleanSlotName(string name)
    {
        name = name.Trim().Trim('"', '\u201C', '\u201D', '\'').Trim();
        return name.Length == 0 ? "default" : name.Length > 60 ? name[..60] : name;
    }

    private void DoSave(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (HostHandlesSaveDialogs) { Emit(new OutputEvent(OutputKind.SaveRequested)); return; }
            name = "default";
        }
        try
        {
            var save = SaveToSlot(name);
            Say(Msg(Engine.Msg.Saved, null) + (save.Name == "default" ? "" : $" (\u201C{save.Name}\u201D)"), TextStyle.System);
        }
        catch (Exception ex)
        {
            Say("Save failed: " + ex.Message, TextStyle.Error);
        }
    }

    private void DoRestore(string? name = null)
    {
        SaveGame? save;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (HostHandlesSaveDialogs) { Emit(new OutputEvent(OutputKind.RestoreRequested)); return; }
            var saves = ListSaves();
            if (saves.Count == 0) { Say("There is no saved game.", TextStyle.System); return; }
            if (saves.Count > 1 && !saves.Any(s => s.Name == "default"))
            {
                Say("Saved games:\n" + string.Join("\n", saves.Select(s => "  " + s.Summary)) + "\nType RESTORE followed by the name.", TextStyle.System);
                return;
            }
            save = saves.FirstOrDefault(s => s.Name == "default") ?? saves[0];
        }
        else save = ReadSave(name);

        if (save == null) { Say($"There is no saved game called \u201C{name}\u201D.", TextStyle.System); return; }
        State = GameState.Deserialize(save.State);
        if (story != null) { RestoreStory(save); return; }
        pending = null;
        Say(Msg(Engine.Msg.Restored, null) + (save.Name is "default" ? "" : $" (\u201C{(save.IsAutosave ? "autosave" : save.Name)}\u201D)"), TextStyle.System);
        Describe(null, forceFull: true);
        if (State.CurrentSoundId != null) PlaySound(State.CurrentSoundId, true);
    }

    private void DoRestart()
    {
        Emit(new OutputEvent(OutputKind.Restart));
        Emit(new OutputEvent(OutputKind.ClearScreen));
        StopSound();
        StartCore();
    }

    private void DoUndo()
    {
        if (!Adventure.Settings.AllowUndo || undo.Count == 0) { Say(Msg(Engine.Msg.NothingToUndo, null), TextStyle.System); return; }
        State = GameState.Deserialize(undo.Pop());
        Say(Msg(Engine.Msg.Undone, null), TextStyle.System);
        Describe(null, forceFull: false);
    }
}
