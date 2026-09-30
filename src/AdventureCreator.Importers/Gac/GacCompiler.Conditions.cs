using System.Text.RegularExpressions;
using AdventureCreator.Core.Model;

namespace AdventureCreator.Importers.Gac;

internal sealed partial class GacCompiler
{
    private enum Table { High, Local, Low }

    /// <summary>A condition line compiled from a trigger, and the table (and room, for local lines) it belongs in.</summary>
    private sealed record Compiled(Table Table, int Room, byte[] Code);

    /// <summary>A trigger's id belongs to line <paramref name="line"/> ("lp3" → lp3, lp3.2, lp3a, lp3s; not lp30).</summary>
    private static bool OfLine(string id, string line) =>
        id == line || id.StartsWith(line, StringComparison.Ordinal) && id[line.Length] is '.' or (>= 'a' and <= 'z');

    private static bool Helper(Trigger t) => t.Id.StartsWith("gac_darkness", StringComparison.Ordinal);

    private readonly List<Compiled> appended = new();

    private void Conditions()
    {
        var baseIds = baseline.Triggers.Select(t => t.Id).ToHashSet();
        var claimed = new HashSet<string>();

        List<byte[]> Rewrite(List<byte[]> lines, string prefix, Table table, int room)
        {
            var result = new List<byte[]>();
            int k = 0;
            foreach (var line in lines)
            {
                bool counted = GacDecompiler.Decompile(Codes(line)).Count > 0;
                if (!counted) { result.Add(line); continue; }
                var id = $"{prefix}{++k}";
                var before = baseline.Triggers.Where(t => OfLine(t.Id, id)).ToList();
                var now = a.Triggers.Where(t => OfLine(t.Id, id)).ToList();
                foreach (var t in now) claimed.Add(t.Id);
                if (before.Count == 0 || J(before.OrderBy(t => t.Id)) == J(now.OrderBy(t => t.Id)))
                {
                    result.Add(line);
                    continue;
                }
                Changed = true;
                // The line was edited (or deleted): each remaining trigger becomes a line of its own.
                foreach (var t in now.Where(t => !IsStartCopy(t, now)).OrderByDescending(t => t.Priority))
                    if (Compile(t) is { } c)
                    {
                        if (c.Table == table && c.Room == room) result.Add(c.Code);
                        else appended.Add(c);
                    }
            }
            return result;
        }

        var high = Rewrite(raw.High, "hp", GacCompiler.Table.High, 0);
        var local = raw.Local.Select(l => (l.Room, Lines: Rewrite(l.Lines, $"lc{l.Room}_", GacCompiler.Table.Local, l.Room))).ToList();
        var low = Rewrite(raw.Low, "lp", GacCompiler.Table.Low, 0);

        // New triggers.
        var news = a.Triggers.Where(t => !claimed.Contains(t.Id) && !baseIds.Contains(t.Id) && !Helper(t)).ToList();
        foreach (var t in news.Where(t => !IsStartCopy(t, news)).OrderByDescending(t => t.Priority))
            if (Compile(t) is { } c) appended.Add(c);
        InitialValues();

        foreach (var c in appended)
        {
            Changed = true;
            switch (c.Table)
            {
                case GacCompiler.Table.High: high.Add(c.Code); break;
                case GacCompiler.Table.Low: low.Add(c.Code); break;
                default:
                    int i = local.FindIndex(l => l.Room == c.Room);
                    if (i < 0) { local.Add((c.Room, new List<byte[]>())); i = local.Count - 1; }
                    local[i].Lines.Add(c.Code);
                    break;
            }
        }
        raw.High.Clear(); raw.High.AddRange(high);
        raw.Low.Clear(); raw.Low.AddRange(low);
        raw.Local.Clear(); raw.Local.AddRange(local.Where(l => l.Lines.Count > 0));
    }

    /// <summary>The importer's GameStart copy of a high priority line ("hp3s" next to "hp3").</summary>
    private static bool IsStartCopy(Trigger t, List<Trigger> all) =>
        t.Event == TriggerEvent.GameStart && t.Id.EndsWith('s') && all.Any(o => o.Id == t.Id[..^1] && o.Event == TriggerEvent.EveryTurn);

    private static List<GacCode> Codes(byte[] line)
    {
        var list = new List<GacCode>();
        for (int i = 0; i < line.Length; i++)
        {
            if ((line[i] & 0x80) != 0) { list.Add(new GacCode(true, ((line[i] & 0x7F) << 8) | line[i + 1])); i++; }
            else list.Add(new GacCode(false, line[i] & 0x3F));
        }
        return list;
    }

