namespace AdventureCreator.Core.Parsing;

/// <summary>
/// The built-in English vocabulary and grammar. Each verb entry is
/// <c>id | word, word, multi word | grammar line ; grammar line</c>, with an optional <c>!</c> after the id for
/// meta (out-of-world) commands. Grammar lines use the syntax documented on
/// <see cref="Model.VerbDefinition"/>; <c>=&gt; action</c> redirects a line to another action id.
/// </summary>
public static class BuiltInLexicon
{
    public static readonly string[] VerbTable =
    {
        "look | look, l | * ; * around ; * {direction}",
        "examine | examine, x, inspect, check, look at, l at, describe, study, observe, watch, view, read about | * {noun}",
        "search | search, look in, look inside, look through, l in, rummage, rummage through | * ; * {noun} ; * around",
        "inventory | inventory, i, inv, invent | *",
        "go | go, walk, run, head, travel, proceed, crawl, sneak, stroll, march, wander | * {direction} ; * to {noun} => enter ; * through {noun} => enter ; * into|in {noun} => enter ; * {noun} => enter",
        "enter | enter, go in, go into, get in, get into, climb in, climb into, step into, step in | * ; * {noun}",
        "exit | exit, leave, get out, go out, climb out, step out, get off | * ; * {noun} ; * of {noun}",
        "take | take, get, pick up, grab, carry, hold, collect, acquire, snatch, obtain | * {multi} ; * {multi} from|out_of|off|in|on {noun2} ; * up {multi} ; * {held} off => remove ; * off {held} => remove",
        "pick | pick | * up {multi} => take ; * {multi} up => take ; * {noun} => pick",
        "drop | drop, discard, put down, set down, dump, release, let go of | * {multiheld} ; * {multiheld} in|into {noun2} => insert ; * {multiheld} on|onto {noun2} => puton",
        "put | put, place, stick, lay, set, stuff, shove | * {multiheld} in|into|inside|within {noun2} => insert ; * {multiheld} on|onto|upon|atop {noun2} => puton ; * down {multiheld} => drop ; * {multiheld} down => drop ; * on {held} => wear ; * {held} on => wear ; * out {noun} => extinguish ; * {noun} out => extinguish",
        "insert | insert, slide, slot | * {multiheld} in|into|inside {noun2}",
        "puton | | * {multiheld} on {noun2}",
        "wear | wear, don, put on, dress in | * {held}",
        "remove | remove, take off, doff, shed | * {held} ; * {multi} from|out_of {noun2} => take",
        "open | open, unwrap, uncover, pry open | * {noun} ; * {noun} with {held2} => unlock",
        "close | close, shut, cover, slam | * {noun} ; * up {noun}",
        "lock | lock | * {noun} with {held2} ; * {noun}",
        "unlock | unlock, unbolt | * {noun} with {held2} ; * {noun}",
        "eat | eat, consume, devour, munch, nibble, taste, bite | * {noun}",
        "drink | drink, sip, quaff, swallow, gulp | * {noun} ; * from {noun}",
        "read | read, peruse, skim, decipher | * {noun}",
        "give | give, offer, hand, feed, donate, pass | * {held} to {person2} ; * {person2} {held}",
        "show | show, display, present, flash | * {held} to {person2} ; * {person2} {held}",
        "ask | ask, question, query, interrogate, quiz | * {person} about {topic} ; * {person} for {noun2} => askfor ; * {person} to {text} => order ; * about {topic}",
        "askfor | request, beg | * {person} for {noun2} ; * for {noun2}",
        "tell | tell, inform | * {person} about {topic} ; * {person} to {text} => order ; * about {topic}",
        "order | order, command, instruct | * {person} to {text}",
        "talk | talk, speak, chat, converse, talk to, speak to, talk with, speak with, chat to, chat with, greet, hail | * ; * {person} ; * to|with {person} ; * about {topic}",
        "say | say, shout, yell, scream, whisper, call, cry, utter, mutter, sing out | * {text} to {person2} ; * {text}",
        "answer | answer, reply, respond | * {text} to {person2} ; * {text}",
        "attack | attack, hit, kill, fight, punch, strike, murder, slay, stab, thump, assault, whack, bash, beat | * {noun} ; * {noun} with {held2}",
        "kick | kick, boot | * {noun}",
        "break | break, smash, shatter, destroy, wreck, crack, demolish | * {noun} ; * {noun} with {held2}",
        "push | push, press, shove, move, shift, nudge, prod, poke, depress | * {noun} ; * {noun} {direction} ; * {noun} to {noun2}",
        "pull | pull, drag, tug, yank, heave, jerk | * {noun} ; * on {noun}",
        "turn | turn, rotate, twist, spin, screw, unscrew, crank, wind, dial | * {noun} ; * {noun} to {number} ; * {noun} to {text} ; * on {noun} => switchon ; * {noun} on => switchon ; * off {noun} => switchoff ; * {noun} off => switchoff",
        "switch | switch, flip, toggle, flick | * {noun} ; * on {noun} => switchon ; * {noun} on => switchon ; * off {noun} => switchoff ; * {noun} off => switchoff",
        "switchon | switch on, turn on, activate, start, power up, boot up | * {noun}",
        "switchoff | switch off, turn off, deactivate, stop, power down, shut down | * {noun}",
        "light | light, ignite, kindle | * {noun} ; * {noun} with {held2}",
        "extinguish | extinguish, put out, blow out, douse, snuff, quench, snuff out | * {noun}",
        "burn | burn, set fire to, torch | * {noun} ; * {noun} with {held2}",
        "climb | climb, scale, clamber, shin, climb up, climb down, climb over | * ; * {noun} ; * up|down|over|on|onto {noun}",
        "jump | jump, leap, hop, vault, jump over, jump across | * ; * {noun} ; * over|across|on|onto|off|into|in {noun}",
        "listen | listen, hear | * ; * to {noun} ; * {noun}",
        "smell | smell, sniff, inhale | * ; * {noun}",
        "touch | touch, feel, stroke, pat, pet, caress, tickle | * {noun}",
        "rub | rub, polish, clean, wipe, shine, scrub, buff, dust, wash | * {noun} ; * {noun} with {held2}",
        "wait | wait, z, pause, linger, loiter | * ; * for {text}",
        "sleep | sleep, nap, rest, snooze, doze | *",
        "wake | wake, awaken, wake up, rouse | * ; * {person} ; * up {person} ; * {person} up",
        "throw | throw, toss, hurl, fling, chuck, lob, pitch | * {held} ; * {held} at|to|towards|toward|against {noun2} ; * {held} in|into|down {noun2}",
        "fill | fill, refill | * {noun} ; * {noun} with|from|in|at {noun2}",
        "empty | empty, pour, spill, tip, drain | * {noun} ; * {noun} in|into|on|onto|over|out {noun2} ; * out {noun}",
        "dig | dig, excavate, burrow, shovel | * ; * {noun} ; * in|into|with {noun} ; * {noun} with {held2} ; * with {held2}",
        "cut | cut, slice, chop, saw, slash, sever, carve | * {noun} ; * {noun} with {held2} ; * through {noun} ; * through {noun} with {held2}",
        "tie | tie, attach, fasten, connect, bind, hook, knot | * {noun} ; * {noun} to|onto|around {noun2}",
        "untie | untie, detach, unfasten, disconnect, unhook, free | * {noun} ; * {noun} from {noun2}",
        "wave | wave, brandish, flourish, shake | * ; * {held} ; * at {person} ; * {held} at {noun2}",
        "kiss | kiss, embrace, hug, cuddle | * {person}",
        "buy | buy, purchase | * {noun} ; * {noun} from {person2}",
        "sit | sit, sit down, sit on, sit in, perch | * ; * on|in {noun} ; * {noun}",
        "stand | stand, stand up, rise, get up | * ; * on {noun}",
        "lie | lie, lie down, recline | * ; * on|in {noun}",
        "knock | knock, rap, tap, bang | * ; * on|at {noun} ; * {noun}",
        "use | use, apply, utilise, utilize, employ, operate | * {noun} ; * {noun} on|with|in|to {noun2}",
        "lookbehind | look behind, peer behind, l behind | * {noun}",
        "lookunder | look under, look beneath, peer under, check under, l under | * {noun}",
        "type | type, enter code, key in | * {text} ; * {text} on|into {noun2}",
        "write | write, scribble, inscribe | * {text} ; * {text} on|in {noun2}",
        "pray | pray, worship | * ; * to {noun}",
        "think | think, ponder, contemplate, consider, meditate | * ; * about {topic}",
        "sing | sing, hum, whistle | * ; * {text}",
        "dance | dance, twirl, boogie | *",
        "swim | swim, dive, paddle, bathe | * ; * in {noun} ; * {direction}",
        "shout | shout, holler, bellow | * ; * {text}",
        "cry | weep, sob | *",
        "laugh | laugh, giggle, chuckle, smile, grin | * ; * at {noun}",
        "blow | blow | * {noun} ; * on|into|in {noun}",
        "play | play | * {noun} ; * with {noun}",
        "hide | hide | * ; * behind|under|in {noun} ; * {held} in|under|behind {noun2}",
        "follow | follow, chase, pursue, track | * {noun}",
        "yes | yes, y, yeah, yep, ok, okay, sure | *",
        "no | no, nope, nah | *",
        "sorry | sorry, apologise, apologize | *",
        "xyzzy | xyzzy, plugh, plover | *",
        "disarm | disarm, defuse, dismantle | * ; * {text}",
        "diagnose! | diagnose, health, hp, status, wounds | *",
        "score! | score, points | *",
        "turns! | turns, time | *",
        "hint! | hint, hints, clue, clues | *",
        "help! | help, about, info, instructions | *",
        "save! | save, suspend | * ; * {text}",
        "restore! | restore, load, resume | * ; * {text}",
        "restart! | restart, reset, begin again | *",
        "quit! | quit, q, bye, goodbye, end game, stop game | *",
        "undo! | undo, back, take back | *",
        "verbose! | verbose | *",
        "brief! | brief, normal | *",
        "exits! | exits, directions, ways | *",
        "vocabulary! | vocabulary, words, verbs, commands | *",
    };

