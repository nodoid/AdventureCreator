using System.Text.RegularExpressions;
using System.Xml.Linq;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Quest;

/// <summary>Quest scripts → triggers.</summary>
internal sealed partial class QuestConverter
{
    /// <summary>What names mean inside a script: "this", the command's objects, the room.</summary>
    private sealed record Scope(string? This, bool Command, string? RoomId);

    /// <summary>A trigger whose actions are the converted script; branches become subroutine triggers.</summary>
    private Trigger ScriptTrigger(string id, string name, TriggerEvent ev, XElement scriptElement, XElement? thisObject, string? roomId = null, bool command = false)
    {
        var t = new Trigger { Id = id, Name = name, Event = ev, StopsCommand = false };
        if (roomId != null && ev is TriggerEvent.AfterDescribe or TriggerEvent.EnterRoom or TriggerEvent.LeaveRoom or TriggerEvent.BeforeEnterRoom) t.RoomId = roomId;
        var thisId = thisObject?.Attribute("name")?.Value is { } n ? (roomIds.TryGetValue(n, out var r) ? r : itemIds.TryGetValue(n, out var i) ? i : null) : null;
        var scope = new Scope(thisId, command, roomId);
        var notes = new List<string>();
        t.Actions.AddRange(Statements(QuestScript.Parse(scriptElement.Value), scope, notes, t.Id));
        if (notes.Count > 0) t.Notes = "Not converted: " + string.Join(" | ", notes.Distinct());
        return t;
    }

    private List<GameAction> Statements(List<QStatement> statements, Scope scope, List<string> notes, string owner)
    {
        var list = new List<GameAction>();
        foreach (var st in statements) Statement(st, list, scope, notes, owner);
        return list;
    }

    private void NotConverted(QStatement st, List<string> notes, string kind)
    {
        notes.Add(st.Source.Length > 120 ? st.Source[..120] + "…" : st.Source);
        Skip(kind);
    }

    private void Statement(QStatement st, List<GameAction> list, Scope scope, List<string> notes, string owner)
    {
        switch (st)
        {
            case QCall call: Call(call, list, scope, notes); break;
            case QAssign assign: Assign(assign, list, scope, notes); break;
            case QIf branch: If(branch.Branches, branch.Else, list, scope, notes, owner); break;
            case QSwitch sw:
                If(sw.Cases.Select(c => ((QExpr)new QBinary("=", sw.Subject, c.Value), c.Body)).ToList(), sw.Default, list, scope, notes, owner);
                break;
            case QFirstTime first:
            {
                // The first time: a once-only subroutine. Otherwise: one that needs the first to have happened.
                var once = Sub(owner, "first", first.Body, scope, notes);
                once.OnceOnly = true;
                if (first.Otherwise != null)
                {
                    var later = Sub(owner, "otherwise", first.Otherwise, scope, notes);
                    later.Conditions.Add(new Condition(ConditionType.TriggerFired, once.Id));
                    list.Add(new GameAction(ActionType.RunTrigger, later.Id));
                }
                list.Add(new GameAction(ActionType.RunTrigger, once.Id));
                break;
            }
            default: NotConverted(st, notes, "Script statements (loops, handlers, unknown syntax)"); break;
        }
    }

    private Trigger Sub(string owner, string what, List<QStatement> body, Scope scope, List<string> notes)
    {
        var t = new Trigger { Id = $"{owner}_{what}{++nextSub}", Name = $"{owner}: {what}", Event = TriggerEvent.Subroutine, StopsCommand = false };
        t.Actions.AddRange(Statements(body, scope, notes, owner));
        a.Triggers.Add(t);
        return t;
    }

