using AdventureCreator.Core.Model;
using AdventureCreator.Core.Parsing;

namespace AdventureCreator.Core.Engine;

public sealed partial class GameEngine
{
    // ================================================================ executing a command

    private void ExecuteCommand(ParsedCommand cmd, List<Item>? forced1, List<Item>? forced2)
    {
        // "tell robot to go north" / "ask robot to …"
        if (cmd.ActionId == "order" && cmd.Text != null && cmd.Object1 != null)
        {
            var inner = Parser.Parse(cmd.Text);
            foreach (var c in inner.Commands)
            {
                c.Addressee = cmd.Object1;
                ExecuteCommand(c, null, null);
                if (pending != null || State.GameOver) return;
            }
            if (inner.Error != null) Say(inner.Error == "UNKNOWN" ? Msg(Engine.Msg.UnknownWord, null, ("word", inner.UnknownWord ?? "")) : inner.Error);
            return;
        }

        var ctx0 = new CommandContext(this, cmd);

        if (cmd.Addressee != null)
        {
            var who = Resolve(cmd.Addressee, SlotKind.Person, cmd, isSecond: false);
            if (who.Items.Count != 1 || !who.Items[0].IsCharacter)
            {
                Say(who.Items.Count == 0 ? Msg(Engine.Msg.NotHere, ctx0) : "You can only talk to people.");
                return;
            }
            ctx0.Actor = who.Items[0];
        }

        if (cmd.ActionId == "undo") { DoUndo(); return; }

        // Resolve objects.
        List<Item> items1 = new();
        List<Item> items2 = new();
        if (cmd.Object1 != null)
        {
            if (forced1 != null) items1 = forced1;
            else
            {
                var r = Resolve(cmd.Object1, cmd.Object1Slot ?? SlotKind.Noun, cmd, isSecond: false);
                if (r.Ambiguous.Count > 0) { AskWhich(cmd, false, r.Ambiguous); return; }
                if (r.Error != null && r.Items.Count == 0 && !r.NotFound) { Say(r.Error); return; }
                items1 = r.Items;
                ctx0.Self1 = r.Self;
                ctx0.Object1Missing = r.NotFound;
            }
            ctx0.Word1 = cmd.Object1.Simple().FirstOrDefault()?.Noun ?? cmd.Object1.Simple().FirstOrDefault()?.Adjectives.LastOrDefault();
        }
        if (cmd.Object2 != null)
        {
            if (forced2 != null) items2 = forced2;
            else
            {
                var r = Resolve(cmd.Object2, cmd.Object2Slot ?? SlotKind.Noun, cmd, isSecond: true, items1);
                if (r.Ambiguous.Count > 0) { AskWhich(cmd, true, r.Ambiguous); return; }
                if (r.Error != null && r.Items.Count == 0 && !r.NotFound) { Say(r.Error); return; }
                items2 = r.Items;
                ctx0.Object2Missing = r.NotFound;
            }
            ctx0.Word2 = cmd.Object2.Noun ?? cmd.Object2.Adjectives.LastOrDefault();
            ctx0.Item2 = items2.FirstOrDefault();
        }

        bool multi = items1.Count > 1;
        if (items1.Count == 0)
        {
            RunPipeline(ctx0);
        }
        else
        {
            foreach (var item in items1)
            {
                if (State.GameOver) break;
                var ctx = new CommandContext(this, cmd)
                {
                    Item1 = item, Item2 = ctx0.Item2, Word1 = ctx0.Word1, Word2 = ctx0.Word2,
                    Actor = ctx0.Actor, Object2Missing = ctx0.Object2Missing,
                };
                if (multi) Say(char.ToUpperInvariant(item.Name[0]) + item.Name[1..] + ": ", newline: false);
                RunPipeline(ctx);
            }
        }

        // Pronouns
        if (items1.Count == 1)
        {
            var it = items1[0];
            if (it.IsCharacter) { State.Him = it.Id; State.Her = it.Id; }
            if (it.Plural) State.Them = new() { it.Id }; else State.It = new() { it.Id };
        }
        else if (items1.Count > 1) State.Them = items1.Select(i => i.Id).ToList();

        if (!cmd.IsMeta && !State.GameOver) EndTurn(ctx0);
    }

