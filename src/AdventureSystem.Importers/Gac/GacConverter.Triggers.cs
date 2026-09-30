using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Gac;

internal sealed partial class GacConverter
{
    private const int MaxDisjuncts = 64;

    private enum TermKind { Const, Var, Room, Rand, Noun1, Noun2, VerbNo }

    private sealed record Term(TermKind Kind, int N = 0, string? Var = null, int Offset = 0);

    private sealed class Pattern
    {
        public int? Verb;
        /// <summary>Noun slot requirements (0 = the slot must be empty).</summary>
        public int? Noun1, Noun2;
        /// <summary>Set when NO1/NO2 are known exactly (NO1 = n), so actions using NO1/NO2 can be bound.</summary>
        public int? Noun1Exact, Noun2Exact;
        public int? Adverb;
        public readonly List<Condition> Conditions = new();
    }

    // ---- trigger emission -------------------------------------------------------------------------------------

    private void EmitLine(List<GacSegment> segments, string idBase, int priority, string? roomId)
    {
        for (int s = 0; s < segments.Count; s++)
        {
            var seg = segments[s];
            var id = segments.Count > 1 ? $"{idBase}.{s + 1}" : idBase;
            var where = table switch
            {
                Table.High => "high priority",
                Table.Local => $"local (room {localRoom})",
                _ => "low priority",
            };

            List<List<GacLit>> dnf;
            try
            {
                dnf = new List<List<GacLit>> { new() };
                foreach (var c in seg.Conditions) dnf = Cross(dnf, Dnf(c, false));
            }
            catch (GacConvertException e)
            {
                Note($"Skipped {where} condition \"{seg.LineText}\": {e.Message}.");
                continue;
            }

            var probe = ConvertActions(seg.Statements, new List<string>());
            if (probe.Count == 0 && seg.Statements.Count == 0) continue;
            if (dnf.Count == 0)
            {
                Note($"Skipped {where} condition \"{seg.LineText}\": it can never be true.");
                continue;
            }

            bool endsTurn = probe.Any(a => a.Type is ActionType.Done or ActionType.Ok or ActionType.Quit or ActionType.Win or ActionType.Lose);
            if (dnf.Count > 1 && !endsTurn) MakeExclusive(dnf, seg.LineText, where);

            int part = 0;
            int emitted = 0;
            foreach (var conj in dnf)
            {
                part++;
                var p = Build(conj);
                if (p == null) continue;
                foreach (var c in p.Conditions)
                {
                    c.A = BindRef(c.A, p);
                    c.B = BindRef(c.B, p);
                }
                int? verbBound = conj.Where(l => l.Kind == LitKind.Verb && !l.Neg).Select(l => (int?)l.Number).FirstOrDefault();
                var bound = seg.Statements.Select(x => Bind(x, p.Noun1Exact, p.Noun2Exact, verbBound)).ToList();
                var actionNotes = new List<string>();
                var actions = ConvertActions(bound, actionNotes);
                foreach (var n in actionNotes) Note($"In {where} condition \"{seg.LineText}\": {n}.");
                if (actions.Count == 0) continue;
                var tid = dnf.Count > 1 ? $"{id}{(char)('a' + Math.Min(part - 1, 25))}{(part > 26 ? part.ToString() : "")}" : id;
                foreach (var t in MakeTriggers(p, actions, tid, priority, roomId, seg.LineText))
                {
                    adv.Triggers.Add(t);
                    emitted++;
                }
            }
            if (emitted == 0 && table != Table.High)
                Note($"Dropped {where} condition \"{seg.LineText}\": unreachable (its verb is an exit of the room, and GAC moves the player first).");
        }
    }