    /// <summary>
    /// if / else if / else: each branch is a subroutine guarded by a variable, so only the first true branch runs,
    /// even when an earlier branch changes what later conditions would see.
    /// </summary>
    private void If(List<(QExpr Condition, List<QStatement> Body)> branches, List<QStatement>? elseBody, List<GameAction> list, Scope scope, List<string> notes, string owner)
    {
        var guard = Var($"{owner}_if{++nextSub}");
        list.Add(new GameAction(ActionType.SetVar, guard, 0));
        foreach (var (condition, body) in branches)
        {
            var dnf = Conditions(condition, scope);
            if (dnf == null)
            {
                notes.Add("if (" + Describe(condition) + ") – condition not converted, so this branch never runs");
                Skip("Conditions that can't be converted");
                continue;
            }
            foreach (var term in dnf)
            {
                var t = Sub(owner, "if", body, scope, notes);
                t.Conditions.Add(new Condition(ConditionType.VarEquals, guard, 0));
                t.Conditions.AddRange(term);
                t.Actions.Insert(0, new GameAction(ActionType.SetVar, guard, 1));
                list.Add(new GameAction(ActionType.RunTrigger, t.Id));
            }
        }
        if (elseBody != null)
        {
            var t = Sub(owner, "else", elseBody, scope, notes);
            t.Conditions.Add(new Condition(ConditionType.VarEquals, guard, 0));
            list.Add(new GameAction(ActionType.RunTrigger, t.Id));
        }
    }

    // ================================================================= statements

    private void Call(QCall call, List<GameAction> list, Scope scope, List<string> notes)
    {
        var args = call.Args;
        string? Obj(int i) => i < args.Count ? ObjectRef(args[i], scope) : null;
        switch (call.Name.ToLowerInvariant())
        {
            case "msg":
            case "print":
                if (args.Count > 0) list.Add(GameAction.Say(TextOf(args[0], scope, notes)));
                break;
            case "moveobject":
                if (Obj(0) == "@player" && args.Count > 1 && RoomRef(args[1], scope) is { } room) list.Add(new GameAction(ActionType.GoTo, room));
                else if (Obj(0) is { } o && args.Count > 1 && LocationRef(args[1], scope) is { } loc) list.Add(new GameAction(ActionType.MoveItem, o, b: loc));
                else NotConverted(call, notes, "MoveObject with unknown objects");
                break;
            case "moveobjecthere": if (Obj(0) is { } oh) list.Add(new GameAction(ActionType.MoveItem, oh, b: Locations.Here)); break;
            case "addtoinventory": if (Obj(0) is { } oi) list.Add(new GameAction(ActionType.MoveItem, oi, b: Locations.Carried)); break;
            case "removeobject": if (Obj(0) is { } or) list.Add(new GameAction(ActionType.MoveItem, or, b: "")); break;
            case "setobjectflagon":
            case "setobjectflagoff":
                if (Obj(0) is { } of && args.Count > 1 && args[1] is QString flag)
                    list.Add(new GameAction(ActionType.SetVar, Var($"{of}_{flag.Value}"), call.Name.EndsWith("on", StringComparison.OrdinalIgnoreCase) ? 1 : 0));
                break;
            case "increasescore": list.Add(new GameAction(ActionType.AwardScore, n: Number(args, 0, 1))); break;
            case "decreasescore": list.Add(new GameAction(ActionType.AddVar, "@score", -Number(args, 0, 1))); break;
            case "finish":
            {
                // Quest's "finish" just ends the game: a win unless the last message says otherwise.
                var last = list.LastOrDefault(x => x.Type == ActionType.Message)?.Text ?? "";
                bool lost = Regex.IsMatch(last, @"\b(lost|lose|die|died|dead|killed|game over)\b", RegexOptions.IgnoreCase);
                list.Add(new GameAction(lost ? ActionType.Lose : ActionType.Win, text: ""));
                break;
            }
            case "picture":
            case "setframepicture":
                if (args.Count > 0 && args[0] is QString pf && PictureFor(pf.Value) is { } pic) list.Add(new GameAction(ActionType.ShowPicture, pic));
                else NotConverted(call, notes, "Pictures that aren't in the game");
                break;
            case "play":
            case "playsound":
                if (args.Count > 0 && args[0] is QString sf && SoundFor(sf.Value) is { } snd) list.Add(new GameAction(ActionType.PlaySound, snd));
                break;
            case "stopsound": list.Add(new GameAction(ActionType.StopSound)); break;
            case "unlockexit":
            case "lockexit":
                if (args.Count > 0 && args[0] is QName en && exitLocks.TryGetValue(en.Name, out var lockVar))
                    list.Add(new GameAction(ActionType.SetVar, lockVar, call.Name.StartsWith("unlock", StringComparison.OrdinalIgnoreCase) ? 0 : 1));
                else NotConverted(call, notes, "Unlocking exits without a name");
                break;
            case "helperopenobject": if (Obj(0) is { } oo) list.Add(new GameAction(ActionType.SetOpen, oo, 1)); break;
            case "helpercloseobject": if (Obj(0) is { } oc) list.Add(new GameAction(ActionType.SetOpen, oc, 0)); break;
            case "switchon": if (Obj(0) is { } so) list.Add(new GameAction(ActionType.SetLit, so, 1)); break;
            case "switchoff": if (Obj(0) is { } sx) list.Add(new GameAction(ActionType.SetLit, sx, 0)); break;
            case "enableturnscript":
            case "disableturnscript":
            case "enabletimer":
            case "disabletimer":
                if (args.Count > 0 && args[0] is QName tn && a.Triggers.FirstOrDefault(x => x.Name.EndsWith(" " + tn.Name) || x.Id.EndsWith(Slug(tn.Name))) is { } target)
                    list.Add(new GameAction(call.Name.StartsWith("enable", StringComparison.OrdinalIgnoreCase) ? ActionType.EnableTrigger : ActionType.DisableTrigger, target.Id));
                else NotConverted(call, notes, "Turn scripts or timers enabled by name");
                break;
            case "clearscreen": list.Add(new GameAction(ActionType.ClearScreen)); break;
            case "suppressturnscripts":
            case "request":
            case "setbackgroundcolour":
            case "setforegroundcolour":
            case "firsttimeonly":
                break;   // presentation only
            case "return":
                break;
            default:
                NotConverted(call, notes, "Calls to functions without an equivalent");
                break;
        }
    }