    public static readonly (string Canonical, string[] Words)[] Directions =
    {
        ("north", new[] { "north", "n", "northward", "northwards" }),
        ("south", new[] { "south", "s", "southward", "southwards" }),
        ("east", new[] { "east", "e", "eastward", "eastwards" }),
        ("west", new[] { "west", "w", "westward", "westwards" }),
        ("northeast", new[] { "northeast", "ne", "north-east" }),
        ("northwest", new[] { "northwest", "nw", "north-west" }),
        ("southeast", new[] { "southeast", "se", "south-east" }),
        ("southwest", new[] { "southwest", "sw", "south-west" }),
        ("up", new[] { "up", "u", "upward", "upwards", "upstairs", "ascend" }),
        ("down", new[] { "down", "d", "downward", "downwards", "downstairs", "descend" }),
        ("in", new[] { "in", "inside", "inward", "inwards" }),
        ("out", new[] { "out", "outside", "outward", "outwards" }),
    };

    public static readonly string[] Prepositions =
    {
        "in", "into", "inside", "within", "on", "onto", "upon", "atop", "under", "underneath", "beneath", "below",
        "behind", "over", "through", "with", "using", "to", "towards", "toward", "from", "at", "about", "for", "off",
        "out", "of", "across", "around", "near", "by", "between", "against", "up", "down", "beside", "along", "past",
    };