    private IEnumerable<Trigger> MakeTriggers(Pattern p, List<GameAction> actions, string id, int priority, string? roomId, string lineText)
    {
        var conditions = p.Conditions.Select(c => new Condition(c.Type, c.A, c.N, c.B, c.Negate)).ToList();
        var name = lineText.Length > 70 ? lineText[..70] + "…" : lineText;

        if (table == Table.High)
        {
            bool testsCommand = p.Verb != null || p.Noun1 != null || p.Noun2 != null || p.Adverb != null
                || conditions.Any(c => c.Type is ConditionType.Noun1Is or ConditionType.Noun2Is or ConditionType.WordUsed or ConditionType.AdverbUsed);
            var every = NewTrigger(id, name, TriggerEvent.EveryTurn, priority, null, p, conditions, actions, lineText);
            if (testsCommand)
                every.Notes += "\nGAC runs the high priority table before input, with the words of the previous command: this trigger tests them.";
            yield return every;
            if (!testsCommand)
                yield return NewTrigger(id + "s", name, TriggerEvent.GameStart, priority, null, p, conditions, actions,
                    lineText + "\n(copy: GAC also runs the high priority table before the first input)");
            yield break;
        }

        // GAC tries the room's connections before the local and low priority tables.
        if (p.Verb is int v && exitRooms.TryGetValue(v, out var rooms))
        {
            if (table == Table.Local)
            {
                if (rooms.Contains(localRoom)) yield break;
            }
            else
            {
                foreach (var r in rooms) conditions.Add(new Condition(ConditionType.PlayerIn, $"r{r}", negate: true));
            }
        }
        yield return NewTrigger(id, name, TriggerEvent.BeforeCommand, priority, roomId, p, conditions, actions, lineText);
    }

    private Trigger NewTrigger(string id, string name, TriggerEvent ev, int priority, string? roomId, Pattern p,
        List<Condition> conditions, List<GameAction> actions, string notes)
    {
        bool command = ev == TriggerEvent.BeforeCommand;
        return new Trigger
        {
            Id = id,
            Name = name,
            Event = ev,
            Priority = priority,
            RoomId = roomId,
            Verb = p.Verb is int v ? VerbId(v) : command ? "*" : null,
            Noun1 = p.Noun1 is int n1 ? SlotWord(n1) : command ? "*" : null,
            Noun2 = p.Noun2 is int n2 ? SlotWord(n2) : null,
            Adverb = p.Adverb is int a ? AdverbWord(a) : null,
            Conditions = conditions.Select(c => new Condition(c.Type, c.A, c.N, c.B, c.Negate)).ToList(),
            Actions = actions.Select(x => new GameAction(x.Type, x.A, x.N, x.B, x.Text)).ToList(),
            StopsCommand = false,
            Notes = notes,
        };
    }

    /// <summary>
    /// When the actions don't end the turn, two true disjuncts would run them twice (GAC runs them once).
    /// Adds "not the earlier disjunct" to later ones when that is a single literal.
    /// </summary>
    private void MakeExclusive(List<List<GacLit>> dnf, string lineText, string where)
    {
        bool warned = false;
        for (int i = 1; i < dnf.Count; i++)
            for (int j = 0; j < i; j++)
            {
                if (Exclusive(dnf[i], dnf[j])) continue;
                if (dnf[j].Count == 1) dnf[i].Add(dnf[j][0].Negated());
                else if (!warned)
                {
                    warned = true;
                    Note($"The {where} condition \"{lineText}\" was split into several triggers that may both fire in the same turn.");
                }
            }
    }

    private static bool Exclusive(List<GacLit> x, List<GacLit> y)
    {
        foreach (var a in x)
            foreach (var b in y)
            {
                if (a.SameAtom(b) && a.Neg != b.Neg) return true;
                if (a.Neg || b.Neg) continue;
                if (a.Kind == b.Kind && a.Kind is LitKind.Verb or LitKind.Adverb or LitKind.Noun1Exact or LitKind.Noun2Exact && a.Number != b.Number) return true;
                if (a.Kind == LitKind.Cond && b.Kind == LitKind.Cond && a.Cond!.Type == b.Cond!.Type)
                {
                    if (a.Cond.Type == ConditionType.PlayerIn && a.Cond.A != b.Cond.A) return true;
                    if (a.Cond.Type == ConditionType.VarEquals && a.Cond.A == b.Cond.A && a.Cond.N != b.Cond.N) return true;
                }
            }
        return false;
    }