    private static int Number(List<QExpr> args, int i, int fallback) => i < args.Count && args[i] is QNumber n ? n.Value : fallback;

    /// <summary>Local variables used to build up text: s = "A"; s = s + "B"; msg (s + "C") prints A, B and C in turn.</summary>
    private readonly HashSet<string> textLocals = new(StringComparer.OrdinalIgnoreCase);

    private void Assign(QAssign st, List<GameAction> list, Scope scope, List<string> notes)
    {
        if (st.Target is QName local && IsText(st.Value, local.Name))
        {
            textLocals.Add(local.Name);
            var piece = TextOf(st.Value, scope, notes);
            if (piece.Length > 0) list.Add(new GameAction(ActionType.Message, n: 1, text: piece));
            return;
        }
        if (st.Target is not QMember member) { NotConverted(st, notes, "Assignments to local variables"); return; }
        var owner = ObjectRef(member.Target, scope);
        var attr = member.Attribute.ToLowerInvariant();

        // game.score = game.score + n
        if (member.Target is QName { Name: "game" } && attr == "score")
        {
            if (st.Value is QBinary { Op: "+" or "-" } b && b.Left is QMember { Attribute: "score" } && b.Right is QNumber n)
                list.Add(b.Op == "+" ? new GameAction(ActionType.AwardScore, n: n.Value) : new GameAction(ActionType.AddVar, "@score", -n.Value));
            else if (st.Value is QNumber set) list.Add(new GameAction(ActionType.SetVar, "@score", set.Value));
            else NotConverted(st, notes, "Score calculations");
            return;
        }
        if (owner == null) { NotConverted(st, notes, "Attributes of unknown objects"); return; }

        switch (attr)
        {
            case "parent":
                if (owner == "@player" && RoomRef(st.Value, scope) is { } room) list.Add(new GameAction(ActionType.GoTo, room));
                else if (LocationRef(st.Value, scope) is { } loc) list.Add(new GameAction(ActionType.MoveItem, owner, b: loc));
                else NotConverted(st, notes, "Moving objects to computed places");
                return;
            case "visible":
                if (st.Value is QBool vis)
                {
                    var initial = a.FindItem(owner)?.Location;
                    list.Add(vis.Value ? new GameAction(ActionType.MoveItem, owner, b: InitialLocationOf(owner) ?? Locations.Here) : new GameAction(ActionType.MoveItem, owner, b: ""));
                }
                else NotConverted(st, notes, "Visibility set by a calculation");
                return;
            case "isopen": if (st.Value is QBool open) list.Add(new GameAction(ActionType.SetOpen, owner, open.Value ? 1 : 0)); return;
            case "switchedon": if (st.Value is QBool on) list.Add(new GameAction(ActionType.SetLit, owner, on.Value ? 1 : 0)); return;
            case "worn":
                if (st.Value is QBool worn)
                {
                    // The game wears it from a script, so it is wearable.
                    if (a.FindItem(owner) is { } wearable) wearable.Wearable = true;
                    list.Add(new GameAction(worn.Value ? ActionType.WearItem : ActionType.UnwearItem, owner));
                }
                return;
        }
        var v = Var($"{owner.TrimStart('@')}_{attr}");
        switch (st.Value)
        {
            case QBool bo: list.Add(new GameAction(ActionType.SetVar, v, bo.Value ? 1 : 0)); break;
            case QNumber num: list.Add(new GameAction(ActionType.SetVar, v, num.Value)); break;
            case QUnary { Op: "-", Operand: QNumber neg }: list.Add(new GameAction(ActionType.SetVar, v, -neg.Value)); break;
            case QBinary { Op: "+" or "-" } bin when bin.Left is QMember lm && lm.Attribute.Equals(member.Attribute, StringComparison.OrdinalIgnoreCase) && bin.Right is QNumber d:
                list.Add(new GameAction(ActionType.AddVar, v, bin.Op == "+" ? d.Value : -d.Value));
                break;
            case QMember other when ObjectRef(other.Target, scope) is { } src:
                list.Add(new GameAction(ActionType.CopyVar, v, b: Var($"{src.TrimStart('@')}_{other.Attribute.ToLowerInvariant()}")));
                break;
            default:
                NotConverted(st, notes, "Attributes set to text or calculations");
                break;
        }
    }