    private void RunPipeline(CommandContext ctx)
    {
        var cmd = ctx.Command!;
        if (Adventure.Settings.ExitsBeforeTriggers && ctx.Actor == null && cmd.Direction != null && cmd.Object1 == null &&
            FindExit(State.CurrentRoomId, cmd.Direction) != null)
        {
            if (Go(cmd.Direction, ctx) && !State.GameOver) RunCommandTriggers(TriggerEvent.AfterCommand, ctx);
            return;
        }

        var before = RunCommandTriggers(TriggerEvent.BeforeCommand, ctx);
        if (State.GameOver) return;
        bool handled = before.Handled || (Adventure.Settings.LegacyTableSemantics && before.AnyFired && !before.ContinueRequested);
        if (handled) return;

        if (ctx.Actor != null)
        {
            Say($"{Cap(ctx.Actor.WithDefinite())} ignores you.");
            return;
        }

        if (ctx.Item1 != null && ctx.Item1.VerbResponses.TryGetValue(cmd.ActionId ?? "", out var response) && !string.IsNullOrWhiteSpace(response))
        {
            Say(Format(response, ctx));
            RunCommandTriggers(TriggerEvent.AfterCommand, ctx);
            return;
        }

        if (cmd.VerbId == null || (cmd.Lenient && !IsLooseVerb(cmd)))
        {
            Say(cmd.GrammarError ?? Msg(Engine.Msg.DontUnderstand, ctx));
            return;
        }

        var unknown = cmd.Object1?.Simple().SelectMany(p => p.UnknownWords).Concat(cmd.Object2?.Simple().SelectMany(p => p.UnknownWords) ?? Enumerable.Empty<string>()).FirstOrDefault();
        if (unknown != null && (ctx.Object1Missing || ctx.Object2Missing))
        {
            Say(Msg(Engine.Msg.UnknownWord, ctx, ("word", unknown)));
            lastFailedInput = currentInput;
            lastUnknownWord = unknown;
            return;
        }

        if (ctx.Object1Missing && !ctx.Self1)
        {
            Say(IsDark() ? Msg(Engine.Msg.CantSeeInDark, ctx) : Msg(Engine.Msg.NotHere, ctx));
            return;
        }
        if (ctx.Object2Missing)
        {
            Say(IsDark() ? Msg(Engine.Msg.CantSeeInDark, ctx) : Msg(Engine.Msg.NotHere, ctx));
            return;
        }

        var spec = Lexicon.FindVerb(cmd.ActionId ?? cmd.VerbId);
        bool success;
        bool customHasDefault = spec?.Definition is { } d && (!string.IsNullOrWhiteSpace(d.DefaultResponse) || d.DefaultActions.Count > 0);
        if (cmd.Direction != null && spec != null && !spec.IsBuiltIn && !customHasDefault)
        {
            // Direction verbs from imported games ("north" defined as a verb): move.
            success = Go(cmd.Direction, ctx);
        }
        else if (spec?.Definition != null && !spec.IsBuiltIn)
        {
            success = RunCustomVerb(spec.Definition, ctx);
        }
        else
        {
            success = BuiltIn(ctx);
        }
        if (success && !State.GameOver) RunCommandTriggers(TriggerEvent.AfterCommand, ctx);
    }

    /// <summary>Verbs without author grammar accept anything loosely (legacy/custom games).</summary>
    private bool IsLooseVerb(ParsedCommand cmd)
    {
        var spec = Lexicon.FindVerb(cmd.VerbId!);
        return spec != null && !spec.IsBuiltIn && spec.Lines.Count == 0;
    }