    /// <summary>Turns a conjunction of literals into a command pattern plus conditions; null if contradictory.</summary>
    private Pattern? Build(List<GacLit> conj)
    {
        var p = new Pattern();
        var posVerbs = conj.Where(l => l.Kind == LitKind.Verb && !l.Neg).Select(l => l.Number).Distinct().ToList();
        var negVerbs = conj.Where(l => l.Kind == LitKind.Verb && l.Neg).Select(l => l.Number).Distinct().ToList();
        if (posVerbs.Count > 1) return null;
        if (posVerbs.Count == 1)
        {
            if (negVerbs.Contains(posVerbs[0])) return null;
            p.Verb = posVerbs[0];
        }
        else
        {
            foreach (var v in negVerbs)
            {
                var words = verbGroups.TryGetValue(v, out var g) ? g.Words : new List<string> { $"verb{v}" };
                foreach (var w in words) p.Conditions.Add(new Condition(ConditionType.WordUsed, w, negate: true));
            }
        }

        // Exact slots (NO1 = n, NO2 = n) first, then NOUN n (either slot) fills a free slot.
        foreach (var (kind, slot) in new[] { (LitKind.Noun1Exact, 1), (LitKind.Noun2Exact, 2) })
        {
            var pos = conj.Where(l => l.Kind == kind && !l.Neg).Select(l => l.Number).Distinct().ToList();
            var neg = conj.Where(l => l.Kind == kind && l.Neg).Select(l => l.Number).Distinct().ToList();
            if (pos.Count > 1 || pos.Any(neg.Contains)) return null;
            if (pos.Count == 1)
            {
                if (slot == 1) p.Noun1 = p.Noun1Exact = pos[0];
                else p.Noun2 = p.Noun2Exact = pos[0];
            }
            else
                foreach (var n in neg)
                    p.Conditions.Add(new Condition(slot == 1 ? ConditionType.Noun1Is : ConditionType.Noun2Is, SlotWord(n), negate: true));
        }
        var posNouns = conj.Where(l => l.Kind == LitKind.Noun && !l.Neg).Select(l => l.Number).Distinct().ToList();
        var negNouns = conj.Where(l => l.Kind == LitKind.Noun && l.Neg).Select(l => l.Number).Distinct().ToList();
        if (posNouns.Any(negNouns.Contains)) return null;
        if (negNouns.Contains(p.Noun1 ?? -1) || negNouns.Contains(p.Noun2 ?? -1)) return null;
        foreach (var n in posNouns)
        {
            if (p.Noun1 == n || p.Noun2 == n) continue;
            if (p.Noun1 == null) p.Noun1 = n;
            else if (p.Noun2 == null) p.Noun2 = n;
            else return null;
        }
        foreach (var n in negNouns)
        {
            p.Conditions.Add(new Condition(ConditionType.Noun1Is, NounWord(n), negate: true));
            p.Conditions.Add(new Condition(ConditionType.Noun2Is, NounWord(n), negate: true));
        }

        var posAdv = conj.Where(l => l.Kind == LitKind.Adverb && !l.Neg).Select(l => l.Number).Distinct().ToList();
        var negAdv = conj.Where(l => l.Kind == LitKind.Adverb && l.Neg).Select(l => l.Number).Distinct().ToList();
        if (posAdv.Count > 1) return null;
        if (posAdv.Count == 1)
        {
            if (negAdv.Contains(posAdv[0])) return null;
            p.Adverb = posAdv[0];
        }
        else
            foreach (var a in negAdv) p.Conditions.Add(new Condition(ConditionType.AdverbUsed, AdverbWord(a), negate: true));

        var seen = new HashSet<string>();
        foreach (var l in conj.Where(l => l.Kind == LitKind.Cond))
        {
            if (!seen.Add(l.Key + (l.Neg ? "!" : ""))) continue;
            if (seen.Contains(l.Key + (l.Neg ? "" : "!"))) return null;
            var c = l.Cond!;
            p.Conditions.Add(new Condition(c.Type, c.A, c.N, c.B, l.Neg));
        }
        return p;
    }

    // ---- boolean expressions → DNF ----------------------------------------------------------------------------

    private static List<List<GacLit>> Truth(bool t) => t ? new List<List<GacLit>> { new() } : new List<List<GacLit>>();

