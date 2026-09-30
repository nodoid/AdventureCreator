using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Gac;

internal sealed partial class GacConverter
{
    private enum OutKind { Message, Inline, LineFeed, Other }

    private static readonly string[] WinWords =
    {
        "congratulat", "well done", "you have won", "you win", "you've won", "you have completed", "you have succeeded",
        "enhorabuena", "felicidades", "has ganado", "felicitaciones",
    };

    /// <summary>Converts the statements of one IF segment to actions; untranslatable ones are reported in <paramref name="notes"/>.</summary>
    private List<GameAction> ConvertActions(List<GacNode> statements, List<string> notes)
    {
        var output = new List<(GameAction Action, OutKind Kind)>();
        void Add(GameAction a, OutKind k = OutKind.Other) => output.Add((a, k));

        foreach (var s in statements)
        {
            try
            {
                switch (s.Op)
                {
                    case GacOps.Hold:
                        Add(new GameAction(ActionType.Pause, n: ConstArg(s, "HOLD") * 20));
                        break;
                    case GacOps.Get: Add(new GameAction(ActionType.TakeItem, ObjRef(s.Args[0]))); break;
                    case GacOps.Drop: Add(new GameAction(ActionType.DropItem, ObjRef(s.Args[0]))); break;
                    case GacOps.Swap: Add(new GameAction(ActionType.SwapItems, ObjRef(s.Args[0]), b: ObjRef(s.Args[1]))); break;
                    case GacOps.To: Add(new GameAction(ActionType.MoveItem, ObjRef(s.Args[0]), b: LocRef(s.Args[1]))); break;
                    case GacOps.Brin: Add(new GameAction(ActionType.MoveItem, ObjRef(s.Args[0]), b: Locations.Here)); break;
                    case GacOps.Find: throw new GacConvertException("FIND (go to where an object is) has no equivalent and was left out");
                    case GacOps.Obj:
                    {
                        var a = s.Args[0];
                        if (a.IsConst)
                            Add(GameAction.Say(db.Objects.TryGetValue(a.Value, out var o) ? o.Name.Trim() : ""), OutKind.Inline);
                        else if (a.Op == GacOps.No1) Add(GameAction.Say("{noun1}"), OutKind.Inline);
                        else if (a.Op == GacOps.No2) Add(GameAction.Say("{noun2}"), OutKind.Inline);
                        else throw new GacConvertException($"\"{s.Text}\" names a computed object");
                        break;
                    }
                    case GacOps.Set:
                    case GacOps.Rese:
                    {
                        int f = ConstArg(s, s.Op == GacOps.Set ? "SET" : "RESE");
                        Add(new GameAction(ActionType.SetVar, Marker(f), s.Op == GacOps.Set ? 1 : 0));
                        if (usesDarkness && f is 1 or 2) Add(new GameAction(ActionType.RunTrigger, DarknessRoutine));
                        break;
                    }
                    case GacOps.Cset: ConvertCset(s, Add, notes); break;
                    case GacOps.Incr:
                    case GacOps.Decr:
                    {
                        int c = ConstArg(s, s.Op == GacOps.Incr ? "INCR" : "DECR");
                        int d = s.Op == GacOps.Incr ? 1 : -1;
                        if (c == 0) Add(new GameAction(ActionType.AwardScore, n: d));
                        else if (c is 126 or 127) throw new GacConvertException("changing the turn counter has no equivalent");
                        else Add(new GameAction(ActionType.AddVar, Counter(c), d));
                        break;
                    }
                    case GacOps.Desc:
                    {
                        var a = s.Args[0];
                        if (a.Op == GacOps.Room || (a.IsConst && table == Table.Local && a.Value == localRoom)) Add(new GameAction(ActionType.Look));
                        else throw new GacConvertException("DESC of another room has no equivalent");
                        break;
                    }
                    case GacOps.Look: Add(new GameAction(ActionType.Look)); break;
                    case GacOps.Mess:
                    {
                        int m = ConstArg(s, "MESS");
                        if (!db.Messages.TryGetValue(m, out var text)) throw new GacConvertException($"message {m} does not exist");
                        Add(GameAction.Say(text), OutKind.Message);
                        break;
                    }
                    case GacOps.Prin:
                    {
                        var t = Resolve(s.Args[0]);
                        string text = t switch
                        {
                            { Kind: TermKind.Const } => t.N.ToString(),
                            { Kind: TermKind.Var, Var: "@score", Offset: 0 } => "{score}",
                            { Kind: TermKind.Var, Var: "@turns", Offset: 0 } => "{turns}",
                            { Kind: TermKind.Var, Offset: 0 } => $"{{var:{t.Var}}}",
                            _ => throw new GacConvertException("PRIN of a computed value cannot be converted"),
                        };
                        if (text.StartsWith("{var:")) notes.Add($"PRIN of a counter uses the placeholder {text}");
                        Add(GameAction.Say(text), OutKind.Inline);
                        break;
                    }
                    case GacOps.Save: Add(new GameAction(ActionType.Save)); break;
                    case GacOps.Load: Add(new GameAction(ActionType.Restore)); break;
                    case GacOps.Okay: Add(new GameAction(ActionType.Ok)); break;
                    case GacOps.Wait: Add(new GameAction(ActionType.Done)); break;
                    case GacOps.Quit: Add(new GameAction(ActionType.Quit)); break;
                    case GacOps.Exit:
                    {
                        var last = output.LastOrDefault(o => o.Action.Type == ActionType.Message).Action?.Text ?? "";
                        bool win = WinWords.Any(w => last.Contains(w, StringComparison.OrdinalIgnoreCase));
                        Add(new GameAction(win ? ActionType.Win : ActionType.Lose, text: ""));
                        break;
                    }
                    case GacOps.Goto: Add(new GameAction(ActionType.GoTo, $"r{ConstArg(s, "GOTO")}")); break;
                    case GacOps.List:
                    {
                        var a = s.Args[0];
                        if (a.Op == GacOps.With || (a.IsConst && a.Value == CarriedRoom)) Add(new GameAction(ActionType.Inventory));
                        else throw new GacConvertException("LIST of a room's objects has no equivalent");
                        break;
                    }
                    case GacOps.Pict: Add(new GameAction(ActionType.ShowPicture)); break;
                    case GacOps.Text: notes.Add("TEXT (switch pictures off) was ignored"); break;
                    case GacOps.Stre:
                    {
                        int v = ConstArg(s, "STRE");
                        if (strength is int old && old != v) notes.Add($"STRE {v} conflicts with an earlier STRE {old}; the maximum weight is fixed at {old}");
                        else strength = v;
                        break;
                    }
                    case GacOps.Lf: Add(GameAction.Say(""), OutKind.LineFeed); break;
                    default:
                        throw new GacConvertException($"\"{s.Text}\" is not supported");
                }
            }
            catch (GacConvertException e)
            {
                notes.Add($"left out \"{s.Text}\" ({e.Message})");
            }
        }

        // Line breaks: GAC prints messages as a continuous stream; LF starts a new line. Our Message action adds
        // a line break unless N = 1, so join messages that run into an object name/number, and fold LF into the
        // preceding message.
        var result = new List<GameAction>();
        for (int i = 0; i < output.Count; i++)
        {
            var (a, kind) = output[i];
            var next = i + 1 < output.Count ? output[i + 1].Kind : OutKind.Other;
            switch (kind)
            {
                case OutKind.LineFeed:
                    if (result.Count > 0 && result[^1].Type == ActionType.Message && result[^1].N == 1) result[^1].N = 0;
                    else result.Add(a);
                    break;
                case OutKind.Message:
                    a.N = next == OutKind.Inline || (next == OutKind.Message && (a.Text ?? "").EndsWith(' ')) ? 1 : 0;
                    if (a.N == 0) a.Text = (a.Text ?? "").TrimEnd();
                    result.Add(a);
                    break;
                case OutKind.Inline:
                    a.N = next is OutKind.Message or OutKind.Inline ? 1 : 0;
                    result.Add(a);
                    break;
                default:
                    result.Add(a);
                    break;
            }
        }
        return result;
    }

