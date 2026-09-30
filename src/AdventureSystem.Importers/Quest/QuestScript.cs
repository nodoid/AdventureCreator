using System.Globalization;
using System.Text;

namespace AdventureSystem.Importers.Quest;

/// <summary>A parsed Quest 5 script statement.</summary>
internal abstract record QStatement(string Source);
internal sealed record QCall(string Source, string Name, List<QExpr> Args) : QStatement(Source);
internal sealed record QAssign(string Source, QExpr Target, QExpr Value) : QStatement(Source);
internal sealed record QIf(string Source, List<(QExpr Condition, List<QStatement> Body)> Branches, List<QStatement>? Else) : QStatement(Source);
internal sealed record QFirstTime(string Source, List<QStatement> Body, List<QStatement>? Otherwise) : QStatement(Source);
internal sealed record QSwitch(string Source, QExpr Subject, List<(QExpr Value, List<QStatement> Body)> Cases, List<QStatement>? Default) : QStatement(Source);
internal sealed record QUnknown(string Source) : QStatement(Source);

/// <summary>A parsed Quest expression.</summary>
internal abstract record QExpr;
internal sealed record QNumber(int Value) : QExpr;
internal sealed record QString(string Value) : QExpr;
internal sealed record QBool(bool Value) : QExpr;
internal sealed record QName(string Name) : QExpr;                         // object or variable
internal sealed record QMember(QExpr Target, string Attribute) : QExpr;    // obj.attr
internal sealed record QFunc(string Name, List<QExpr> Args) : QExpr;
internal sealed record QUnary(string Op, QExpr Operand) : QExpr;
internal sealed record QBinary(string Op, QExpr Left, QExpr Right) : QExpr;

/// <summary>Parses Quest 5 script text (the subset the importer can use; anything else becomes <see cref="QUnknown"/>).</summary>
internal static class QuestScript
{
    public static List<QStatement> Parse(string script)
    {
        var tokens = Tokenise(script);
        int p = 0;
        return Block(tokens, ref p, topLevel: true);
    }

    // ================================================================= tokens

    private enum T { Word, Number, String, Symbol, Newline, End }
    private sealed record Tok(T Kind, string Text, int Line);

