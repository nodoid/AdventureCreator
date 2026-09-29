namespace AdventureCreator.Core.Model;

/// <summary>
/// Author-defined vocabulary. The parser starts from a large built-in English lexicon
/// (see <see cref="Parsing.BuiltInLexicon"/>) and merges these definitions on top, so authors can
/// add completely new commands, extra synonyms for built-in verbs, adverbs, prepositions and so on.
/// </summary>
public sealed class Vocabulary
{
    /// <summary>New commands, or extensions (extra words / grammar) for built-in verbs with the same id.</summary>
    public List<VerbDefinition> Verbs { get; set; } = new();
    /// <summary>Extra nouns that are not attached to any item (scenery words, words used only by triggers).</summary>
    public List<string> Nouns { get; set; } = new();
    public List<string> Adverbs { get; set; } = new();
    public List<string> Adjectives { get; set; } = new();
    public List<string> Prepositions { get; set; } = new();
    /// <summary>Words the parser should silently ignore ("please", "kindly").</summary>
    public List<string> IgnoredWords { get; set; } = new();
    /// <summary>Whole-word substitutions applied before parsing, e.g. "xyzzy" -> "say xyzzy".</summary>
    public Dictionary<string, string> Replacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Custom directions (e.g. "fore", "aft") mapped to a canonical exit direction.</summary>
    public Dictionary<string, string> Directions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Legacy word numbers from imported PAWS/Quill/GAC games: word -> number.
    /// Words with the same number are synonyms. Used only by importers for fidelity.
    /// </summary>
    public Dictionary<string, int> LegacyWordNumbers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A command understood by the parser.
/// <para>Grammar patterns are sequences of literal words and slots:</para>
/// <list type="bullet">
/// <item><c>{noun}</c> one visible object, <c>{held}</c> one carried object,</item>
/// <item><c>{multi}</c> one or more objects ("all", "lamp and key"), <c>{multiheld}</c> several carried objects,</item>
/// <item><c>{noun2}</c>/<c>{held2}</c> the second (indirect) object, <c>{direction}</c>, <c>{number}</c>,</item>
/// <item><c>{topic}</c>/<c>{text}</c> the rest of the input as free text, <c>{person}</c> a character.</item>
/// </list>
/// Literal alternatives are written with a bar: <c>put {multiheld} in|into|inside {noun2}</c>.
/// The first word(s) of a pattern are the verb and must be one of <see cref="Words"/>;
/// in patterns the verb is written as <c>*</c>, e.g. <c>* {noun} with {held2}</c>.
/// </summary>
public sealed class VerbDefinition
{
    /// <summary>Canonical id used by triggers (e.g. "polish"). Reusing a built-in id extends that verb.</summary>
    public string Id { get; set; } = "";
    /// <summary>Words or phrases that invoke the verb: "polish", "shine", "buff up".</summary>
    public List<string> Words { get; set; } = new();
    /// <summary>Grammar lines. Empty means the verb takes an optional single object: "* " and "* {noun}".</summary>
    public List<string> Grammar { get; set; } = new();
    /// <summary>Printed when no trigger handles the command. May contain {noun1}, {noun2}, {adverb} etc.</summary>
    public string DefaultResponse { get; set; } = "";
    /// <summary>Actions performed when no trigger handles the command (after <see cref="DefaultResponse"/>).</summary>
    public List<GameAction> DefaultActions { get; set; } = new();
    /// <summary>Meta commands (save, score…) do not consume a turn.</summary>
    public bool IsMeta { get; set; }
    public string Help { get; set; } = "";
    public override string ToString() => $"{Id}: {string.Join(", ", Words)}";
}
