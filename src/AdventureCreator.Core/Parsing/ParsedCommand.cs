namespace AdventureCreator.Core.Parsing;

/// <summary>A noun phrase such as "the small brass key", "all the coins except the silver one", "it".</summary>
public sealed class NounPhrase
{
    public List<string> Words { get; init; } = new();
    public List<string> Adjectives { get; } = new();
    /// <summary>Head noun (may be a multi-word noun such as "control panel"), or null when only adjectives were given.</summary>
    public string? Noun { get; set; }
    public bool Plural { get; set; }
    public bool All { get; set; }
    public string? Pronoun { get; set; }
    public bool IsSelf { get; set; }
    /// <summary>1-based ordinal ("second key"), -1 for "last", 0 for none.</summary>
    public int Ordinal { get; set; }
    public int? Count { get; set; }
    /// <summary>Conjoined phrases ("lamp and key"). When non-empty this phrase is only a container.</summary>
    public List<NounPhrase> Parts { get; } = new();
    public List<NounPhrase> Except { get; } = new();
    /// <summary>Words not found in the lexicon.</summary>
    public List<string> UnknownWords { get; } = new();

    public bool IsCompound => Parts.Count > 0;
    public string Text => string.Join(' ', Words);

    public IEnumerable<NounPhrase> Simple() => IsCompound ? Parts : new[] { this };

    public override string ToString() => Text;

    /// <summary>Analyses the words of a noun phrase. Returns null if the words cannot form one.</summary>
    public static NounPhrase? Parse(IReadOnlyList<string> words, Lexicon lex)
    {
        if (words.Count == 0) return null;
        var phrase = new NounPhrase { Words = words.ToList() };

        // except / but
        int exceptAt = -1;
        for (int i = 0; i < words.Count; i++)
            if (lex.Is(words[i], WordKind.Exception) && !lex.Is(words[i], WordKind.Noun)) { exceptAt = i; break; }
        var include = exceptAt >= 0 ? words.Take(exceptAt).ToList() : words.ToList();
        if (exceptAt >= 0)
        {
            var rest = words.Skip(exceptAt + 1).Where(w => w != "for" && w != "from").ToList();
            var ex = Parse(rest, lex);
            if (ex == null) return null;
            phrase.Except.AddRange(ex.Simple());
        }

        // conjunctions
        var parts = new List<List<string>> { new() };
        foreach (var w in include)
        {
            if (w == "," || (lex.Is(w, WordKind.Conjunction) && !lex.Is(w, WordKind.Noun))) parts.Add(new());
            else parts[^1].Add(w);
        }
        parts.RemoveAll(p => p.Count == 0);
        if (parts.Count == 0) return phrase.Except.Count > 0 ? null : null;
        if (parts.Count > 1)
        {
            foreach (var p in parts)
            {
                var sub = ParseSimple(p, lex);
                if (sub == null) return null;
                phrase.Parts.Add(sub);
            }
            return phrase;
        }

        var simple = ParseSimple(parts[0], lex);
        if (simple == null) return null;
        simple.Except.AddRange(phrase.Except);
        return new NounPhrase { Words = words.ToList() }.CopyFrom(simple);
    }

    private NounPhrase CopyFrom(NounPhrase s)
    {
        Adjectives.AddRange(s.Adjectives);
        Noun = s.Noun; Plural = s.Plural; All = s.All; Pronoun = s.Pronoun; IsSelf = s.IsSelf;
        Ordinal = s.Ordinal; Count = s.Count;
        Except.AddRange(s.Except);
        UnknownWords.AddRange(s.UnknownWords);
        return this;
    }