    // ================================================================= variables

    private enum VarKind { Counter, Marker, Turns, Room }

    private readonly Dictionary<string, int> newCounters = new();

    private (VarKind Kind, int N)? Var(string? name)
    {
        switch (name)
        {
            case "@score": return (VarKind.Counter, 0);
            case "@turns": return (VarKind.Turns, 0);
            case "@room": return (VarKind.Room, 0);
            case null: return null;
        }
        if (Numbered(name, 'm') is int m and < 256) return (VarKind.Marker, m);
        if (Numbered(name, 'c') is int c and < 126) return (VarKind.Counter, c);
        if (name.StartsWith('@')) return null;
        if (newCounters.TryGetValue(name, out var n)) return (VarKind.Counter, n);
        var used = a.Variables.Select(v => Numbered(v.Name, 'c')).OfType<int>().Concat(newCounters.Values).ToHashSet();
        for (int k = 125; k > 0; k--)
            if (!used.Contains(k)) { newCounters[name] = k; return (VarKind.Counter, k); }
        return null;
    }

    private int? onceMarker;

    /// <summary>A marker for "this has happened": GAC markers start clear.</summary>
    private int NewMarker()
    {
        var used = a.Variables.Concat(baseline.Variables).Select(v => Numbered(v.Name, 'm')).OfType<int>().ToHashSet();
        if (onceMarker is int last) used.UnionWith(Enumerable.Range(last, 256 - last));
        for (int k = 250; k > 3; k--)
            if (!used.Contains(k)) { onceMarker = k; return k; }
        throw new InvalidOperationException("No free GAC marker is left.");
    }

    /// <summary>Counters that should not start at 0 are set by a high priority line that runs once.</summary>
    private void InitialValues()
    {
        var code = new Code();
        foreach (var v in a.Variables)
        {
            var old = baseline.Variables.FirstOrDefault(b => b.Name == v.Name);
            if (v.InitialValue == (old?.InitialValue ?? 0)) continue;
            switch (Var(v.Name))
            {
                case (VarKind.Counter, var n):
                    code.K(v.InitialValue); code.K(n); code.Op(GacOps.Cset);
                    break;
                case (VarKind.Marker, var n):
                    code.K(n); code.Op(v.InitialValue != 0 ? GacOps.Set : GacOps.Rese);
                    break;
                default:
                    warnings.Add($"The starting value of \"{v.Name}\" can't be set in GAC");
                    break;
            }
        }
        if (code.Bytes.Count == 0) return;
        int marker = NewMarker();
        var line = new Code();
        line.K(marker); line.Op(GacOps.ResQ); line.Op(GacOps.If);
        line.K(marker); line.Op(GacOps.Set);
        line.Bytes.AddRange(code.Bytes);
        line.Op(GacOps.End);
        appended.Insert(0, new Compiled(Table.High, 0, line.Bytes.ToArray()));
    }

    // ================================================================= compiling a trigger

    private sealed class Code
    {
        public readonly List<byte> Bytes = new();
        public int Terms;
        public void K(int n) { Bytes.Add((byte)(0x80 | ((n >> 8) & 0x7F))); Bytes.Add((byte)n); }
        public void Op(int op) => Bytes.Add((byte)op);
    }

    private sealed class Skip : Exception
    {
        public Skip(string why) : base(why) { }
    }