    /// <summary>True for text: a string, or text added to the same local variable.</summary>
    private static bool IsText(QExpr e, string local) => e switch
    {
        QString => true,
        QBinary { Op: "+" } b => (b.Left is QName n && string.Equals(n.Name, local, StringComparison.OrdinalIgnoreCase) || IsText(b.Left, local)) && (b.Right is QString || IsText(b.Right, local) || b.Right is QMember or QFunc),
        _ => false,
    };

    private string? InitialLocationOf(string itemId) => a.FindItem(itemId) is { } it && it.Location.Length > 0 ? it.Location : null;

    // ================================================================= references

    /// <summary>An item id, "@player", or a room id, for a Quest object expression.</summary>
    private string? ObjectRef(QExpr e, Scope scope) => e switch
    {
        QName { Name: "this" } => scope.This,
        QName { Name: "object" or "object1" } when scope.Command => "$noun1",
        QName { Name: "object2" } when scope.Command => "$noun2",
        QName n when string.Equals(n.Name, playerName, StringComparison.OrdinalIgnoreCase) || n.Name == "game.pov" => "@player",
        QMember { Target: QName { Name: "game" }, Attribute: "pov" } => "@player",
        QName n when itemIds.TryGetValue(n.Name, out var id) => id,
        QName n when roomIds.TryGetValue(n.Name, out var rid) => rid,
        QFunc { Name: "GetObject" } f when f.Args.Count > 0 && f.Args[0] is QString s => ObjectRef(new QName(s.Value), scope),
        _ => null,
    };

    private string? RoomRef(QExpr e, Scope scope) => ObjectRef(e, scope) is { } id && a.Rooms.Any(r => r.Id == id) || roomIds.ContainsValue(ObjectRef(e, scope) ?? "") ? ObjectRef(e, scope) : null;

    /// <summary>Where an object is being put: a room, an item (container), the player, or the player's room.</summary>
    private string? LocationRef(QExpr e, Scope scope)
    {
        if (e is QMember { Attribute: "parent" } pm && ObjectRef(pm.Target, scope) == "@player") return Locations.Here;
        var id = ObjectRef(e, scope);
        return id == "@player" ? Locations.Carried : id;
    }