    private static List<Tok> Tokenise(string s)
    {
        var list = new List<Tok>();
        int line = 0;
        for (int i = 0; i < s.Length;)
        {
            char c = s[i];
            if (c == '\n') { list.Add(new Tok(T.Newline, "\n", line++)); i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1] == 'n' ? '\n' : s[i + 1]); i += 2; continue; }
                    sb.Append(s[i++]);
                }
                i++;
                list.Add(new Tok(T.String, sb.ToString(), line));
                continue;
            }
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                list.Add(new Tok(T.Number, s[start..i], line));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))) i++;
                list.Add(new Tok(T.Word, s[start..i], line));
                continue;
            }
            string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
            if (two is "<>" or ">=" or "<=" or "==" or "!=" or "=>" or "&&" or "||") { list.Add(new Tok(T.Symbol, two, line)); i += 2; continue; }
            list.Add(new Tok(T.Symbol, c.ToString(), line));
            i++;
        }
        list.Add(new Tok(T.End, "", line));
        return list;
    }

    // ================================================================= statements

    private static void SkipNewlines(List<Tok> t, ref int p) { while (t[p].Kind == T.Newline) p++; }

    private static List<QStatement> Block(List<Tok> t, ref int p, bool topLevel)
    {
        var list = new List<QStatement>();
        while (true)
        {
            SkipNewlines(t, ref p);
            if (t[p].Kind == T.End) return list;
            if (t[p].Kind == T.Symbol && t[p].Text == "}")
            {
                if (!topLevel) { p++; return list; }
                p++;
                continue;
            }
            list.Add(Statement(t, ref p));
        }
    }

    private static List<QStatement> Braced(List<Tok> t, ref int p)
    {
        SkipNewlines(t, ref p);
        if (t[p].Kind == T.Symbol && t[p].Text == "{") { p++; return Block(t, ref p, topLevel: false); }
        return new List<QStatement> { Statement(t, ref p) };
    }

    private static string SourceOf(List<Tok> t, int from, int to) =>
        string.Join(" ", t.Skip(from).Take(Math.Max(0, to - from)).Where(x => x.Kind != T.Newline).Select(x => x.Kind == T.String ? $"\"{x.Text}\"" : x.Text));

    private static QStatement Statement(List<Tok> t, ref int p)
    {
        int start = p;
        var tok = t[p];
        try
        {
            if (tok.Kind == T.Word)
            {
                switch (tok.Text)
                {
                    case "if":
                    {
                        p++;
                        var branches = new List<(QExpr, List<QStatement>)>();
                        var cond = Paren(t, ref p);
                        branches.Add((cond, Braced(t, ref p)));
                        List<QStatement>? elseBody = null;
                        while (true)
                        {
                            int save = p;
                            SkipNewlines(t, ref p);
                            if (t[p].Kind == T.Word && t[p].Text == "else")
                            {
                                p++;
                                if (t[p].Kind == T.Word && t[p].Text == "if")
                                {
                                    p++;
                                    var c2 = Paren(t, ref p);
                                    branches.Add((c2, Braced(t, ref p)));
                                    continue;
                                }
                                elseBody = Braced(t, ref p);
                            }
                            else p = save;
                            break;
                        }
                        return new QIf(SourceOf(t, start, p), branches, elseBody);
                    }
                    case "firsttime":
                    {
                        p++;
                        var body = Braced(t, ref p);
                        int save = p;
                        SkipNewlines(t, ref p);
                        List<QStatement>? otherwise = null;
                        if (t[p].Kind == T.Word && t[p].Text == "otherwise") { p++; otherwise = Braced(t, ref p); }
                        else p = save;
                        return new QFirstTime(SourceOf(t, start, p), body, otherwise);
                    }
                    case "switch":
                    {
                        p++;
                        var subject = Paren(t, ref p);
                        SkipNewlines(t, ref p);
                        Expect(t, ref p, "{");
                        var cases = new List<(QExpr, List<QStatement>)>();
                        List<QStatement>? def = null;
                        while (true)
                        {
                            SkipNewlines(t, ref p);
                            if (t[p].Text == "}") { p++; break; }
                            if (t[p].Text == "case")
                            {
                                p++;
                                var value = Paren(t, ref p);
                                cases.Add((value, Braced(t, ref p)));
                            }
                            else if (t[p].Text == "default") { p++; def = Braced(t, ref p); }
                            else throw new FormatException("switch");
                        }
                        return new QSwitch(SourceOf(t, start, p), subject, cases, def);
                    }
                    case "foreach":
                    case "for":
                    case "while":
                    case "wait":
                    case "on":
                    {
                        // Loops and callbacks aren't converted: skip the header and its block.
                        p++;
                        if (t[p].Text == "(") Paren(t, ref p);
                        Braced(t, ref p);
                        return new QUnknown(SourceOf(t, start, p));
                    }
                }
            }
            // Assignment or call.
            var lhs = Expr(t, ref p);
            if (t[p].Kind == T.Symbol && t[p].Text == "=")
            {
                p++;
                var rhs = Expr(t, ref p);
                EndOfStatement(t, ref p);
                return new QAssign(SourceOf(t, start, p), lhs, rhs);
            }
            if (t[p].Kind == T.Symbol && t[p].Text == "=>")
            {
                // obj.changedparent => { … }: a change handler.
                p++;
                Braced(t, ref p);
                return new QUnknown(SourceOf(t, start, p));
            }
            if (lhs is QFunc f)
            {
                EndOfStatement(t, ref p);
                return new QCall(SourceOf(t, start, p), f.Name, f.Args);
            }
            if (lhs is QName n)
            {
                // A call without parentheses: finish, SuppressTurnscripts…
                var args = new List<QExpr>();
                while (t[p].Kind is not (T.Newline or T.End) && !(t[p].Kind == T.Symbol && t[p].Text == "}"))
                {
                    args.Add(Expr(t, ref p));
                    if (t[p].Text == ",") p++;
                }
                return new QCall(SourceOf(t, start, p), n.Name, args);
            }
            throw new FormatException("statement");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            // Skip to the end of the line (and any block that starts on it).
            p = start;
            int depth = 0;
            while (t[p].Kind != T.End)
            {
                if (t[p].Text == "{") depth++;
                if (t[p].Text == "}") { if (depth == 0) break; depth--; if (depth == 0) { p++; break; } }
                if (t[p].Kind == T.Newline && depth == 0) break;
                p++;
            }
            return new QUnknown(SourceOf(t, start, p));
        }
    }

    private static void EndOfStatement(List<Tok> t, ref int p)
    {
        if (t[p].Kind is T.Newline or T.End || t[p].Text == "}") return;
        throw new FormatException("end of statement");
    }

    private static void Expect(List<Tok> t, ref int p, string text)
    {
        if (t[p].Text != text) throw new FormatException(text);
        p++;
    }

    private static QExpr Paren(List<Tok> t, ref int p)
    {
        Expect(t, ref p, "(");
        inCondition++;
        try
        {
            var e = Expr(t, ref p);
            Expect(t, ref p, ")");
            return e;
        }
        finally { inCondition--; }
    }

    // ================================================================= expressions (precedence climbing)

    private static QExpr Expr(List<Tok> t, ref int p) => Or(t, ref p);

    private static QExpr Or(List<Tok> t, ref int p)
    {
        var left = And(t, ref p);
        while (t[p].Text is "or" or "||") { p++; left = new QBinary("or", left, And(t, ref p)); }
        return left;
    }

    private static QExpr And(List<Tok> t, ref int p)
    {
        var left = Not(t, ref p);
        while (t[p].Text is "and" or "&&") { p++; left = new QBinary("and", left, Not(t, ref p)); }
        return left;
    }

    private static QExpr Not(List<Tok> t, ref int p)
    {
        if (t[p].Text is "not" or "!") { p++; return new QUnary("not", Not(t, ref p)); }
        return Compare(t, ref p);
    }

    private static QExpr Compare(List<Tok> t, ref int p)
    {
        var left = Sum(t, ref p);
        // "=" inside an expression is comparison (the statement parser handles assignment first).
        if (t[p].Kind == T.Symbol && t[p].Text is "<>" or "!=" or ">" or "<" or ">=" or "<=" or "==")
        {
            var op = t[p].Text is "!=" ? "<>" : t[p].Text == "==" ? "=" : t[p].Text;
            p++;
            return new QBinary(op, left, Sum(t, ref p));
        }
        if (t[p].Kind == T.Symbol && t[p].Text == "=" && inCondition > 0)
        {
            p++;
            return new QBinary("=", left, Sum(t, ref p));
        }
        return left;
    }

    [ThreadStatic] private static int inCondition;

    private static QExpr Sum(List<Tok> t, ref int p)
    {
        var left = Product(t, ref p);
        while (t[p].Kind == T.Symbol && t[p].Text is "+" or "-") { var op = t[p++].Text; left = new QBinary(op, left, Product(t, ref p)); }
        return left;
    }

    private static QExpr Product(List<Tok> t, ref int p)
    {
        var left = Primary(t, ref p);
        while (t[p].Kind == T.Symbol && t[p].Text is "*" or "/" or "%") { var op = t[p++].Text; left = new QBinary(op, left, Primary(t, ref p)); }
        return left;
    }

    private static QExpr Primary(List<Tok> t, ref int p)
    {
        var tok = t[p];
        switch (tok.Kind)
        {
            case T.Number: p++; return new QNumber(int.Parse(tok.Text, CultureInfo.InvariantCulture));
            case T.String: p++; return new QString(tok.Text);
            case T.Symbol when tok.Text == "-": p++; return new QUnary("-", Primary(t, ref p));
            case T.Symbol when tok.Text == "(":
            {
                p++;
                inCondition++;
                try
                {
                    var e = Expr(t, ref p);
                    Expect(t, ref p, ")");
                    return e;
                }
                finally { inCondition--; }
            }
            case T.Word:
            {
                p++;
                if (tok.Text is "true" or "false") return new QBool(tok.Text == "true");
                if (t[p].Kind == T.Symbol && t[p].Text == "(")
                {
                    p++;
                    var args = new List<QExpr>();
                    inCondition++;
                    try
                    {
                        while (t[p].Text != ")")
                        {
                            args.Add(Expr(t, ref p));
                            if (t[p].Text == ",") p++;
                            else if (t[p].Text != ")") throw new FormatException("arguments");
                        }
                        p++;
                    }
                    finally { inCondition--; }
                    return new QFunc(tok.Text, args);
                }
                // Dotted names: obj.attr (the tokeniser keeps "a.b.c" together).
                var parts = tok.Text.Split('.');
                QExpr e2 = new QName(parts[0]);
                for (int i = 1; i < parts.Length; i++) e2 = new QMember(e2, parts[i]);
                return e2;
            }
        }
        throw new FormatException("expression");
    }

    /// <summary>Parses a condition: "=" means equality.</summary>
    public static QExpr? ParseCondition(string text)
    {
        var tokens = Tokenise(text);
        int p = 0;
        inCondition++;
        try { return Expr(tokens, ref p); }
        catch (FormatException) { return null; }
        finally { inCondition--; }
    }
}