    private static List<List<GacLit>> Cross(List<List<GacLit>> a, List<List<GacLit>> b)
    {
        var result = new List<List<GacLit>>();
        foreach (var x in a)
            foreach (var y in b)
            {
                var c = new List<GacLit>(x);
                bool contradiction = false;
                foreach (var l in y)
                {
                    if (c.Any(k => k.SameAtom(l) && k.Neg != l.Neg)) { contradiction = true; break; }
                    if (!c.Any(k => k.SameAtom(l))) c.Add(l);
                }
                if (contradiction) continue;
                result.Add(c);
                if (result.Count > MaxDisjuncts) throw new GacConvertException("the condition is too complex to split into triggers");
            }
        return result;
    }

    private static List<List<GacLit>> Union(List<List<GacLit>> a, List<List<GacLit>> b)
    {
        var r = a.Concat(b).ToList();
        if (r.Count > MaxDisjuncts) throw new GacConvertException("the condition is too complex to split into triggers");
        return r;
    }

    private List<List<GacLit>> Dnf(GacNode n, bool neg)
    {
        if (n.IsConst) return Truth((n.Value != 0) ^ neg);
        switch (n.Op)
        {
            case GacOps.And:
                return neg ? Union(Dnf(n.Args[0], true), Dnf(n.Args[1], true)) : Cross(Dnf(n.Args[0], false), Dnf(n.Args[1], false));
            case GacOps.Or:
                return neg ? Cross(Dnf(n.Args[0], true), Dnf(n.Args[1], true)) : Union(Dnf(n.Args[0], false), Dnf(n.Args[1], false));
            case GacOps.Not:
                return Dnf(n.Args[0], !neg);
            case GacOps.Xor:
                return neg
                    ? Union(Cross(Dnf(n.Args[0], false), Dnf(n.Args[1], false)), Cross(Dnf(n.Args[0], true), Dnf(n.Args[1], true)))
                    : Union(Cross(Dnf(n.Args[0], false), Dnf(n.Args[1], true)), Cross(Dnf(n.Args[0], true), Dnf(n.Args[1], false)));
        }

        object atom = Atom(n);
        if (atom is bool b) return Truth(b ^ neg);
        if (atom is List<GacLit> any)
        {
            // A disjunction of literals (e.g. NO1 < 5 → NO1 = 1 OR NO1 = 2 ...).
            if (neg) return new List<List<GacLit>> { any.Select(l => l.Negated()).ToList() };
            return any.Select(l => new List<GacLit> { l }).ToList();
        }
        var lit = (GacLit)atom;
        return new List<List<GacLit>> { new() { neg ? lit.Negated() : lit } };
    }

    private static int ConstArg(GacNode n, string what)
    {
        if (n.Args.Length > 0 && n.Args[0].IsConst) return n.Args[0].Value;
        throw new GacConvertException($"{what} with a computed operand ({n.Text}) cannot be converted");
    }

    private static GacLit CondLit(ConditionType type, string? a, int n = 0, string? b = null) =>
        new() { Kind = LitKind.Cond, Cond = new Condition(type, a, n, b) };

    /// <summary>Returns a <see cref="GacLit"/> (not negated) or a bool when the value is known statically.</summary>
    private object Atom(GacNode n)
    {
        switch (n.Op)
        {
            case GacOps.Verb: return new GacLit { Kind = LitKind.Verb, Number = ConstArg(n, "VERB") };
            case GacOps.Noun: return new GacLit { Kind = LitKind.Noun, Number = ConstArg(n, "NOUN") };
            case GacOps.Adve: return new GacLit { Kind = LitKind.Adverb, Number = ConstArg(n, "ADVE") };
            case GacOps.At: return CondLit(ConditionType.PlayerIn, $"r{ConstArg(n, "AT")}");
            case GacOps.Here: return CondLit(ConditionType.ItemIn, ObjRef(n.Args[0]), b: Locations.Here);
            case GacOps.Carr: return CondLit(ConditionType.ItemCarried, ObjRef(n.Args[0]));
            case GacOps.Avai: return CondLit(ConditionType.ItemPresent, ObjRef(n.Args[0]));
            case GacOps.In: return CondLit(ConditionType.ItemIn, ObjRef(n.Args[0]), b: LocRef(n.Args[1]));
            case GacOps.SetQ: return CondLit(ConditionType.VarEquals, Marker(ConstArg(n, "SET?")), 1);
            case GacOps.ResQ: return CondLit(ConditionType.VarEquals, Marker(ConstArg(n, "RES?")), 0);
            case GacOps.EquQ:
            {
                if (!n.Args[1].IsConst) throw new GacConvertException("EQU? with a computed counter number cannot be converted");
                var counter = new Term(TermKind.Var, Var: CounterRead(n.Args[1].Value));
                return Compare(GacOps.Eq, Resolve(n.Args[0]), counter);
            }
            case GacOps.Lt:
            case GacOps.Gt:
            case GacOps.Eq:
                return Compare(n.Op, Resolve(n.Args[0]), Resolve(n.Args[1]));
            default:
            {
                // A number used as a truth value: true when non-zero.
                var t = Resolve(n);
                var r = Compare(GacOps.Eq, t, new Term(TermKind.Const, 0));
                return r is bool b ? !b : ((GacLit)r).Negated();
            }
        }
    }