    // ================================================================= conditions

    /// <summary>A Quest condition as alternatives of AND-ed conditions (disjunctive normal form), or null if it can't be converted.</summary>
    private List<List<Condition>>? Conditions(QExpr e, Scope scope, int depth = 0)
    {
        if (depth > 8) return null;
        switch (e)
        {
            case QBool b: return b.Value ? new() { new() } : new() { new() { new Condition(ConditionType.Always, negate: true) } };
            case QBinary { Op: "and" } and:
            {
                var l = Conditions(and.Left, scope, depth + 1);
                var r = Conditions(and.Right, scope, depth + 1);
                if (l == null || r == null) return null;
                var result = new List<List<Condition>>();
                foreach (var x in l) foreach (var y in r) result.Add(x.Concat(y).ToList());
                return result.Count > 16 ? null : result;
            }
            case QBinary { Op: "or" } or:
            {
                var l = Conditions(or.Left, scope, depth + 1);
                var r = Conditions(or.Right, scope, depth + 1);
                return l == null || r == null ? null : l.Concat(r).ToList();
            }
            case QUnary { Op: "not" } not:
                return Negate(not.Operand, scope, depth);
            case QFunc f when functions.TryGetValue(f.Name, out var fn) && fn.Params.Count == 0:
            {
                // Boolean functions of the form "return (condition)" are used inline.
                var body = QuestScript.Parse(fn.Body.Value);
                if (body.Count == 1 && body[0] is QCall { Name: "return", Args.Count: 1 } ret) return Conditions(ret.Args[0], scope, depth + 1);
                return null;
            }
            default:
                return Atom(e, scope) is { } c ? new() { new() { c } } : null;
        }
    }

    private List<List<Condition>>? Negate(QExpr e, Scope scope, int depth)
    {
        switch (e)
        {
            case QBinary { Op: "and" } and: return Conditions(new QBinary("or", new QUnary("not", and.Left), new QUnary("not", and.Right)), scope, depth + 1);
            case QBinary { Op: "or" } or: return Conditions(new QBinary("and", new QUnary("not", or.Left), new QUnary("not", or.Right)), scope, depth + 1);
            case QUnary { Op: "not" } not: return Conditions(not.Operand, scope, depth + 1);
            case QFunc f when functions.TryGetValue(f.Name, out var fn) && fn.Params.Count == 0:
            {
                var body = QuestScript.Parse(fn.Body.Value);
                if (body.Count == 1 && body[0] is QCall { Name: "return", Args.Count: 1 } ret) return Negate(ret.Args[0], scope, depth + 1);
                return null;
            }
            default:
                if (Atom(e, scope) is not { } c) return null;
                c.Negate = !c.Negate;
                return new() { new() { c } };
        }
    }