    private Compiled? Compile(Trigger t)
    {
        if (!t.Enabled) { warnings.Add("Disabled triggers can't be written to GAC and were left out"); return null; }
        Table table;
        int room = 0;
        switch (t.Event)
        {
            case TriggerEvent.EveryTurn or TriggerEvent.GameStart:
                table = Table.High;
                break;
            case TriggerEvent.BeforeCommand or TriggerEvent.AfterCommand:
                if (t.Event == TriggerEvent.AfterCommand) warnings.Add("GAC has no \"after the command\" stage; AfterCommand triggers became low priority conditions");
                if (t.RoomId != null) { table = Table.Local; room = RoomNumber(t.RoomId); }
                else table = Table.Low;
                break;
            case TriggerEvent.Subroutine:
                warnings.Add("GAC has no subroutines; subroutine triggers were left out");
                return null;
            default:
                warnings.Add($"GAC has no equivalent of {t.Event} triggers; they were left out");
                return null;
        }

        var code = new Code();
        try
        {
            void Term(Action emit) { emit(); if (code.Terms++ > 0) code.Op(GacOps.And); }
            void Alternatives(string pattern, Func<string, int?> number, int op, string what)
            {
                var numbers = pattern.Split('|').Select(w => number(w) ?? throw new Skip($"the {what} \"{w}\" isn't in the GAC vocabulary")).Distinct().ToList();
                Term(() =>
                {
                    for (int i = 0; i < numbers.Count; i++)
                    {
                        code.K(numbers[i]); code.Op(op);
                        if (i > 0) code.Op(GacOps.Or);
                    }
                });
            }

            if (t.Verb is { } verb and not "*" and not "") Alternatives(verb, VerbNumber, GacOps.Verb, "verb");
            if (t.Noun1 == "-") Term(() => { code.Op(GacOps.No1); code.K(0); code.Op(GacOps.Eq); });
            else if (t.Noun1 is { } n1 and not "*" and not "") Alternatives(n1, NounNumber, GacOps.Noun, "noun");
            if (t.Noun2 == "-") Term(() => { code.Op(GacOps.No2); code.K(0); code.Op(GacOps.Eq); });
            else if (t.Noun2 is { } n2 and not "*" and not "")
            {
                int n = NounNumber(n2) ?? throw new Skip($"the noun \"{n2}\" isn't in the GAC vocabulary");
                Term(() => { code.Op(GacOps.No2); code.K(n); code.Op(GacOps.Eq); });
            }
            if (t.Adverb is { } adv and not "*" and not "")
            {
                int n = adverbByWord.TryGetValue(adv, out var an) ? an : throw new Skip($"the adverb \"{adv}\" isn't in the GAC vocabulary");
                Term(() => { code.K(n); code.Op(GacOps.Adve); });
            }
            if (t.Preposition is { Length: > 0 } and not "*") throw new Skip("GAC has no prepositions");
            if (table == Table.High && t.RoomId != null)
            {
                int r = RoomNumber(t.RoomId);
                Term(() => { code.K(r); code.Op(GacOps.At); });
            }
            int? once = null;
            if (t.OnceOnly || t.Event == TriggerEvent.GameStart)
            {
                int m = NewMarker();
                once = m;
                Term(() => { code.K(m); code.Op(GacOps.ResQ); });
            }
            foreach (var c in t.Conditions) Term(() => Condition(c, code));
            if (code.Terms == 0) code.K(1);
            code.Op(GacOps.If);

            int before = code.Bytes.Count;
            if (once is int marker) { code.K(marker); code.Op(GacOps.Set); }
            Actions(t.Actions, code);
            bool ends = t.Actions.Any(x => x.Type is ActionType.Done or ActionType.Ok or ActionType.Quit or ActionType.Win or ActionType.Lose);
            if (table != Table.High && t.StopsCommand && !ends) code.Op(GacOps.Wait);
            if (code.Bytes.Count == before) return null;
            code.Op(GacOps.End);
        }
        catch (Skip e)
        {
            warnings.Add($"Triggers GAC can't express were left out ({e.Message})");
            return null;
        }
        return new Compiled(table, room, code.Bytes.ToArray());
    }

    private void Object(string? id, Code code)
    {
        switch (id)
        {
            case "$noun1": code.Op(GacOps.No1); return;
            case "$noun2": code.Op(GacOps.No2); return;
        }
        code.K(ObjectNumber(id) ?? throw new Skip($"\"{id}\" isn't an object"));
    }

    private void Value(string? variable, Code code)
    {
        switch (Var(variable))
        {
            case (VarKind.Counter, var n): code.K(n); code.Op(GacOps.Ctr); return;
            case (VarKind.Turns, _): code.Op(GacOps.Turn); return;
            case (VarKind.Room, _): code.Op(GacOps.Room); return;
            default: throw new Skip($"the variable \"{variable}\" can't be used as a number in GAC");
        }
    }

