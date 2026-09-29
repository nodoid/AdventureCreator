using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Parsing;

[Flags]
public enum WordKind
{
    None = 0,
    Verb = 1 << 0,
    Noun = 1 << 1,
    Adjective = 1 << 2,
    Adverb = 1 << 3,
    Preposition = 1 << 4,
    Pronoun = 1 << 5,
    Article = 1 << 6,
    Conjunction = 1 << 7,
    Quantifier = 1 << 8,
    Exception = 1 << 9,
    Direction = 1 << 10,
    Number = 1 << 11,
    Ignored = 1 << 12,
    SentenceBreak = 1 << 13,
    Self = 1 << 14,
    Ordinal = 1 << 15,
}

public enum SlotKind { Noun, Held, Multi, MultiHeld, Person, Direction, Number, Topic, Text }

/// <summary>One element of a compiled grammar line.</summary>
public sealed class GrammarToken
{
    public bool IsSlot { get; init; }
    public SlotKind Slot { get; init; }
    /// <summary>Slot fills the second (indirect) object.</summary>
    public bool Second { get; init; }
    /// <summary>Literal alternatives; each may contain several words ("out of").</summary>
    public string[][] Literals { get; init; } = Array.Empty<string[]>();

    public bool IsObjectSlot => IsSlot && Slot is SlotKind.Noun or SlotKind.Held or SlotKind.Multi or SlotKind.MultiHeld or SlotKind.Person;
    public override string ToString() => IsSlot ? $"{{{Slot}{(Second ? "2" : "")}}}" : string.Join("|", Literals.Select(l => string.Join(' ', l)));
}

public sealed class GrammarLine
{
    public string Source { get; init; } = "";
    public List<GrammarToken> Tokens { get; init; } = new();
    /// <summary>Action id this line produces (the verb id unless redirected with =&gt;).</summary>
    public string ActionId { get; init; } = "";

    public static GrammarLine Parse(string source, string verbId, IReadOnlyCollection<string> verbWords)
    {
        var text = source.Trim();
        string action = verbId;
        int arrow = text.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            action = text[(arrow + 2)..].Trim().ToLowerInvariant();
            text = text[..arrow].Trim();
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[0] == "*") parts.RemoveAt(0);
        else if (parts.Count > 0 && verbWords.Contains(parts[0].ToLowerInvariant())) parts.RemoveAt(0);

        var tokens = new List<GrammarToken>();
        foreach (var raw in parts)
        {
            var p = raw.ToLowerInvariant();
            if (p.StartsWith('{') && p.EndsWith('}'))
            {
                var name = p[1..^1];
                bool second = name.EndsWith('2');
                if (second) name = name[..^1];
                var slot = name switch
                {
                    "noun" or "object" or "thing" => SlotKind.Noun,
                    "held" or "carried" => SlotKind.Held,
                    "multi" or "all" => SlotKind.Multi,
                    "multiheld" => SlotKind.MultiHeld,
                    "person" or "creature" or "character" => SlotKind.Person,
                    "direction" or "dir" => SlotKind.Direction,
                    "number" or "num" => SlotKind.Number,
                    "topic" => SlotKind.Topic,
                    "text" or "string" => SlotKind.Text,
                    _ => throw new FormatException($"Unknown grammar slot {{{name}}} in \"{source}\"."),
                };
                tokens.Add(new GrammarToken { IsSlot = true, Slot = slot, Second = second });
            }
            else if (tokens.Count > 0 && !tokens[^1].IsSlot && !p.Contains('|'))
            {
                // Consecutive literal words form a multi-word literal ("out of").
                var prev = tokens[^1];
                tokens[^1] = new GrammarToken { Literals = prev.Literals.Select(l => l.Append(p).ToArray()).ToArray() };
            }
            else
            {
                tokens.Add(new GrammarToken
                {
                    Literals = p.Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(alt => alt.Split('_', StringSplitOptions.RemoveEmptyEntries)).ToArray(),
                });
            }
        }

        // "out of" in a bar list is written with the words together; split alternatives containing spaces were
        // already handled above. Mark the second object slot automatically if two object slots are unmarked.
        var objectSlots = tokens.Where(t => t.IsObjectSlot).ToList();
        if (objectSlots.Count == 2 && !objectSlots[0].Second && !objectSlots[1].Second)
        {
            int idx = tokens.IndexOf(objectSlots[1]);
            tokens[idx] = new GrammarToken { IsSlot = true, Slot = objectSlots[1].Slot, Second = true };
        }
        return new GrammarLine { Source = source, Tokens = tokens, ActionId = action };
    }

    public override string ToString() => Source;
}