    /// <summary>One simple test.</summary>
    private Condition? Atom(QExpr e, Scope scope)
    {
        switch (e)
        {
            case QFunc { Name: "Got" } g when g.Args.Count == 1 && ObjectRef(g.Args[0], scope) is { } got:
                return new Condition(ConditionType.ItemCarried, got);
            case QFunc { Name: "ListContains" } lc when lc.Args.Count == 2 && lc.Args[0] is QFunc { Name: "ScopeVisible" or "ScopeReachable" or "ScopeVisibleNotHeld" } && ObjectRef(lc.Args[1], scope) is { } seen:
                return new Condition(ConditionType.ItemPresent, seen);
            case QFunc { Name: "IsSwitchedOn" } sw when sw.Args.Count == 1 && ObjectRef(sw.Args[0], scope) is { } lit:
                return new Condition(ConditionType.ItemLit, lit);
            case QFunc { Name: "RandomChance" } rc when rc.Args.Count == 1 && rc.Args[0] is QNumber pct:
                return new Condition(ConditionType.Chance, n: pct.Value);
            case QFunc { Name: "GetBoolean" } gb when gb.Args.Count == 2 && gb.Args[1] is QString attr && ObjectRef(gb.Args[0], scope) is { } bo:
                return BooleanAttribute(bo, attr.Value);
            case QMember m when ObjectRef(m.Target, scope) is { } owner:
                return BooleanAttribute(owner, m.Attribute);
            case QBinary { Op: "=" or "<>" } eq:
            {
                bool negate = eq.Op == "<>";
                // x.parent = place
                if (eq.Left is QMember { Attribute: "parent" } pm && ObjectRef(pm.Target, scope) is { } who)
                {
                    if (who == "@player") return ObjectRef(eq.Right, scope) is { } room ? new Condition(ConditionType.PlayerIn, room, negate: negate) : null;
                    if (eq.Right is QMember { Attribute: "parent" } rp && ObjectRef(rp.Target, scope) == "@player")
                        return new Condition(ConditionType.ItemIn, who, b: Locations.Here, negate: negate);
                    return LocationRef(eq.Right, scope) is { } where ? new Condition(ConditionType.ItemIn, who, b: where, negate: negate) : null;
                }
                // obj.parent.parent = player.parent: in something in the room – approximated as "present"
                if (eq.Left is QMember { Attribute: "parent", Target: QMember { Attribute: "parent" } inner } && ObjectRef(inner.Target, scope) is { } nested
                    && eq.Right is QMember { Attribute: "parent" } rp2 && ObjectRef(rp2.Target, scope) == "@player")
                    return new Condition(ConditionType.ItemPresent, nested, negate: negate);
                if (Numeric(eq.Left, scope) is { } v && eq.Right is QNumber n) return new Condition(ConditionType.VarEquals, v, n.Value, negate: negate);
                if (Numeric(eq.Left, scope) is { } vb && eq.Right is QBool bv) return new Condition(ConditionType.VarEquals, vb, bv.Value ? 1 : 0, negate: negate);
                if (Numeric(eq.Left, scope) is { } v1 && Numeric(eq.Right, scope) is { } v2) return new Condition(ConditionType.VarEqualsVar, v1, b: v2, negate: negate);
                if (ObjectRef(eq.Left, scope) is { } x && ObjectRef(eq.Right, scope) is { } y) return x == y ? null : new Condition(ConditionType.Always, negate: !negate);
                return null;
            }
            case QBinary { Op: ">" or "<" or ">=" or "<=" } cmp when Numeric(cmp.Left, scope) is { } v && cmp.Right is QNumber n:
                return cmp.Op switch
                {
                    ">" => new Condition(ConditionType.VarGreater, v, n.Value),
                    "<" => new Condition(ConditionType.VarLess, v, n.Value),
                    ">=" => new Condition(ConditionType.VarGreater, v, n.Value - 1),
                    _ => new Condition(ConditionType.VarLess, v, n.Value + 1),
                };
            case QBinary { Op: ">" or "<" } cmp2 when Numeric(cmp2.Left, scope) is { } l && Numeric(cmp2.Right, scope) is { } r:
                return new Condition(cmp2.Op == ">" ? ConditionType.VarGreaterVar : ConditionType.VarLessVar, l, b: r);
        }
        return null;
    }

    private Condition BooleanAttribute(string owner, string attribute) => attribute.ToLowerInvariant() switch
    {
        "isopen" => new Condition(ConditionType.ItemOpen, owner),
        "switchedon" => new Condition(ConditionType.ItemLit, owner),
        "worn" => new Condition(ConditionType.ItemWorn, owner),
        "visible" => new Condition(ConditionType.ItemExists, owner),
        "locked" => new Condition(ConditionType.ItemLocked, owner),
        "visited" => new Condition(ConditionType.RoomVisited, owner),
        var attr => new Condition(ConditionType.VarEquals, Var($"{owner.TrimStart('@')}_{attr}"), 0, negate: true),
    };

    /// <summary>A variable for a numeric expression: obj.attr or game.score.</summary>
    private string? Numeric(QExpr e, Scope scope)
    {
        if (e is QMember { Target: QName { Name: "game" }, Attribute: "score" }) return "@score";
        if (e is QMember m && ObjectRef(m.Target, scope) is { } owner) return Var($"{owner.TrimStart('@')}_{m.Attribute.ToLowerInvariant()}");
        return null;
    }