    private Term Resolve(GacNode n)
    {
        if (n.IsConst) return new Term(TermKind.Const, n.Value);
        switch (n.Op)
        {
            case GacOps.Ctr: return new Term(TermKind.Var, Var: CounterRead(ConstArg(n, "CTR")));
            case GacOps.Turn: return new Term(TermKind.Var, Var: "@turns");
            case GacOps.Room: return new Term(TermKind.Room);
            case GacOps.Rand: return new Term(TermKind.Rand, ConstArg(n, "RAND"));
            case GacOps.No1: return new Term(TermKind.Noun1);
            case GacOps.No2: return new Term(TermKind.Noun2);
            case GacOps.Vbno: return new Term(TermKind.VerbNo);
            case GacOps.With: return new Term(TermKind.Const, CarriedRoom);
            case GacOps.Weig:
            {
                int o = ConstArg(n, "WEIG");
                return new Term(TermKind.Const, db.Objects.TryGetValue(o, out var obj) ? obj.Weight : 0);
            }
            case GacOps.Conn:
            {
                if (table != Table.Local) throw new GacConvertException("CONN outside a local condition cannot be converted");
                int v = ConstArg(n, "CONN");
                int dest = db.Rooms.TryGetValue(localRoom, out var room) ? room.Exits.FirstOrDefault(e => e.Verb == v).Destination : 0;
                return new Term(TermKind.Const, dest);
            }
            case GacOps.Plus:
            case GacOps.Minus:
            {
                var a = Resolve(n.Args[0]);
                var b = Resolve(n.Args[1]);
                int sign = n.Op == GacOps.Plus ? 1 : -1;
                if (b.Kind == TermKind.Const)
                    return a.Kind == TermKind.Const ? a with { N = a.N + sign * b.N } : a with { Offset = a.Offset + sign * b.N };
                if (a.Kind == TermKind.Const && sign == 1) return b with { Offset = b.Offset + a.N };
                throw new GacConvertException($"the arithmetic \"{n.Text}\" cannot be converted");
            }
        }
        throw new GacConvertException($"\"{n.Text}\" cannot be used as a number here");
    }

