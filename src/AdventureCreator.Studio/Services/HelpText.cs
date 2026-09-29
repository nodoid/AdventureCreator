using System.Text;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Parsing;

namespace AdventureCreator.Studio.Services;

/// <summary>Reference documentation generated from the live lexicon and enums, so it never goes stale.</summary>
public static class HelpText
{
    public static string ParserReference(Adventure adventure)
    {
        var sb = new StringBuilder();
        sb.AppendLine("HOW THE PARSER WORKS");
        sb.AppendLine("• Several commands per line: separate with . ; THEN, or AND/comma before a new verb (\"take lamp and go north\").");
        sb.AppendLine("• Adverbs can go anywhere (\"quietly open the door\", \"open the door quietly\"). Unknown words ending in -ly are treated as adverbs.");
        sb.AppendLine("• Adjectives pick between similar items (\"take the red key\"); if still ambiguous the player is asked which one they mean.");
        sb.AppendLine("• ALL, EVERYTHING, BOTH, EXCEPT/BUT, plurals (\"take coins\"), ordinals (\"the second key\") and pronouns IT/THEM/HIM/HER.");
        sb.AppendLine("• Characters: \"robot, go north\" or \"tell the robot to go north\" (triggers with Subject = the character handle these).");
        sb.AppendLine("• Quoted text: say \"open sesame\". Numbers: turn dial to 7.");
        sb.AppendLine("• Filler is ignored: \"I want to…\", \"please\", \"could you…\". Idioms: \"where am I\", \"what am I carrying\".");
        sb.AppendLine("• Spelling mistakes are corrected against the vocabulary; OOPS <word> fixes the last unknown word; AGAIN / G repeats.");
        sb.AppendLine("• Games may set 'Significant letters' (PAWS/Quill style word truncation).");
        sb.AppendLine();
        sb.AppendLine("GRAMMAR LINES (for your own commands)");
        sb.AppendLine("  * {noun}                       — the verb followed by one visible object");
        sb.AppendLine("  * {held} with {noun2}          — first object must be carried (taken automatically if possible)");
        sb.AppendLine("  * {multi} / {multiheld}        — several objects, ALL, EXCEPT");
        sb.AppendLine("  * {person} about {topic}       — a character and free text");
        sb.AppendLine("  * {direction} / * {number} / * {text}");
        sb.AppendLine("  put {multiheld} in|into {noun2} => insert   — alternatives with |, redirect to another action with =>");
        sb.AppendLine();
        sb.AppendLine("VERBS");
        var lex = new Lexicon(adventure);
        foreach (var v in lex.Verbs.Values.Where(v => v.Words.Count > 0).OrderBy(v => v.Id))
        {
            sb.AppendLine($"{v.Id}{(v.IsMeta ? " (meta)" : "")}{(v.IsBuiltIn ? "" : " (custom)")}: {string.Join(", ", v.Words)}");
            foreach (var line in v.Lines) sb.AppendLine($"     {line.Source}");
        }
        sb.AppendLine();
        sb.AppendLine("DIRECTIONS: " + string.Join(", ", lex.DirectionWords.Select(d => d.Key == d.Value ? d.Key : $"{d.Key}→{d.Value}")));
        sb.AppendLine();
        sb.AppendLine("ADVERBS: " + string.Join(", ", BuiltInLexicon.Adverbs.Concat(adventure.Vocabulary.Adverbs).Distinct().OrderBy(x => x)));
        sb.AppendLine();
        sb.AppendLine("PREPOSITIONS: " + string.Join(", ", BuiltInLexicon.Prepositions.Concat(adventure.Vocabulary.Prepositions).Distinct()));
        return sb.ToString();
    }

    public static string TriggerReference()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TRIGGERS");
        sb.AppendLine("A trigger runs its actions when its event happens, its pattern matches and all its conditions are true.");
        sb.AppendLine("Patterns: Verb (command id or word; alternatives with |), Noun1/Noun2 (item id or word), Adverb, Preposition, Room.");
        sb.AppendLine("  *  matches anything    -  requires the slot to be empty");
        sb.AppendLine("BeforeCommand triggers replace the built-in behaviour unless an action is Continue (or 'Stops command' is off).");
        sb.AppendLine();
        sb.AppendLine("EVENTS");
        foreach (var e in Enum.GetNames<TriggerEvent>()) sb.AppendLine("  " + e);
        sb.AppendLine();
        sb.AppendLine("CONDITIONS (tick 'not' to negate)");
        foreach (var c in Enum.GetValues<ConditionType>())
            sb.AppendLine($"  {c,-22} {References.Hint(new Condition { Type = c })}");
        sb.AppendLine();
        sb.AppendLine("ACTIONS");
        foreach (var a in Enum.GetValues<ActionType>())
            sb.AppendLine($"  {a,-20} {References.Hint(new GameAction { Type = a })}");
        sb.AppendLine();
        sb.AppendLine("TEXT PLACEHOLDERS");
        sb.AppendLine("  {noun1} {the noun1} {The noun1} {a noun1} {noun2} {verb} {adverb} {direction} {text} {number}");
        sb.AppendLine("  {score} {max} {turns} {room} {title} {actor} {carried} {var:name} {item:id} {room:id} {newline}");
        sb.AppendLine();
        sb.AppendLine("SPECIAL VALUES");
        sb.AppendLine("  $noun1 / $noun2 — the item the player named;  @here — the current room;  @carried / @worn — the player");
        sb.AppendLine("  Variables @score @turns @room @carried can be tested and (except @carried) changed.");
        return sb.ToString();
    }
}