    // ================================================================= text

    private string TextOf(QExpr e, Scope scope, List<string> notes) => e switch
    {
        QString s => TextProcessor(s.Value),
        QNumber n => n.Value.ToString(),
        QBinary { Op: "+" } b => TextOf(b.Left, scope, notes) + TextOf(b.Right, scope, notes),
        QMember { Attribute: "article" or "gender" } m when ObjectRef(m.Target, scope) == "$noun1" => "{the noun1}",
        QFunc { Name: "GetDisplayAlias" or "GetDisplayName" } f when f.Args.Count == 1 && ObjectRef(f.Args[0], scope) is "$noun1" => "{noun1}",
        QFunc { Name: "GetDisplayAlias" or "GetDisplayName" } f when f.Args.Count == 1 && ObjectRef(f.Args[0], scope) is "$noun2" => "{noun2}",
        QFunc { Name: "GetDisplayAlias" or "GetDisplayName" } f when f.Args.Count == 1 && ObjectRef(f.Args[0], scope) is { } id && a.FindItem(id) is { } it => it.Name,
        QMember { Attribute: "alias" } m when ObjectRef(m.Target, scope) is { } id2 && a.FindItem(id2) is { } it2 => it2.Name,
        QName { Name: "command" } => "{input}",
        QName local when textLocals.Contains(local.Name) => "",   // already printed as it was built
        QName { Name: "text" } when scope.Command => "{text}",
        _ when Numeric(e, scope) is { } v => "{var:" + v + "}",
        _ => Unknown(e, notes),
    };

    private string Unknown(QExpr e, List<string> notes)
    {
        notes.Add("text: " + Describe(e));
        Skip("Text built from values without an equivalent");
        return "";
    }

    private static string Describe(QExpr e) => e switch
    {
        QString s => $"\"{s.Value}\"",
        QNumber n => n.Value.ToString(),
        QBool b => b.Value ? "true" : "false",
        QName n => n.Name,
        QMember m => $"{Describe(m.Target)}.{m.Attribute}",
        QFunc f => $"{f.Name}({string.Join(", ", f.Args.Select(Describe))})",
        QUnary u => $"{u.Op} {Describe(u.Operand)}",
        QBinary b => $"{Describe(b.Left)} {b.Op} {Describe(b.Right)}",
        _ => "?",
    };

    // ================================================================= gamebooks

    /// <summary>Quest gamebooks: each page is a room whose options become numbered choices.</summary>
    private void GamebookPages()
    {
        var pages = Root.Elements("object").Where(o => o.Attribute("name") != null && !Inherits(o, "editor_player") && o.Attribute("name")!.Value != playerName).ToList();
        foreach (var p in pages) roomIds[p.Attribute("name")!.Value] = Unique(Slug(p.Attribute("name")!.Value));
        foreach (var p in pages)
        {
            var name = p.Attribute("name")!.Value;
            var room = new Room { Id = roomIds[name], Name = Text(p, "alias") ?? name, Description = TextProcessor(Text(p, "description") ?? "") };
            if (Text(p, "picture") is { } pic && PictureFor(pic) is { } pid) room.PictureId = pid;
            int n = 0;
            foreach (var item in p.Element("options")?.Elements("item") ?? Enumerable.Empty<XElement>())
            {
                var target = item.Element("key")?.Value;
                var text = item.Element("value")?.Value ?? target;
                if (target == null || !roomIds.TryGetValue(target, out var tid)) continue;
                Choices.Add(a, room, ++n, tid, text ?? "");
            }
            if (p.Element("script") is { } s && IsScript(s))
                a.Triggers.Add(ScriptTrigger($"{room.Id}_script", $"{room.Name}: page script", TriggerEvent.EnterRoom, s, p, roomId: room.Id));
            a.Rooms.Add(room);
        }
        if (pages.Count > 0) a.StartRoomId = roomIds[pages[0].Attribute("name")!.Value];
        Choices.Configure(a);
    }
}