    private object Compare(int op, Term l, Term r)
    {
        if (l.Kind == TermKind.Const && r.Kind == TermKind.Const)
            return op == GacOps.Lt ? l.N < r.N : op == GacOps.Gt ? l.N > r.N : l.N == r.N;
        if (l.Kind == TermKind.Const)
        {
            (l, r) = (r, l);
            op = op == GacOps.Lt ? GacOps.Gt : op == GacOps.Gt ? GacOps.Lt : op;
        }
        if (r.Kind != TermKind.Const)
        {
            // Two computed values – counters, TURN or ROOM, each possibly with a number added:
            // l + lo OP r + ro  becomes  l OP r + (ro - lo).
            if (l.Kind is TermKind.Var or TermKind.Room && r.Kind is TermKind.Var or TermKind.Room)
            {
                if (l.Kind == TermKind.Room || r.Kind == TermKind.Room) usesRoomNumber = true;
                var type = op == GacOps.Eq ? ConditionType.VarEqualsVar : op == GacOps.Lt ? ConditionType.VarLessVar : ConditionType.VarGreaterVar;
                return CondLit(type, VariableOf(l), r.Offset - l.Offset, VariableOf(r));
            }
            throw new GacConvertException("comparing a random number, noun or verb number with another computed value cannot be converted");
        }
        int k = r.N - l.Offset;
        switch (l.Kind)
        {
            case TermKind.Var:
                return CondLit(op == GacOps.Eq ? ConditionType.VarEquals : op == GacOps.Lt ? ConditionType.VarLess : ConditionType.VarGreater, l.Var, k);
            case TermKind.Room:
                if (op == GacOps.Eq) return CondLit(ConditionType.PlayerIn, $"r{k}");
                usesRoomNumber = true;
                return CondLit(op == GacOps.Lt ? ConditionType.VarLess : ConditionType.VarGreater, RoomVar, k);
            case TermKind.Rand:
            {
                int max = Math.Max(1, l.N);
                int count = Enumerable.Range(0, max).Count(x => op == GacOps.Lt ? x < k : op == GacOps.Gt ? x > k : x == k);
                if (count == 0) return false;
                if (count == max) return true;
                return CondLit(ConditionType.Chance, null, Math.Clamp((int)Math.Round(100.0 * count / max), 1, 99));
            }
            case TermKind.Noun1:
            case TermKind.Noun2:
            {
                var kind = l.Kind == TermKind.Noun1 ? LitKind.Noun1Exact : LitKind.Noun2Exact;
                if (op == GacOps.Eq) return new GacLit { Kind = kind, Number = k };
                // A range of noun numbers: one literal per vocabulary noun in range (0, "no noun", is not included).
                var numbers = nounGroups.Keys.Where(x => x is > 0 and < 255 && (op == GacOps.Lt ? x < k : x > k)).OrderBy(x => x).ToList();
                if (numbers.Count == 0) return false;
                if (numbers.Count > MaxDisjuncts) throw new GacConvertException("the noun number range is too large to split into triggers");
                return numbers.Select(x => new GacLit { Kind = kind, Number = x }).ToList();
            }
            case TermKind.VerbNo:
                if (op != GacOps.Eq || l.Offset != 0) throw new GacConvertException("VBNO can only be compared for equality");
                return new GacLit { Kind = LitKind.Verb, Number = k };
        }
        throw new GacConvertException("unsupported comparison");
    }

    private static string VariableOf(Term t) => t.Kind == TermKind.Room ? RoomVar : t.Var!;

    // ---- operands ---------------------------------------------------------------------------------------------

    private static string? BindRef(string? r, Pattern p) => r switch
    {
        "$noun1" when p.Noun1Exact is int n and > 0 => $"o{n}",
        "$noun2" when p.Noun2Exact is int n and > 0 => $"o{n}",
        _ => r,
    };

    /// <summary>Pattern word for a noun slot requirement: "-" for "no noun".</summary>
    private string SlotWord(int n) => n == 0 ? "-" : NounWord(n);

    /// <summary>Replaces NO1/NO2/VBNO by the constants a disjunct fixes them to.</summary>
    private static GacNode Bind(GacNode n, int? no1, int? no2, int? verb)
    {
        if (n.IsConst) return n;
        if (n.Op == GacOps.No1 && no1 is int a) return GacNode.Const(a);
        if (n.Op == GacOps.No2 && no2 is int b) return GacNode.Const(b);
        if (n.Op == GacOps.Vbno && verb is int v) return GacNode.Const(v);
        if (n.Args.Length == 0) return n;
        return new GacNode { Op = n.Op, Value = n.Value, Text = n.Text, Args = n.Args.Select(x => Bind(x, no1, no2, verb)).ToArray() };
    }

    private string ObjRef(GacNode n)
    {
        if (n.IsConst)
        {
            if (!db.Objects.ContainsKey(n.Value)) Note($"Object {n.Value} is used by a condition but does not exist.");
            return $"o{n.Value}";
        }
        return n.Op switch
        {
            GacOps.No1 => "$noun1",
            GacOps.No2 => "$noun2",
            _ => throw new GacConvertException($"the object \"{n.Text}\" is computed"),
        };
    }

    private string LocRef(GacNode n)
    {
        if (n.IsConst) return LocationId(n.Value);
        return n.Op switch
        {
            GacOps.Room => Locations.Here,
            GacOps.With => Locations.Carried,
            _ => throw new GacConvertException($"the room \"{n.Text}\" is computed"),
        };
    }
}