    public static readonly string[] Adverbs =
    {
        "carefully", "quietly", "quickly", "slowly", "gently", "softly", "loudly", "hard", "firmly", "forcefully",
        "violently", "cautiously", "silently", "noisily", "roughly", "tenderly", "lightly", "heavily", "briskly",
        "angrily", "calmly", "politely", "rudely", "boldly", "bravely", "carelessly", "casually", "cleverly",
        "deliberately", "desperately", "eagerly", "fiercely", "frantically", "furiously", "gracefully", "greedily",
        "happily", "hastily", "honestly", "hopefully", "innocently", "madly", "nervously", "patiently", "powerfully",
        "precisely", "rapidly", "recklessly", "sadly", "secretly", "sharply", "sneakily", "stealthily", "steadily",
        "strongly", "suddenly", "swiftly", "thoroughly", "urgently", "warily", "weakly", "wildly", "fast", "closely",
        "completely", "partly", "slightly", "tightly", "loosely", "backwards", "forwards", "sideways", "together",
        "apart", "silently", "discreetly", "boldly", "confidently", "respectfully", "sweetly", "meekly", "timidly",
        "vigorously", "repeatedly", "once", "twice", "harder", "softer", "faster", "slower", "properly", "normally",
    };

    /// <summary>Common adjectives recognised even when no item uses them, so the parser can explain "you see no such thing".</summary>
    public static readonly string[] Adjectives =
    {
        "red", "green", "blue", "yellow", "black", "white", "grey", "gray", "brown", "orange", "purple", "pink", "silver",
        "golden", "gold", "bronze", "brass", "copper", "iron", "steel", "wooden", "stone", "glass", "crystal", "paper",
        "big", "large", "huge", "small", "little", "tiny", "tall", "short", "long", "old", "new", "ancient", "rusty",
        "shiny", "dusty", "dirty", "clean", "broken", "heavy", "light", "empty", "full", "open", "closed", "locked",
        "dark", "bright", "strange", "odd", "mysterious", "magic", "magical", "wet", "dry", "hot", "cold", "sharp",
        "blunt", "round", "square", "left", "right", "front", "back", "top", "bottom", "first", "second", "third",
        "last", "other", "same", "rotten", "fresh", "dead", "sleeping", "angry", "friendly",
    };