    private void Condition(Condition c, Code code)
    {
        switch (c.Type)
        {
            case ConditionType.Always: code.K(1); break;
            case ConditionType.PlayerIn: code.K(RoomNumber(c.A)); code.Op(GacOps.At); break;
            case ConditionType.ItemPresent: Object(c.A, code); code.Op(GacOps.Avai); break;
            case ConditionType.ItemCarried: Object(c.A, code); code.Op(GacOps.Carr); break;
            case ConditionType.ItemIn:
                switch (c.B)
                {
                    case Locations.Here: Object(c.A, code); code.Op(GacOps.Here); break;
                    case Locations.Carried or Locations.Worn: Object(c.A, code); code.Op(GacOps.Carr); break;
                    default:
                        Object(c.A, code);
                        code.K(LocationNumber(c.B, "an object") ?? 0);
                        code.Op(GacOps.In);
                        break;
                }
                break;
            case ConditionType.ItemExists:
                Object(c.A, code); code.K(0); code.Op(GacOps.In); code.Op(GacOps.Not);
                break;
            case ConditionType.VarEquals or ConditionType.VarGreater or ConditionType.VarLess:
                if (Var(c.A) is (VarKind.Marker, var m))
                {
                    if (c.Type != ConditionType.VarEquals) throw new Skip("GAC markers are only set or clear");
                    code.K(m); code.Op(c.N == 0 ? GacOps.ResQ : GacOps.SetQ);
                    break;
                }
                Value(c.A, code);
                code.K(c.N);
                code.Op(c.Type == ConditionType.VarEquals ? GacOps.Eq : c.Type == ConditionType.VarGreater ? GacOps.Gt : GacOps.Lt);
                break;
            case ConditionType.VarEqualsVar or ConditionType.VarGreaterVar or ConditionType.VarLessVar:
                Value(c.A, code);
                Value(c.B, code);
                if (c.N != 0) { code.K(Math.Abs(c.N)); code.Op(c.N > 0 ? GacOps.Plus : GacOps.Minus); }
                code.Op(c.Type == ConditionType.VarEqualsVar ? GacOps.Eq : c.Type == ConditionType.VarGreaterVar ? GacOps.Gt : GacOps.Lt);
                break;
            case ConditionType.Chance:
                code.K(100); code.Op(GacOps.Rand); code.K(Math.Clamp(c.N, 0, 100)); code.Op(GacOps.Lt);
                break;
            case ConditionType.Noun1Is or ConditionType.Noun2Is:
                code.Op(c.Type == ConditionType.Noun1Is ? GacOps.No1 : GacOps.No2);
                code.K(NounNumber(c.A ?? "") ?? throw new Skip($"the noun \"{c.A}\" isn't in the GAC vocabulary"));
                code.Op(GacOps.Eq);
                break;
            case ConditionType.AdverbUsed when c.A is { Length: > 0 } adv && adverbByWord.TryGetValue(adv, out var an):
                code.K(an); code.Op(GacOps.Adve);
                break;
            case ConditionType.WordUsed when c.A is { } w:
                if (VerbNumber(w) is int vn) { code.K(vn); code.Op(GacOps.Verb); }
                else if (nounByWord.TryGetValue(w, out var nn)) { code.K(nn); code.Op(GacOps.Noun); }
                else if (adverbByWord.TryGetValue(w, out var av)) { code.K(av); code.Op(GacOps.Adve); }
                else throw new Skip($"the word \"{w}\" isn't in the GAC vocabulary");
                break;
            default:
                throw new Skip($"{c.Type} conditions have no GAC equivalent");
        }
        if (c.Negate) code.Op(GacOps.Not);
    }

    // ================================================================= actions

