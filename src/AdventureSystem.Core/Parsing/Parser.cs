using System.Text;

namespace AdventureSystem.Core.Parsing;

/// <summary>
/// Natural language command parser.
/// <para>Features: multiple commands per line ("take lamp then go north", "open door and go in"),
/// grammar-line matching with direct/indirect objects and prepositions, phrasal verbs ("pick up", "switch off"),
/// adverbs anywhere in the sentence ("quietly open the door"), adjectives, multi-word nouns, plurals, "all"/"except",
/// pronouns, ordinals, numbers, quoted text, addressing characters ("robot, go north" / "tell robot to go north"),
/// idioms ("where am I"), filler phrases ("I want to…", "please"), PAWS/Quill style significant-letter matching,
/// and spelling correction.</para>
/// </summary>
public sealed class Parser
{
    public Lexicon Lexicon { get; }
    public bool SpellingCorrection { get; set; } = true;

    public Parser(Lexicon lexicon) => Lexicon = lexicon;

    // ---------------------------------------------------------------- tokenising

    private sealed class Token
    {
        public string Text = "";
        public bool Quoted;
        public bool Punctuation;
        public override string ToString() => Text;
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length > 0) { tokens.Add(new Token { Text = sb.ToString().ToLowerInvariant() }); sb.Clear(); }
        }

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"' || c == '“' || c == '”')
            {
                Flush();
                int end = input.IndexOfAny(new[] { '"', '”', '“' }, i + 1);
                if (end < 0) end = input.Length;
                tokens.Add(new Token { Text = input[(i + 1)..end].Trim(), Quoted = true });
                i = end;
            }
            else if (char.IsWhiteSpace(c)) Flush();
            else if (c is '.' or ',' or ';' or '!' or '?' or ':')
            {
                // Keep decimal points and times such as 3.14 / 10:30 inside numbers.
                if ((c == '.' || c == ':') && sb.Length > 0 && char.IsDigit(sb[^1]) && i + 1 < input.Length && char.IsDigit(input[i + 1]))
                {
                    sb.Append(c);
                    continue;
                }
                Flush();
                tokens.Add(new Token { Text = c.ToString(), Punctuation = true });
            }
            else if (c == '\'' || c == '’')
            {
                // Possessive 's is dropped ("the troll's axe" -> "the troll axe"); other apostrophes are kept.
                if (i + 1 < input.Length && (input[i + 1] == 's' || input[i + 1] == 'S') &&
                    (i + 2 >= input.Length || !char.IsLetter(input[i + 2])) && sb.Length > 0)
                {
                    i++;
                    continue;
                }
                sb.Append('\'');
            }
            else if (char.IsLetterOrDigit(c) || c == '-' || c == '&' || c == '_') sb.Append(c);
            // other symbols are ignored
        }
        Flush();
        return tokens;
    }

    // ---------------------------------------------------------------- top level

    /// <summary>
    /// Parses a line of input. Spelling correction is only used when the sentence doesn't make sense as typed
    /// (so free text such as SAVE MY GAME or SAY "XYZZY" is never "corrected").
    /// </summary>
    public ParseOutcome Parse(string input)
    {
        var plain = ParseInternal(input, correct: false);
        if (!SpellingCorrection || IsClean(plain)) return plain;
        var corrected = ParseInternal(input, correct: true);
        return corrected.Corrections.Count > 0 && (corrected.Success || !plain.Success) ? corrected : plain;
    }

    private static bool IsClean(ParseOutcome o) =>
        o.Success && o.Commands.All(c => !c.Lenient &&
            (c.Object1 == null || c.Object1.Simple().All(p => p.UnknownWords.Count == 0)) &&
            (c.Object2 == null || c.Object2.Simple().All(p => p.UnknownWords.Count == 0)));

    private bool correctSpelling;

    private ParseOutcome ParseInternal(string input, bool correct)
    {
        correctSpelling = correct;
        var outcome = new ParseOutcome();
        var tokens = Tokenize(input);

        // Split into sentences on . ; ! ? and "then".
        var sentences = new List<List<Token>> { new() };
        foreach (var t in tokens)
        {
            if (t.Punctuation && t.Text is "." or ";" or "!" or "?") { sentences.Add(new()); continue; }
            if (!t.Quoted && Lexicon.Is(t.Text, WordKind.SentenceBreak) && !Lexicon.Is(t.Text, WordKind.Noun | WordKind.Verb)) { sentences.Add(new()); continue; }
            sentences[^1].Add(t);
        }

        foreach (var sentence in sentences.Where(s => s.Count > 0))
        {
            foreach (var clause in SplitClauses(Prepare(sentence, outcome)))
            {
                var cmd = ParseClause(clause, outcome);
                if (cmd == null) return outcome;
                cmd.Input = string.Join(' ', clause.Select(t => t.Quoted ? $"\"{t.Text}\"" : t.Text));
                outcome.Commands.Add(cmd);
            }
        }
        return outcome;
    }

    /// <summary>Idioms, replacements, noise removal, normalisation and spelling correction.</summary>
    private List<Token> Prepare(List<Token> sentence, ParseOutcome outcome)
    {
        var joined = string.Join(' ', sentence.Where(t => !t.Punctuation).Select(t => t.Text));
        if (BuiltInLexicon.Idioms.TryGetValue(joined, out var idiom) && !Lexicon.Replacements.ContainsKey(joined))
            sentence = Tokenize(idiom);
        if (Lexicon.Replacements.TryGetValue(joined, out var whole))
            sentence = Tokenize(whole);

        // Word-level replacements
        var replaced = new List<Token>();
        foreach (var t in sentence)
        {
            if (!t.Quoted && !t.Punctuation && Lexicon.Replacements.TryGetValue(t.Text, out var rep))
                replaced.AddRange(Tokenize(rep));
            else replaced.Add(t);
        }
        sentence = replaced;

        // Leading filler ("I want to", "please", "try to")
        bool changed = true;
        while (changed && sentence.Count > 1)
        {
            changed = false;
            foreach (var noise in BuiltInLexicon.LeadingNoise)
            {
                var nw = noise.Split(' ');
                if (sentence.Count > nw.Length && sentence.Take(nw.Length).Select(t => t.Text).SequenceEqual(nw) &&
                    !(nw.Length == 1 && Lexicon.Is(nw[0], WordKind.Verb)))
                {
                    sentence = sentence.Skip(nw.Length).ToList();
                    changed = true;
                    break;
                }
            }
        }

        foreach (var t in sentence)
        {
            if (t.Quoted || t.Punctuation) continue;
            var norm = Lexicon.Normalize(t.Text);
            if (!Lexicon.IsKnown(norm) && correctSpelling && !LooksPlural(norm))
            {
                var fix = Lexicon.SuggestCorrection(t.Text);
                if (fix != null)
                {
                    outcome.Corrections.Add((t.Text, fix));
                    norm = fix;
                }
            }
            t.Text = norm;
        }
        return sentence;
    }

    private bool LooksPlural(string w) =>
        (w.EndsWith("ies") && Lexicon.Is(w[..^3] + "y", WordKind.Noun)) ||
        (w.EndsWith("es") && Lexicon.Is(w[..^2], WordKind.Noun)) ||
        (w.EndsWith('s') && Lexicon.Is(w[..^1], WordKind.Noun));

    /// <summary>
    /// Splits "take the lamp and go north" / "open door, go in" into separate clauses when the word after
    /// "and" / "," starts a new verb phrase. Leaves "take lamp and key" alone. Handles "Robot, go north".
    /// </summary>
    private List<List<Token>> SplitClauses(List<Token> tokens)
    {
        var clauses = new List<List<Token>> { new() };
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            bool separator = t.Punctuation && t.Text == "," || (!t.Quoted && Lexicon.Is(t.Text, WordKind.Conjunction));
            if (separator && i + 1 < tokens.Count && clauses[^1].Count > 0 && StartsClause(tokens, i + 1))
            {
                // "Robot, go north": the first part is an addressee, keep it attached.
                bool isAddress = t.Text == "," && clauses.Count == 1 && !StartsClause(clauses[0], 0) && !IsDirectionOnly(clauses[0]);
                if (isAddress) { clauses[^1].Add(t); continue; }
                clauses.Add(new());
                continue;
            }
            clauses[^1].Add(t);
        }
        clauses.RemoveAll(c => c.Count == 0);
        return clauses;
    }

    private bool IsDirectionOnly(List<Token> clause) => clause.Count == 1 && Lexicon.Direction(clause[0].Text) != null;

    private bool StartsClause(List<Token> tokens, int start)
    {
        if (start >= tokens.Count || tokens[start].Quoted) return false;
        var w = tokens[start].Text;
        var kind = Lexicon.KindOf(w);
        // Skip adverbs: "and quietly open the door"
        if ((kind & WordKind.Adverb) != 0 && (kind & (WordKind.Noun | WordKind.Adjective | WordKind.Verb)) == 0)
            return StartsClause(tokens, start + 1);
        if ((kind & WordKind.Direction) != 0 && (kind & (WordKind.Noun | WordKind.Adjective)) == 0)
        {
            // "take lamp and north" -> new clause if the direction is the last word or followed by another separator
            return start + 1 >= tokens.Count || tokens[start + 1].Punctuation || Lexicon.Is(tokens[start + 1].Text, WordKind.Conjunction);
        }
        if ((kind & WordKind.Verb) == 0) return false;
        // A word that is also a noun/adjective in this game ("light") only starts a clause when followed by
        // something that can't continue a noun list.
        if ((kind & (WordKind.Noun | WordKind.Adjective)) != 0)
        {
            if (start + 1 >= tokens.Count) return false;
            var next = Lexicon.KindOf(tokens[start + 1].Text);
            return (next & (WordKind.Article | WordKind.Pronoun | WordKind.Quantifier | WordKind.Preposition)) != 0;
        }
        return true;
    }

    // ---------------------------------------------------------------- clause parsing

    private ParsedCommand? ParseClause(List<Token> clause, ParseOutcome outcome)
    {
        var cmd = new ParsedCommand();

        // Addressee: "robot, go north"
        int comma = clause.FindIndex(t => t.Punctuation && t.Text == ",");
        if (comma > 0 && comma < clause.Count - 1)
        {
            var addr = NounPhrase.Parse(clause.Take(comma).Select(t => t.Text).ToList(), Lexicon);
            if (addr != null && StartsClauseOrDirection(clause, comma + 1))
            {
                cmd.Addressee = addr;
                clause = clause.Skip(comma + 1).ToList();
            }
        }

        // Pull out quoted text first: say "open sesame"
        string? quoted = null;
        var words = new List<string>();
        foreach (var t in clause)
        {
            if (t.Quoted) { quoted = quoted == null ? t.Text : quoted + " " + t.Text; words.Add("\u0001"); }
            else if (!t.Punctuation) words.Add(t.Text);
            else if (t.Text == ",") words.Add(",");
        }

        // Adverbs anywhere in the sentence.
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var kind = Lexicon.KindOf(w);
            if ((kind & WordKind.Adverb) == 0) continue;
            bool other = (kind & (WordKind.Noun | WordKind.Verb | WordKind.Direction | WordKind.Preposition)) != 0;
            bool adjective = (kind & WordKind.Adjective) != 0;
            if (other && !(i > 0 && (kind & WordKind.Verb) != 0 && (kind & WordKind.Noun) == 0 && i == words.Count - 1)) continue;
            // "hard hat": adjective before a noun stays an adjective
            if (adjective && i + 1 < words.Count && Lexicon.Is(words[i + 1], WordKind.Noun | WordKind.Adjective)) continue;
            cmd.Adverbs.Add(w);
            words.RemoveAt(i);
            i--;
        }
        // Unknown words ending in -ly are treated as adverbs ("glumly").
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].Length > 4 && words[i].EndsWith("ly") && !Lexicon.IsKnown(words[i]))
            {
                cmd.Adverbs.Add(words[i]);
                words.RemoveAt(i);
                i--;
            }
        }
        words.RemoveAll(w => Lexicon.KindOf(w) == WordKind.Ignored);
        cmd.Words.AddRange(words.Where(w => w != "\u0001" && w != ","));
        if (quoted != null) cmd.Words.AddRange(quoted.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (words.Count == 0)
        {
            if (cmd.Adverbs.Count > 0)
            {
                outcome.Error = "What do you want to do " + cmd.Adverbs[0] + "?";
                return null;
            }
            outcome.Error = "I beg your pardon?";
            return null;
        }

        // Bare direction: "north", "go quietly north" handled by grammar.
        if (words.Count == 1 && Lexicon.Direction(words[0]) is { } dir && !Lexicon.Is(words[0], WordKind.Verb))
        {
            cmd.VerbId = cmd.ActionId = "go";
            cmd.VerbWords = words[0];
            cmd.Direction = dir;
            return cmd;
        }

        ParsedCommand? best = null;
        string? error = null;
        foreach (var (verb, length) in Lexicon.MatchVerbPhrases(words))
        {
            var rest = words.Skip(length).ToList();
            var lines = verb.Lines.Count > 0 ? verb.Lines : DefaultLines(verb);
            foreach (var line in lines)
            {
                var state = new MatchState();
                if (!Match(line.Tokens, 0, rest, 0, state)) continue;
                var candidate = Build(cmd, verb, string.Join(' ', words.Take(length)), line, state, quoted);
                if (candidate == null) continue;
                // Legacy games define direction words as verbs ("north" as verb 1): keep the direction too.
                if (candidate.Direction == null && length == 1 && Lexicon.Direction(words[0]) is { } verbDir) candidate.Direction = verbDir;
                return candidate;
            }
            error ??= DescribeFailure(verb, words.Take(length), rest);
            best ??= Lenient(cmd, verb, string.Join(' ', words.Take(length)), rest, quoted);
        }

        if (best != null)
        {
            best.Lenient = true;
            best.GrammarError = error;
            return best;
        }

        // No verb. A noun phrase alone ("lamp") or unknown words.
        var unknown = words.FirstOrDefault(w => w != "\u0001" && w != "," && !Lexicon.IsKnown(w));
        if (unknown != null)
        {
            outcome.Error = "UNKNOWN";
            outcome.UnknownWord = unknown;
            return null;
        }
        var np = NounPhrase.Parse(words.Where(w => w != "\u0001").ToList(), Lexicon);
        if (np != null)
        {
            cmd.Object1 = np;
            cmd.Lenient = true;
            cmd.GrammarError = $"What do you want to do with {np.Text}?";
            return cmd;
        }
        // Lenient without verb: keep words for legacy triggers (e.g. PAWS nouns used as verbs).
        cmd.Lenient = true;
        cmd.GrammarError = "That's not a verb I recognise.";
        return cmd;
    }

    private bool StartsClauseOrDirection(List<Token> clause, int start) =>
        StartsClause(clause, start) || (start < clause.Count && Lexicon.Direction(clause[start].Text) != null);

    private static readonly Dictionary<string, List<GrammarLine>> defaultLinesCache = new();

    private static List<GrammarLine> DefaultLines(VerbSpec verb)
    {
        lock (defaultLinesCache)
        {
            if (!defaultLinesCache.TryGetValue(verb.Id, out var lines))
            {
                lines = new List<GrammarLine>
                {
                    GrammarLine.Parse("*", verb.Id, verb.Words),
                    GrammarLine.Parse("* {noun}", verb.Id, verb.Words),
                    GrammarLine.Parse("* {noun} with|on|in|to|at|from|into|onto {noun2}", verb.Id, verb.Words),
                    GrammarLine.Parse("* {direction}", verb.Id, verb.Words),
                };
                defaultLinesCache[verb.Id] = lines;
            }
            return lines;
        }
    }

    private string DescribeFailure(VerbSpec verb, IEnumerable<string> verbWords, List<string> rest)
    {
        var v = string.Join(' ', verbWords);
        if (rest.Count == 0) return $"What do you want to {v}?";
        var unknown = rest.FirstOrDefault(w => w != "\u0001" && w != "," && !Lexicon.IsKnown(w));
        if (unknown != null) return $"I don't know the word \"{unknown}\".";
        return $"I only understood you as far as wanting to {v}.";
    }

    // ---------------------------------------------------------------- grammar matching

    private sealed class MatchState
    {
        public List<string>? Obj1, Obj2;
        public SlotKind? Slot1, Slot2;
        public string? Direction;
        public int? Number;
        public string? Text;
        public List<string> Preps = new();
    }

    private bool Match(List<GrammarToken> tokens, int ti, List<string> words, int wi, MatchState st)
    {
        if (ti == tokens.Count) return wi == words.Count;
        var tok = tokens[ti];

        if (!tok.IsSlot)
        {
            foreach (var lit in tok.Literals)
            {
                if (wi + lit.Length > words.Count) continue;
                bool ok = true;
                for (int k = 0; k < lit.Length && ok; k++) ok = Lexicon.Equivalent(words[wi + k], lit[k]);
                if (!ok) continue;
                st.Preps.Add(string.Join(' ', lit));
                if (Match(tokens, ti + 1, words, wi + lit.Length, st)) return true;
                st.Preps.RemoveAt(st.Preps.Count - 1);
            }
            return false;
        }

        if (wi >= words.Count) return false;

        switch (tok.Slot)
        {
            case SlotKind.Direction:
            {
                var d = Lexicon.Direction(words[wi]);
                if (d == null) return false;
                st.Direction = d;
                if (Match(tokens, ti + 1, words, wi + 1, st)) return true;
                st.Direction = null;
                return false;
            }
            case SlotKind.Number:
            {
                int n;
                if (Lexicon.IsNumeric(words[wi])) n = int.Parse(words[wi]);
                else if (!BuiltInLexicon.NumberWords.TryGetValue(words[wi], out n)) return false;
                st.Number = n;
                if (Match(tokens, ti + 1, words, wi + 1, st)) return true;
                st.Number = null;
                return false;
            }
        }

        // Variable-length slots: objects, topic, text.
        bool isText = tok.Slot is SlotKind.Topic or SlotKind.Text;
        bool last = ti == tokens.Count - 1;
        IEnumerable<int> ends = last
            ? new[] { words.Count }
            : isText ? Enumerable.Range(wi + 1, words.Count - wi).Reverse() : Enumerable.Range(wi + 1, words.Count - wi);

        foreach (var end in ends)
        {
            var span = words.GetRange(wi, end - wi);
            if (isText)
            {
                st.Text = string.Join(' ', span);
                if (Match(tokens, ti + 1, words, end, st)) return true;
                st.Text = null;
                continue;
            }
            if (span.Contains("\u0001") || !IsPlausibleObject(span)) continue;
            if (tok.Second) { st.Obj2 = span; st.Slot2 = tok.Slot; }
            else { st.Obj1 = span; st.Slot1 = tok.Slot; }
            if (Match(tokens, ti + 1, words, end, st)) return true;
            if (tok.Second) { st.Obj2 = null; st.Slot2 = null; } else { st.Obj1 = null; st.Slot1 = null; }
        }
        return false;
    }

    private bool IsPlausibleObject(List<string> span)
    {
        if (span.Count == 0) return false;
        // A preposition-only word at the edges means the split is wrong.
        foreach (var edge in new[] { span[0], span[^1] })
        {
            var k = Lexicon.KindOf(edge);
            if ((k & WordKind.Preposition) != 0 && (k & (WordKind.Noun | WordKind.Adjective)) == 0) return false;
            if (edge == ",") return false;
        }
        return NounPhrase.Parse(span, Lexicon) != null;
    }

    private ParsedCommand? Build(ParsedCommand template, VerbSpec verb, string verbWords, GrammarLine line, MatchState st, string? quoted)
    {
        var cmd = new ParsedCommand
        {
            VerbId = verb.Id,
            ActionId = line.ActionId,
            VerbWords = verbWords,
            Grammar = line,
            Direction = st.Direction,
            Number = st.Number,
            Addressee = template.Addressee,
            IsMeta = (Lexicon.FindVerb(line.ActionId) ?? verb).IsMeta,
        };
        cmd.Adverbs.AddRange(template.Adverbs);
        cmd.Words.AddRange(template.Words);
        cmd.Prepositions.AddRange(st.Preps.Where(p => Lexicon.Is(p.Split(' ')[0], WordKind.Preposition)));

        if (st.Text != null) cmd.Text = st.Text.Replace("\u0001", quoted ?? "").Trim();
        else if (quoted != null) cmd.Text = quoted;

        if (st.Obj1 != null)
        {
            cmd.Object1 = NounPhrase.Parse(st.Obj1, Lexicon);
            cmd.Object1Slot = st.Slot1;
            if (cmd.Object1 == null) return null;
            if (cmd.Object1.IsCompound && st.Slot1 is not (SlotKind.Multi or SlotKind.MultiHeld)) return null;
        }
        if (st.Obj2 != null)
        {
            cmd.Object2 = NounPhrase.Parse(st.Obj2, Lexicon);
            cmd.Object2Slot = st.Slot2;
            if (cmd.Object2 == null) return null;
        }
        return cmd;
    }

    /// <summary>PAWS-style loose parse: verb, first noun phrase, preposition, second noun phrase.</summary>
    private ParsedCommand Lenient(ParsedCommand template, VerbSpec verb, string verbWords, List<string> rest, string? quoted)
    {
        var cmd = new ParsedCommand
        {
            VerbId = verb.Id,
            ActionId = verb.Id,
            VerbWords = verbWords,
            Addressee = template.Addressee,
            IsMeta = verb.IsMeta,
            Text = quoted,
        };
        cmd.Adverbs.AddRange(template.Adverbs);
        cmd.Words.AddRange(template.Words);

        var current = new List<string>();
        var phrases = new List<List<string>>();
        foreach (var w in rest)
        {
            if (w == "\u0001") continue;
            var kind = Lexicon.KindOf(w);
            if (Lexicon.Direction(w) is { } d && (kind & (WordKind.Noun | WordKind.Adjective)) == 0 && cmd.Direction == null &&
                (kind & WordKind.Preposition) == 0)
            {
                cmd.Direction = d;
                continue;
            }
            if ((kind & WordKind.Preposition) != 0 && (kind & (WordKind.Noun | WordKind.Adjective)) == 0)
            {
                cmd.Prepositions.Add(w);
                if (current.Count > 0) { phrases.Add(current); current = new(); }
                continue;
            }
            if ((kind & WordKind.Number) != 0 && cmd.Number == null)
                cmd.Number = Lexicon.IsNumeric(w) ? int.Parse(w) : BuiltInLexicon.NumberWords.GetValueOrDefault(w);
            current.Add(w);
        }
        if (current.Count > 0) phrases.Add(current);
        if (phrases.Count > 0) cmd.Object1 = NounPhrase.Parse(phrases[0], Lexicon);
        if (phrases.Count > 1) cmd.Object2 = NounPhrase.Parse(phrases[1], Lexicon);
        cmd.Object1Slot = SlotKind.Noun;
        cmd.Object2Slot = SlotKind.Noun;
        if (cmd.Text == null && rest.Count > 0) cmd.Text = string.Join(' ', rest.Where(w => w != "\u0001"));
        return cmd;
    }
}