    public static readonly string[] Articles = { "the", "a", "an", "some", "my", "your", "his", "her", "its", "their", "this", "that", "these", "those", "any" };
    public static readonly string[] Pronouns = { "it", "them", "him", "her", "they" };
    public static readonly string[] Quantifiers = { "all", "everything", "every", "each", "both", "anything" };
    public static readonly string[] Exceptions = { "except", "but", "excluding", "besides", "apart" };
    public static readonly string[] Conjunctions = { "and", "&", "plus" };
    public static readonly string[] SentenceBreaks = { "then", "afterwards", "next" };
    public static readonly string[] SelfWords = { "me", "myself", "self", "yourself", "player" };
    public static readonly string[] Ignored = { "please", "very", "really", "kindly", "just", "quite", "somewhat", "rather" };

    /// <summary>Phrases stripped from the start of a command ("I want to open the door" → "open the door").</summary>
    public static readonly string[] LeadingNoise =
    {
        "i want to", "i would like to", "i'd like to", "i will", "i'll", "i wish to", "let me", "let's", "lets",
        "try to", "try and", "i try to", "i", "can you", "could you", "would you", "will you", "can i", "could i", "may i",
        "go and", "go ahead and", "now",
    };

    /// <summary>Whole-sentence idioms mapped to commands.</summary>
    public static readonly Dictionary<string, string> Idioms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["where am i"] = "look",
        ["what is here"] = "look",
        ["what's here"] = "look",
        ["look around"] = "look",
        ["what am i carrying"] = "inventory",
        ["what am i holding"] = "inventory",
        ["what do i have"] = "inventory",
        ["what have i got"] = "inventory",
        ["who am i"] = "examine me",
        ["what now"] = "hint",
        ["what should i do"] = "hint",
        ["how do i play"] = "help",
        ["which way"] = "exits",
        ["where can i go"] = "exits",
        ["get up"] = "stand",
        ["sit down"] = "sit",
        ["lie down"] = "lie",
        ["wake up"] = "wake",
        ["give up"] = "quit",
    };

    public static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20,
        ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["hundred"] = 100,
    };

    public static readonly Dictionary<string, int> Ordinals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = 1, ["1st"] = 1, ["second"] = 2, ["2nd"] = 2, ["third"] = 3, ["3rd"] = 3, ["fourth"] = 4, ["4th"] = 4,
        ["fifth"] = 5, ["5th"] = 5, ["last"] = -1,
    };
}