/// <summary>A verb known to the parser: its words and grammar lines.</summary>
public sealed class VerbSpec
{
    public string Id { get; init; } = "";
    public List<string> Words { get; } = new();
    public List<GrammarLine> Lines { get; } = new();
    public bool IsMeta { get; set; }
    public bool IsBuiltIn { get; set; }
    /// <summary>Author definition for custom verbs (default response / actions).</summary>
    public VerbDefinition? Definition { get; set; }
    public override string ToString() => Id;
}

/// <summary>
/// All words the parser knows for one adventure: the built-in English lexicon merged with the adventure's
/// vocabulary, item names and trigger words.
/// </summary>
public sealed class Lexicon
{
    private readonly Dictionary<string, WordKind> kinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> directions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VerbSpec> verbsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string[] Words, VerbSpec Verb)> verbPhrases = new();
    private readonly HashSet<string> multiWordNouns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> truncated = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> legacyNumbers;

    public int SignificantLetters { get; }
    public IReadOnlyDictionary<string, VerbSpec> Verbs => verbsById;
    public IReadOnlyDictionary<string, string> DirectionWords => directions;
    public IEnumerable<string> AllWords => kinds.Keys;
    public Dictionary<string, string> Replacements { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Lexicon(Adventure? adventure = null)
    {
        SignificantLetters = adventure?.Settings.SignificantLetters ?? 0;
        legacyNumbers = adventure?.Vocabulary.LegacyWordNumbers ?? new Dictionary<string, int>();

        foreach (var row in BuiltInLexicon.VerbTable)
        {
            var cols = row.Split('|', 3);
            var id = cols[0].Trim();
            bool meta = id.EndsWith('!');
            if (meta) id = id[..^1];
            var words = cols[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var grammar = cols.Length > 2 ? cols[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>();
            AddVerb(id, words, grammar, meta, builtIn: true);
        }

        foreach (var (canonical, words) in BuiltInLexicon.Directions)
            foreach (var w in words) AddDirection(w, canonical);

        Add(BuiltInLexicon.Prepositions, WordKind.Preposition);
        Add(BuiltInLexicon.Adverbs, WordKind.Adverb);
        Add(BuiltInLexicon.Adjectives, WordKind.Adjective);
        Add(BuiltInLexicon.Articles, WordKind.Article);
        Add(BuiltInLexicon.Pronouns, WordKind.Pronoun);
        Add(BuiltInLexicon.Quantifiers, WordKind.Quantifier);
        Add(BuiltInLexicon.Exceptions, WordKind.Exception);
        Add(BuiltInLexicon.Conjunctions, WordKind.Conjunction);
        Add(BuiltInLexicon.SentenceBreaks, WordKind.SentenceBreak);
        Add(BuiltInLexicon.SelfWords, WordKind.Self);
        Add(BuiltInLexicon.Ignored, WordKind.Ignored);
        Add(BuiltInLexicon.NumberWords.Keys, WordKind.Number);
        Add(BuiltInLexicon.Ordinals.Keys, WordKind.Ordinal);

        if (adventure != null) MergeAdventure(adventure);
        BuildTruncationIndex();
    }

    private void MergeAdventure(Adventure adventure)
    {
        var vocab = adventure.Vocabulary;
        foreach (var def in vocab.Verbs)
        {
            if (string.IsNullOrWhiteSpace(def.Id)) continue;
            var spec = AddVerb(def.Id.Trim().ToLowerInvariant(), def.Words, def.Grammar, def.IsMeta, builtIn: false, prependGrammar: true);
            spec.Definition = def;
        }
        foreach (var (word, canonical) in vocab.Directions) AddDirection(word, canonical);
        Add(vocab.Nouns, WordKind.Noun);
        Add(vocab.Adverbs, WordKind.Adverb);
        Add(vocab.Adjectives, WordKind.Adjective);
        Add(vocab.Prepositions, WordKind.Preposition);
        Add(vocab.IgnoredWords, WordKind.Ignored);
        foreach (var (k, v) in vocab.Replacements) Replacements[k] = v;

        foreach (var item in adventure.Items)
        {
            foreach (var n in item.Nouns) AddNoun(n);
            foreach (var a in item.Adjectives) Add(a, WordKind.Adjective);
            if (item.Nouns.Count == 0 && !string.IsNullOrWhiteSpace(item.Name))
            {
                // Derive words from the name: last word noun, the rest adjectives.
                var words = item.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0) AddNoun(words[^1]);
                foreach (var w in words.SkipLast(1)) if (!Is(w, WordKind.Article)) Add(w, WordKind.Adjective);
            }
            foreach (var topic in item.Topics)
                foreach (var k in topic.Keywords) Add(k, WordKind.Noun);
        }

        foreach (var room in adventure.Rooms)
            foreach (var exit in room.Exits)
                if (!directions.ContainsKey(exit.Direction)) AddDirection(exit.Direction, exit.Direction.ToLowerInvariant());

        foreach (var t in adventure.Triggers)
        {
            AddTriggerWord(adventure, t.Noun1);
            AddTriggerWord(adventure, t.Noun2);
            if (IsPatternWord(t.Adverb)) Add(t.Adverb!, WordKind.Adverb);
            if (IsPatternWord(t.Preposition)) Add(t.Preposition!, WordKind.Preposition);
            if (IsPatternWord(t.Verb))
                foreach (var v in t.Verb!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!verbsById.ContainsKey(v) && FindVerbByWord(v) == null && !directions.ContainsKey(v))
                        AddVerb(v.ToLowerInvariant(), new[] { v }, Array.Empty<string>(), false, false);
            foreach (var c in t.Conditions)
            {
                if (c.Type == ConditionType.AdverbUsed && !string.IsNullOrEmpty(c.A)) Add(c.A, WordKind.Adverb);
                if (c.Type == ConditionType.AdjectiveUsed && !string.IsNullOrEmpty(c.A)) Add(c.A, WordKind.Adjective);
                if (c.Type == ConditionType.PrepositionIs && !string.IsNullOrEmpty(c.A)) Add(c.A, WordKind.Preposition);
                if (c.Type is ConditionType.Noun1Is or ConditionType.Noun2Is) AddTriggerWord(adventure, c.A);
            }
        }
    }

    private static bool IsPatternWord(string? w) => !string.IsNullOrWhiteSpace(w) && w != "*" && w != "-";

    private void AddTriggerWord(Adventure adventure, string? word)
    {
        if (!IsPatternWord(word)) return;
        foreach (var w in word!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (adventure.FindItem(w) == null) AddNoun(w);
    }

    private VerbSpec AddVerb(string id, IEnumerable<string> words, IEnumerable<string> grammar, bool meta, bool builtIn, bool prependGrammar = false)
    {
        if (!verbsById.TryGetValue(id, out var spec))
        {
            spec = new VerbSpec { Id = id, IsMeta = meta, IsBuiltIn = builtIn };
            verbsById[id] = spec;
        }
        else if (meta) spec.IsMeta = true;

        foreach (var w in words.Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0))
        {
            if (spec.Words.Contains(w)) continue;
            spec.Words.Add(w);
            var parts = w.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A later definition of the same phrase (author vocabulary) replaces the earlier one.
            verbPhrases.RemoveAll(p => p.Words.SequenceEqual(parts, StringComparer.OrdinalIgnoreCase));
            verbPhrases.Add((parts, spec));
            if (parts.Length == 1) Add(parts[0], WordKind.Verb);
        }
        verbPhrases.Sort((a, b) => b.Words.Length.CompareTo(a.Words.Length));

        var lines = grammar.Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => GrammarLine.Parse(g, id, spec.Words)).ToList();
        if (prependGrammar) spec.Lines.InsertRange(0, lines); else spec.Lines.AddRange(lines);
        return spec;
    }

    private void AddDirection(string word, string canonical)
    {
        word = word.Trim().ToLowerInvariant();
        if (word.Length == 0) return;
        directions[word] = canonical.Trim().ToLowerInvariant();
        Add(word, WordKind.Direction);
    }

    public void AddNoun(string noun)
    {
        noun = noun.Trim().ToLowerInvariant();
        if (noun.Length == 0) return;
        if (noun.Contains(' '))
        {
            multiWordNouns.Add(noun);
            foreach (var w in noun.Split(' ', StringSplitOptions.RemoveEmptyEntries)) Add(w, WordKind.Noun);
        }
        else Add(noun, WordKind.Noun);
    }

    private void Add(IEnumerable<string> words, WordKind kind)
    {
        foreach (var w in words) Add(w, kind);
    }

    private void Add(string word, WordKind kind)
    {
        word = word.Trim().ToLowerInvariant();
        if (word.Length == 0) return;
        kinds[word] = kinds.TryGetValue(word, out var k) ? k | kind : kind;
    }

    private void BuildTruncationIndex()
    {
        if (SignificantLetters <= 0) return;
        foreach (var w in kinds.Keys)
        {
            if (w.Length < SignificantLetters) continue;
            var key = w[..SignificantLetters];
            truncated.TryAdd(key, w);
        }
    }

    public WordKind KindOf(string word) => kinds.TryGetValue(word, out var k) ? k : IsNumeric(word) ? WordKind.Number : WordKind.None;
    public bool Is(string word, WordKind kind) => (KindOf(word) & kind) != 0;
    public bool IsKnown(string word) => KindOf(word) != WordKind.None;
    public bool IsMultiWordNoun(string phrase) => multiWordNouns.Contains(phrase);
    public IEnumerable<string> MultiWordNouns => multiWordNouns;

    public static bool IsNumeric(string word) => word.Length > 0 && word.All(char.IsDigit);

    public string? Direction(string word) => directions.TryGetValue(word, out var d) ? d : null;

    public VerbSpec? FindVerb(string id) => verbsById.TryGetValue(id, out var v) ? v : null;

    public VerbSpec? FindVerbByWord(string word)
    {
        foreach (var (words, spec) in verbPhrases)
            if (words.Length == 1 && string.Equals(words[0], word, StringComparison.OrdinalIgnoreCase)) return spec;
        return null;
    }

    /// <summary>Longest verb phrase at the start of <paramref name="words"/>.</summary>
    public IEnumerable<(VerbSpec Verb, int Length)> MatchVerbPhrases(IReadOnlyList<string> words, int start = 0)
    {
        foreach (var (phrase, spec) in verbPhrases)
        {
            if (start + phrase.Length > words.Count) continue;
            bool ok = true;
            for (int i = 0; i < phrase.Length && ok; i++)
                ok = string.Equals(words[start + i], phrase[i], StringComparison.OrdinalIgnoreCase);
            if (ok) yield return (spec, phrase.Length);
        }
    }

    /// <summary>
    /// Maps an input word onto the lexicon: exact match, then significant-letter truncation (PAWS/Quill style).
    /// Returns the word unchanged if nothing matches.
    /// </summary>
    public string Normalize(string word)
    {
        if (kinds.ContainsKey(word) || IsNumeric(word)) return word;
        if (SignificantLetters > 0 && word.Length >= SignificantLetters && truncated.TryGetValue(word[..SignificantLetters], out var full))
            return full;
        return word;
    }

    /// <summary>True if the two words are synonyms through legacy word numbers, or equal.</summary>
    public bool Equivalent(string? a, string? b)
    {
        if (a is null || b is null) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (SignificantLetters > 0 && a.Length >= SignificantLetters && b.Length >= SignificantLetters &&
            string.Equals(a[..SignificantLetters], b[..SignificantLetters], StringComparison.OrdinalIgnoreCase))
            return true;
        return legacyNumbers.Count > 0 && legacyNumbers.TryGetValue(a, out var na) && legacyNumbers.TryGetValue(b, out var nb) && na == nb;
    }

    /// <summary>Closest known word for spelling correction (Damerau–Levenshtein), or null.</summary>
    public string? SuggestCorrection(string word)
    {
        if (word.Length < 4 || IsNumeric(word)) return null;
        int maxDistance = word.Length >= 7 ? 2 : 1;
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (var candidate in kinds.Keys)
        {
            if (Math.Abs(candidate.Length - word.Length) > maxDistance || candidate.Length < 3) continue;
            if ((kinds[candidate] & (WordKind.Ignored | WordKind.Article)) != 0) continue;
            int d = Distance(word, candidate, maxDistance);
            if (d < bestDistance || (d == bestDistance && best != null && string.CompareOrdinal(candidate, best) < 0))
            {
                bestDistance = d;
                best = candidate;
            }
        }
        return bestDistance <= maxDistance ? best : null;
    }

    public static int Distance(string a, string b, int cap = int.MaxValue)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int rowMin = int.MaxValue;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > cap) return rowMin;
        }
        return d[a.Length, b.Length];
    }
}
