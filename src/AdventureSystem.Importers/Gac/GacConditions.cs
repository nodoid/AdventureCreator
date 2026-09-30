using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Gac;

/// <summary>A value or statement rebuilt from GAC's postfix bytecode (a tiny expression tree).</summary>
internal sealed class GacNode
{
    public int Op { get; init; } = -1;          // -1 = constant
    public int Value { get; init; }
    public GacNode[] Args { get; init; } = Array.Empty<GacNode>();
    public string Text { get; init; } = "";
    public bool IsConst => Op < 0;
    public bool IsInfix => !IsConst && GacOps.IsInfix(Op);
    public string OperandText => IsInfix ? $"( {Text} )" : Text;
    public static GacNode Const(int v) => new() { Value = v, Text = v.ToString() };
}

/// <summary>One "IF ( ... ) actions" portion of a GAC condition line.</summary>
internal sealed class GacSegment
{
    public List<GacNode> Conditions { get; } = new();
    public List<GacNode> Statements { get; } = new();
    public string LineText { get; set; } = "";
}

internal sealed class GacConvertException : Exception
{
    public GacConvertException(string message) : base(message) { }
}

/// <summary>Verb = VERB n / VBNO = n; Noun = NOUN n (either noun slot); Noun1Exact/Noun2Exact = NO1/NO2 = n (0 = no noun).</summary>
internal enum LitKind { Verb, Noun, Adverb, Cond, Noun1Exact, Noun2Exact }

/// <summary>A literal of a condition in disjunctive normal form.</summary>
internal sealed class GacLit
{
    public LitKind Kind { get; init; }
    public int Number { get; init; }
    public Condition? Cond { get; init; }
    public bool Neg { get; init; }

    public GacLit Negated() => new() { Kind = Kind, Number = Number, Cond = Cond, Neg = !Neg };
    public string Key => Kind == LitKind.Cond ? $"C:{Cond!.Type}|{Cond.A}|{Cond.B}|{Cond.N}" : $"{Kind}:{Number}";
    public bool SameAtom(GacLit o) => Key == o.Key;
}

/// <summary>Splits condition tables into lines and rebuilds expression trees by symbolic stack evaluation.</summary>
internal static class GacDecompiler
{
    /// <summary>Splits a table into lines (each ending with END) and each line into IF segments.</summary>
    public static List<List<GacSegment>> Decompile(IReadOnlyList<GacCode> code)
    {
        var lines = new List<List<GacSegment>>();
        var stack = new List<GacNode>();
        var segments = new List<GacSegment>();
        var conds = new List<GacNode>();
        var current = new GacSegment();
        var text = new List<string>();

        GacNode Pop()
        {
            if (stack.Count == 0) return new GacNode { Value = 0, Text = "?" };
            var n = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return n;
        }

        void FinishLine()
        {
            if (current.Statements.Count > 0) segments.Add(current);
            var lineText = string.Join(" ", text);
            foreach (var s in segments) s.LineText = lineText;
            if (segments.Count > 0) lines.Add(segments);
            segments = new List<GacSegment>();
            conds = new List<GacNode>();
            current = new GacSegment();
            stack.Clear();
            text.Clear();
        }

        foreach (var c in code)
        {
            if (c.IsConstant)
            {
                stack.Add(GacNode.Const(c.Value));
                continue;
            }
            int op = c.Value;
            switch (op)
            {
                case GacOps.If:
                {
                    var cond = Pop();
                    if (current.Statements.Count > 0) segments.Add(current);
                    conds.Add(cond);
                    current = new GacSegment();
                    current.Conditions.AddRange(conds);
                    text.Add($"IF ( {cond.Text} )");
                    break;
                }
                case GacOps.End:
                    text.Add("END");
                    FinishLine();
                    break;
                case GacOps.Nop28:
                case GacOps.Nop29:
                    break;
                default:
                {
                    GacNode node;
                    var name = GacOps.Name(op);
                    if (GacOps.IsInfix(op))
                    {
                        var b = Pop();
                        var a = Pop();
                        node = new GacNode { Op = op, Args = new[] { a, b }, Text = $"{a.Text} {name} {b.OperandText}" };
                    }
                    else if (GacOps.IsPrefix(op))
                    {
                        var a = Pop();
                        node = new GacNode { Op = op, Args = new[] { a }, Text = $"{name} {a.OperandText}" };
                    }
                    else node = new GacNode { Op = op, Text = name };

                    if (GacOps.Pushes(op)) stack.Add(node);
                    else
                    {
                        current.Statements.Add(node);
                        text.Add(node.Text);
                    }
                    break;
                }
            }
        }
        if (current.Statements.Count > 0 || segments.Count > 0) FinishLine();
        return lines;
    }
}