    private static NounPhrase? ParseSimple(List<string> words, Lexicon lex)
    {
        var np = new NounPhrase { Words = words.ToList() };
        var content = new List<string>();
        foreach (var w in words)
        {
            var kind = lex.KindOf(w);
            if ((kind & (WordKind.Noun | WordKind.Adjective)) != 0) { content.Add(w); continue; }
            if ((kind & WordKind.Article) != 0 || (kind & WordKind.Ignored) != 0 || w == "of") continue;
            if ((kind & WordKind.Quantifier) != 0) { np.All = true; continue; }
            if ((kind & WordKind.Pronoun) != 0) { np.Pronoun = w; continue; }
            if ((kind & WordKind.Self) != 0) { np.IsSelf = true; continue; }
            if ((kind & WordKind.Ordinal) != 0) { np.Ordinal = BuiltInLexicon.Ordinals[w]; continue; }
            if ((kind & WordKind.Number) != 0)
            {
                np.Count = Lexicon.IsNumeric(w) ? int.Parse(w) : BuiltInLexicon.NumberWords[w];
                continue;
            }
            if (kind == WordKind.None) { np.UnknownWords.Add(w); content.Add(w); continue; }
            // Words of other kinds (verbs, directions, prepositions, adverbs) are allowed as nouns only if they
            // were also declared as nouns/adjectives above; otherwise the phrase is invalid.
            if ((kind & (WordKind.Direction)) != 0 && content.Count > 0) { content.Add(w); continue; }
            return null;
        }

        if (content.Count == 0)
        {
            if (np.All || np.Pronoun != null || np.IsSelf || np.Count.HasValue) return np;
            return null;
        }

        // Multi-word nouns: longest suffix first.
        for (int start = 0; start < content.Count - 1; start++)
        {
            var joined = string.Join(' ', content.Skip(start));
            if (lex.IsMultiWordNoun(joined))
            {
                np.Noun = joined;
                np.Adjectives.AddRange(content.Take(start));
                return np;
            }
        }

        int head = -1;
        for (int i = content.Count - 1; i >= 0; i--)
            if (lex.Is(content[i], WordKind.Noun)) { head = i; break; }

        if (head < 0)
        {
            // Plurals: "coins" -> "coin"
            var last = content[^1];
            var singular = Singular(last, lex);
            if (singular != null)
            {
                np.Noun = singular;
                np.Plural = true;
                np.Adjectives.AddRange(content.Take(content.Count - 1));
                return np;
            }
            if (np.UnknownWords.Contains(last))
            {
                np.Noun = last;
                np.Adjectives.AddRange(content.Take(content.Count - 1));
                return np;
            }
            np.Adjectives.AddRange(content);
            return np;
        }

        np.Noun = content[head];
        for (int i = 0; i < content.Count; i++)
            if (i != head) np.Adjectives.Add(content[i]);
        return np;
    }

    private static string? Singular(string w, Lexicon lex)
    {
        if (w.EndsWith("ies") && lex.Is(w[..^3] + "y", WordKind.Noun)) return w[..^3] + "y";
        if (w.EndsWith("es") && lex.Is(w[..^2], WordKind.Noun)) return w[..^2];
        if (w.EndsWith('s') && lex.Is(w[..^1], WordKind.Noun)) return w[..^1];
        return null;
    }
}

/// <summary>The result of parsing one command (one sentence of the player's input).</summary>
public sealed class ParsedCommand
{
    /// <summary>Verb id (the grammar's verb), or null when the player typed no verb.</summary>
    public string? VerbId { get; set; }
    /// <summary>The action to perform (usually equal to VerbId; grammar lines can redirect, e.g. "put on X" → wear).</summary>
    public string? ActionId { get; set; }
    /// <summary>Words the player used for the verb ("pick up").</summary>
    public string VerbWords { get; set; } = "";
    public NounPhrase? Object1 { get; set; }
    public NounPhrase? Object2 { get; set; }
    public SlotKind? Object1Slot { get; set; }
    public SlotKind? Object2Slot { get; set; }
    public List<string> Prepositions { get; } = new();
    public string? Preposition => Prepositions.FirstOrDefault();
    public List<string> Adverbs { get; } = new();
    public string? Direction { get; set; }
    public string? Text { get; set; }
    public int? Number { get; set; }
    /// <summary>"Robot, go north" – the character being addressed.</summary>
    public NounPhrase? Addressee { get; set; }
    /// <summary>All words of the command after normalisation.</summary>
    public List<string> Words { get; } = new();
    public string Input { get; set; } = "";
    /// <summary>True if no grammar line matched and the command was parsed loosely (verb + nouns).</summary>
    public bool Lenient { get; set; }
    /// <summary>Explanation of why strict parsing failed (for lenient parses).</summary>
    public string? GrammarError { get; set; }
    public GrammarLine? Grammar { get; set; }
    public bool IsMeta { get; set; }

    public override string ToString()
    {
        var parts = new List<string> { ActionId ?? "(no verb)" };
        if (Adverbs.Count > 0) parts.Add("[" + string.Join(",", Adverbs) + "]");
        if (Direction != null) parts.Add("dir=" + Direction);
        if (Object1 != null) parts.Add("obj1=" + Object1.Text);
        if (Preposition != null) parts.Add("prep=" + Preposition);
        if (Object2 != null) parts.Add("obj2=" + Object2.Text);
        if (Text != null) parts.Add("text=\"" + Text + "\"");
        if (Number != null) parts.Add("num=" + Number);
        if (Addressee != null) parts.Add("to=" + Addressee.Text);
        return string.Join(' ', parts);
    }
}

public sealed class ParseOutcome
{
    public List<ParsedCommand> Commands { get; } = new();
    /// <summary>Parse error for the first sentence that failed; commands before it are still returned.</summary>
    public string? Error { get; set; }
    public string? UnknownWord { get; set; }
    public List<(string From, string To)> Corrections { get; } = new();
    public bool Success => Error == null;
}