    private string Counter(int c)
    {
        counters.Add(c);
        return $"c{c}";
    }

    private void ConvertCset(GacNode s, Action<GameAction, OutKind> add, List<string> notes)
    {
        if (!s.Args[1].IsConst) throw new GacConvertException("CSET of a computed counter number");
        int c = s.Args[1].Value;
        var value = Resolve(s.Args[0]);
        if (c == 0)
        {
            if (value is { Kind: TermKind.Var, Var: "@score" })
            {
                if (value.Offset != 0) add(new GameAction(ActionType.AwardScore, n: value.Offset), OutKind.Other);
                return;
            }
            if (value.Kind == TermKind.Const && value.N == 0)
            {
                notes.Add("setting the score (counter 0) to 0 was ignored");
                return;
            }
            throw new GacConvertException("setting the score (counter 0) to a fixed value has no equivalent; only additions are converted");
        }
        if (c is 126 or 127) throw new GacConvertException("changing the turn counter has no equivalent");
        var name = Counter(c);
        switch (value.Kind)
        {
            case TermKind.Const:
                add(new GameAction(ActionType.SetVar, name, value.N), OutKind.Other);
                return;
            case TermKind.Var when value.Var == name:
                if (value.Offset != 0) add(new GameAction(ActionType.AddVar, name, value.Offset), OutKind.Other);
                return;
            case TermKind.Var:
                add(new GameAction(ActionType.CopyVar, name, b: value.Var), OutKind.Other);
                if (value.Offset != 0) add(new GameAction(ActionType.AddVar, name, value.Offset), OutKind.Other);
                return;
            case TermKind.Room:
                needRoomIndex = true;
                add(new GameAction(ActionType.CopyVar, name, b: "@room"), OutKind.Other);
                if (value.Offset != 0) add(new GameAction(ActionType.AddVar, name, value.Offset), OutKind.Other);
                return;
            case TermKind.Rand:
                // GAC's RAND n gives 0..n-1; RandomVar gives 1..n.
                add(new GameAction(ActionType.RandomVar, name, Math.Max(1, value.N)), OutKind.Other);
                if (value.Offset - 1 != 0) add(new GameAction(ActionType.AddVar, name, value.Offset - 1), OutKind.Other);
                return;
        }
        throw new GacConvertException("the value is computed in a way that cannot be converted");
    }
}