    private bool RunCustomVerb(VerbDefinition def, CommandContext ctx)
    {
        bool any = false;
        if (!string.IsNullOrWhiteSpace(def.DefaultResponse))
        {
            Say(Format(def.DefaultResponse, ctx));
            any = true;
        }
        if (def.DefaultActions.Count > 0)
        {
            ExecuteActions(def.DefaultActions, ctx);
            any = true;
        }
        if (!any) Say(Msg(Engine.Msg.CantDoThat, ctx));
        return any;
    }

    private void EndTurn(CommandContext ctx)
    {
        State.Turns++;
        var turnCtx = new CommandContext(this, ctx.Command) { Item1 = ctx.Item1, Item2 = ctx.Item2, Word1 = ctx.Word1, Word2 = ctx.Word2 };
        RunEventTriggers(TriggerEvent.Timer, turnCtx);
        if (State.GameOver) return;
        RunEventTriggers(TriggerEvent.EveryTurn, turnCtx);
        if (State.GameOver) return;
        CheckPuzzles(turnCtx);
    }

    private void CheckPuzzles(CommandContext ctx)
    {
        foreach (var p in Adventure.Puzzles)
        {
            if (State.SolvedPuzzles.Contains(p.Id) || p.SolvedWhen.Count == 0) continue;
            if (CheckConditions(p.SolvedWhen, ctx)) SolvePuzzle(p, ctx);
            if (State.GameOver) return;
        }
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ================================================================ object resolution

    private sealed class Resolution
    {
        public List<Item> Items = new();
        public List<Item> Ambiguous = new();
        public bool NotFound;
        public bool Self;
        public string? Error;
    }

    private Resolution Resolve(NounPhrase np, SlotKind slot, ParsedCommand cmd, bool isSecond, List<Item>? other = null)
    {
        var res = new Resolution();
        var scope = Scope();
        foreach (var part in np.Simple())
        {
            if (part.IsSelf) { res.Self = true; continue; }
            if (part.Pronoun != null)
            {
                var ids = part.Pronoun switch
                {
                    "it" => State.It,
                    "them" or "they" => State.Them.Count > 0 ? State.Them : State.It,
                    "him" => State.Him != null ? new List<string> { State.Him } : new(),
                    "her" => State.Her != null ? new List<string> { State.Her } : new(),
                    _ => new List<string>(),
                };
                var found = ids.Select(Adventure.FindItem).Where(i => i != null && scope.Contains(i!)).Select(i => i!).ToList();
                if (found.Count == 0)
                {
                    res.Error = ids.Count == 0 ? $"I'm not sure what \"{part.Pronoun}\" refers to." : Msg(Engine.Msg.NotHere, null);
                    res.NotFound = ids.Count > 0;
                    continue;
                }
                res.Items.AddRange(found);
                continue;
            }

            if (part.All)
            {
                var candidates = AllCandidates(slot, cmd, isSecond, scope);
                if (part.Noun != null || part.Adjectives.Count > 0) candidates = candidates.Where(i => Matches(i, part)).ToList();
                foreach (var ex in part.Except.Concat(np.Except))
                    candidates = candidates.Where(i => !Matches(i, ex)).ToList();
                if (candidates.Count == 0)
                {
                    res.Error = cmd.ActionId == "take" ? Msg(Engine.Msg.NothingToTake, null)
                        : slot == SlotKind.MultiHeld ? Msg(Engine.Msg.NothingToDrop, null) : "There's nothing suitable.";
                    continue;
                }
                res.Items.AddRange(candidates);
                continue;
            }

            var matches = scope.Where(i => Matches(i, part)).ToList();
            if (matches.Count == 0)
            {
                res.NotFound = true;
                continue;
            }
            if (part.Plural || part.Count > 1)
            {
                var picked = part.Count is > 1 ? matches.Take(part.Count.Value) : matches;
                res.Items.AddRange(picked);
                continue;
            }
            if (part.Ordinal != 0 && matches.Count > 1)
            {
                res.Items.Add(part.Ordinal == -1 ? matches[^1] : matches[Math.Clamp(part.Ordinal - 1, 0, matches.Count - 1)]);
                continue;
            }
            if (matches.Count > 1) matches = Prefer(matches, slot, cmd, part, other);
            if (matches.Count > 1)
            {
                // Indistinguishable duplicates: just pick one.
                if (matches.All(m => m.Name == matches[0].Name)) matches = new List<Item> { matches[0] };
                else
                {
                    res.Ambiguous = matches;
                    return res;
                }
            }
            res.Items.Add(matches[0]);
        }
        res.Items = res.Items.Distinct().ToList();
        if (res.Items.Count > 0) res.NotFound = false;
        return res;
    }

    private List<Item> AllCandidates(SlotKind slot, ParsedCommand cmd, bool isSecond, List<Item> scope)
    {
        if (slot == SlotKind.MultiHeld || cmd.ActionId is "drop" or "insert" or "puton" or "give" or "throw")
            return ItemsAt(Locations.Carried).ToList();
        if (cmd.ActionId is "take" or "remove" or "pick")
        {
            IEnumerable<Item> pool = scope;
            if (cmd.Object2 != null && !isSecond)
            {
                var from = Resolve(cmd.Object2, SlotKind.Noun, cmd, true);
                if (from.Items.Count == 1) pool = ItemsAt(from.Items[0].Id);
            }
            else pool = ItemsAt(State.CurrentRoomId);
            return pool.Where(i => i.Portable && !i.Scenery && !i.IsCharacter && !IsCarried(i)).ToList();
        }
        return scope.Where(i => !i.Scenery && !i.IsCharacter).ToList();
    }

    private List<Item> Prefer(List<Item> matches, SlotKind slot, ParsedCommand cmd, NounPhrase part, List<Item>? other)
    {
        int Score(Item i)
        {
            int s = 0;
            // Exact use of the item's own adjectives counts for a lot.
            s += part.Adjectives.Count(a => i.Adjectives.Any(x => Lexicon.Equivalent(x, a))) * 4;
            if (part.Noun != null && NounsOf(i).FirstOrDefault() is { } primary && Lexicon.Equivalent(primary, part.Noun)) s += 1;
            bool held = IsCarried(i);
            if (slot is SlotKind.Held or SlotKind.MultiHeld || cmd.ActionId is "drop" or "wear" or "insert" or "puton" or "give" or "throw" or "eat")
                s += held ? 3 : 0;
            if (cmd.ActionId is "take" or "pick") s += held ? 0 : 3;
            if (slot == SlotKind.Person) s += i.IsCharacter ? 5 : 0;
            if (cmd.ActionId is "unlock" or "lock" && other == null) s += i.Lockable ? 2 : 0;
            if (other != null && other.Contains(i)) s -= 10;
            if (i.Scenery) s -= 1;
            return s;
        }
        var scored = matches.Select(m => (m, s: Score(m))).ToList();
        int best = scored.Max(x => x.s);
        return scored.Where(x => x.s == best).Select(x => x.m).ToList();
    }

    public bool Matches(Item item, NounPhrase phrase)
    {
        var nouns = NounsOf(item);
        bool nounOk;
        if (phrase.Noun != null)
        {
            nounOk = nouns.Any(n => Lexicon.Equivalent(n, phrase.Noun)) ||
                     string.Equals(item.Id, phrase.Noun, StringComparison.OrdinalIgnoreCase);
            if (!nounOk && !phrase.Noun.Contains(' '))
            {
                // Last word of a multi-word noun ("panel" for "control panel")
                nounOk = nouns.Any(n => n.Contains(' ') && Lexicon.Equivalent(n.Split(' ')[^1], phrase.Noun));
            }
            if (!nounOk) return false;
        }
        else if (phrase.Adjectives.Count == 0) return phrase.All;

        var nameWords = item.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var adj in phrase.Adjectives)
        {
            bool ok = item.Adjectives.Any(a => Lexicon.Equivalent(a, adj)) ||
                      nameWords.Any(w => Lexicon.Equivalent(w, adj)) ||
                      nouns.Any(n => n.Split(' ').Any(p => Lexicon.Equivalent(p, adj)));
            if (!ok) return false;
        }
        return true;
    }
}