    private void Actions(List<GameAction> actions, Code code)
    {
        foreach (var x in actions)
        {
            switch (x.Type)
            {
                case ActionType.Message: Say(x.Text ?? "", x.N == 1, code); break;
                case ActionType.TakeItem: Object(x.A, code); code.Op(GacOps.Get); break;
                case ActionType.DropItem: Object(x.A, code); code.Op(GacOps.Drop); break;
                case ActionType.SwapItems: Object(x.A, code); Object(x.B, code); code.Op(GacOps.Swap); break;
                case ActionType.DestroyItem: Object(x.A, code); code.K(0); code.Op(GacOps.To); break;
                case ActionType.MoveItem or ActionType.CreateItem:
                    Object(x.A, code);
                    if (x.B == Locations.Here || x.Type == ActionType.CreateItem && string.IsNullOrEmpty(x.B)) code.Op(GacOps.Brin);
                    else { code.K(LocationNumber(x.B, "an object") ?? 0); code.Op(GacOps.To); }
                    break;
                case ActionType.SetVar when x.A == "gac_dark":
                    break;
                case ActionType.SetVar:
                    switch (Var(x.A))
                    {
                        case (VarKind.Marker, var m): code.K(m); code.Op(x.N != 0 ? GacOps.Set : GacOps.Rese); break;
                        case (VarKind.Counter, var n): code.K(x.N); code.K(n); code.Op(GacOps.Cset); break;
                        default: warnings.Add($"Setting \"{x.A}\" has no GAC equivalent and was left out"); break;
                    }
                    break;
                case ActionType.AddVar or ActionType.AwardScore:
                {
                    var name = x.Type == ActionType.AwardScore ? "@score" : x.A;
                    if (Var(name) is not (VarKind.Counter, var n)) { warnings.Add($"Changing \"{name}\" has no GAC equivalent and was left out"); break; }
                    if (x.N == 1) { code.K(n); code.Op(GacOps.Incr); }
                    else if (x.N == -1) { code.K(n); code.Op(GacOps.Decr); }
                    else if (x.N != 0) { code.K(n); code.Op(GacOps.Ctr); code.K(Math.Abs(x.N)); code.Op(x.N > 0 ? GacOps.Plus : GacOps.Minus); code.K(n); code.Op(GacOps.Cset); }
                    break;
                }
                case ActionType.CopyVar:
                    if (Var(x.A) is not (VarKind.Counter, var to)) { warnings.Add($"Changing \"{x.A}\" has no GAC equivalent and was left out"); break; }
                    try { Value(x.B, code); code.K(to); code.Op(GacOps.Cset); }
                    catch (Skip) { warnings.Add($"Copying \"{x.B}\" has no GAC equivalent and was left out"); }
                    break;
                case ActionType.RandomVar:
                    if (Var(x.A) is not (VarKind.Counter, var rn)) { warnings.Add($"Changing \"{x.A}\" has no GAC equivalent and was left out"); break; }
                    code.K(Math.Max(1, x.N)); code.Op(GacOps.Rand); code.K(1); code.Op(GacOps.Plus); code.K(rn); code.Op(GacOps.Cset);
                    break;
                case ActionType.GoTo: code.K(RoomNumber(x.A)); code.Op(GacOps.Goto); break;
                case ActionType.Look: code.Op(GacOps.Look); break;
                case ActionType.Inventory: code.Op(GacOps.With); code.Op(GacOps.List); break;
                case ActionType.Ok: code.Op(GacOps.Okay); break;
                case ActionType.Done: code.Op(GacOps.Wait); break;
                case ActionType.Quit: code.Op(GacOps.Quit); break;
                case ActionType.Win or ActionType.Lose:
                    if (!string.IsNullOrWhiteSpace(x.Text)) Say(x.Text, false, code);
                    code.Op(GacOps.Exit);
                    break;
                case ActionType.Save: code.Op(GacOps.Save); break;
                case ActionType.Restore: code.Op(GacOps.Load); break;
                case ActionType.Pause: code.K(Math.Clamp(x.N / 20, 1, 32767)); code.Op(GacOps.Hold); break;
                case ActionType.ShowPicture: code.Op(GacOps.Pict); break;
                case ActionType.RunTrigger when x.A == "gac_darkness":
                    break;
                default:
                    warnings.Add($"{x.Type} actions have no GAC equivalent and were left out");
                    break;
            }
        }
    }

    private static readonly Regex Placeholder = new(@"\{([^{}]+)\}");

    /// <summary>Text: its words as messages, {noun1}/{score}/{turns}/{var:…} as OBJ and PRIN; an empty line is LF.</summary>
    private void Say(string text, bool runOn, Code code)
    {
        if (text.Length == 0)
        {
            if (!runOn) code.Op(GacOps.Lf);
            return;
        }
        int last = 0;
        void Literal(string s)
        {
            if (s.Length == 0) return;
            if (MessageFor(s) is int m) { code.K(m); code.Op(GacOps.Mess); }
        }
        foreach (Match p in Placeholder.Matches(text))
        {
            Literal(text[last..p.Index]);
            last = p.Index + p.Length;
            var key = p.Groups[1].Value.Trim();
            switch (key)
            {
                case "noun1": case "the noun1": code.Op(GacOps.No1); code.Op(GacOps.Obj); break;
                case "noun2": case "the noun2": code.Op(GacOps.No2); code.Op(GacOps.Obj); break;
                case "score": code.K(0); code.Op(GacOps.Ctr); code.Op(GacOps.Prin); break;
                case "turns": code.Op(GacOps.Turn); code.Op(GacOps.Prin); break;
                default:
                    if (key.StartsWith("var:") && Var(key[4..]) is (VarKind.Counter, var n)) { code.K(n); code.Op(GacOps.Ctr); code.Op(GacOps.Prin); }
                    else warnings.Add($"The text placeholder {{{key}}} has no GAC equivalent and was left out");
                    break;
            }
        }
        Literal(text[last..]);
    }
}
