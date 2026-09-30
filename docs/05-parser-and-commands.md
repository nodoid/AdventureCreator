# 5. The parser and custom commands

## What players can type

The parser understands natural English commands. It starts from a built-in dictionary of about 100 verbs (with
synonyms), directions, prepositions, 90 adverbs and common adjectives. It also learns every noun and adjective used by
your items and triggers.

| Feature | Examples |
|---|---|
| Directions | *n*, *north*, *go north*, *walk south-west*, *up*, *in*, custom exits such as *portal* |
| Objects with adjectives | *take the small brass key* |
| Asking which one | *take key* → "Which do you mean, the red key or the brass key?" → *brass* (or *the second one*, *both*) |
| Indirect objects | *put the coin in the slot*, *unlock the door with the key*, *give the bread to the troll* |
| Phrasal verbs | *pick up the lamp*, *pick the lamp up*, *switch the torch on*, *take off the coat* |
| Adverbs anywhere | *quietly open the door*, *open the door quietly*, *carefully go up*. Unknown *-ly* words count too (*glumly*). |
| Several objects | *take the lamp and the key*, *take all*, *drop everything except the sword*, *take all from the box*, *take coins* |
| Pronouns | *take the lamp. light it*, *ask her about the locket*, *drop them* |
| Several commands | *take the lamp and go north then light it*, *n. e. open door* |
| Orders | *robot, go north*, *tell the robot to open the hatch*, *ask the robot to push the button* |
| Conversation | *ask Tom about the key*, *tell him about the storm*, *talk to Tom*, *say "hello"*, *say "open sesame"* |
| Numbers | *turn the dial to 7*, *turn dial to twelve* |
| Filler and idioms | *I want to open the door*, *please look*, *where am I?*, *what am I carrying?* |
| Mistakes | *exmaine* is corrected; *OOPS lamp* fixes the last unknown word; *AGAIN* (G) repeats; *UNDO* takes back a move |

**NPC, trap and health commands:** FOLLOW (someone who just left), SEARCH (with no object: finds traps), DISARM, DIAGNOSE (HEALTH), *robot, go north* (orders).

**Saving:** SAVE / RESTORE (LOAD) accept an optional name: *save before the troll*, *restore "castle"*.

**Game commands:**
* LOOK (L), INVENTORY (I), EXITS
* SCORE, TURNS, HINT, HELP, VOCABULARY
* SAVE, RESTORE, RESTART, QUIT
* UNDO, AGAIN, OOPS
* VERBOSE, BRIEF

Type **HELP** in any game for a summary, and **VOCABULARY** for the verbs it knows. In the Studio, **Help › Parser &
Command Reference** lists every verb, its synonyms and its grammar for the game you have open.

### Built-in verbs

Every built-in verb has sensible default behaviour, which your triggers can override:

| Group | Verbs |
|---|---|
| Looking | look, examine, search, look under, look behind, read, listen, smell, touch |
| Moving | go, enter, exit, climb, jump, swim, follow |
| Taking and dropping | take, drop, put in / on, insert, throw, give, show |
| Wearing | wear, remove |
| Doors and locks | open, close, lock, unlock |
| Light and switches | switch on / off, light, extinguish, burn |
| Food | eat, drink |
| Containers | fill, empty |
| People | ask, tell, talk, say, answer, order, kiss, wake |
| Force | attack, kick, break, push, pull, turn, rub |
| Other actions | cut, tie, untie, dig, use, type, write, buy, knock, wave, sit, stand, lie, wait, sleep, pray, think, sing, dance, shout, laugh, cry, blow, play, hide, yes, no, sorry, xyzzy |

Verbs whose natural outcome depends on your game (cut, dig, use…) reply with a neutral message until you write a trigger or an item Verb Response.

## Adding new commands

Open **Commands**. Your own commands are listed first; underneath, under **BUILT-IN COMMANDS**, are all the verbs the parser already knows. Select one to see its words and grammar, and press **Add words or grammar to this command** to extend it (your extension joins the top of the list). Click **+** to add a new command. A command has:

| Field | Meaning |
|---|---|
| **Id** | The name triggers use in their Verb field (`polish`). |
| **Words** | Everything players may type for it, including phrases: `polish, shine, buff up`. |
| **Grammar** | One pattern per line (see below). If empty, the command accepts anything: `polish`, `polish lamp`, `polish lamp with cloth`. |
| **Default Response** | Printed when no trigger handles the command. May use placeholders: `You polish {the noun1} {adverb}.` |
| **Default Actions** | Actions run when no trigger handles the command. |
| **Is Meta** | Doesn't use a turn (like SCORE). |
| **Help** | Shown by the HELP command. |

Then write triggers with **Verb** = the command's id for the special cases (Verb `polish`, Noun1 `lens`, …).

**Extending a built-in verb:** give your command the same id as a built-in verb (`take`, `open`…).
* Its words are added: for example, id `take` with words `nick, pinch` makes *nick the jewels* mean take.
* Its grammar lines are tried **before** the built-in ones.
* A word you list is taken away from any other verb that had it, so you can also re-assign words: a command `polish` with the word `wipe` takes *wipe* away from *rub*.

### Grammar lines

A grammar line describes one way to phrase the command. `*` stands for the verb (any of its Words); the rest are
literal words and **slots**:

| Slot | Matches |
|---|---|
| `{noun}` | one thing the player can see |
| `{held}` | one thing the player is carrying. Built-in actions pick it up first if needed: "(first taking the key)". |
| `{multi}` | one or more things: *all*, *the lamp and the key*, *all except the sword*, plurals |
| `{multiheld}` | one or more carried things |
| `{person}` | a character |
| `{noun2}` `{held2}` `{person2}` | the same, for the **second** (indirect) object. Unnumbered slots fill the first object and numbered slots the second. If a line has two unnumbered object slots, the second becomes the indirect one. |
| `{direction}` | a direction word |
| `{number}` | a number (digits or words up to twenty, thirty, forty…) |
| `{topic}` / `{text}` | free text: the rest of the input, or up to the next literal word |

Literal words can have alternatives: `in|into|inside`. Multi-word literals use `_`: `out_of`.
Add `=> otherid` at the end of a line to turn it into a different command, as the built-in `put on {held} => wear` does.

Examples:

```text
* {noun}                              polish the lamp
* {noun} with {held2}                 polish the lamp with the cloth
* {multiheld} in|into {noun2}         pour the oil and water into the tank
* {person} about {topic}              quiz the butler about the murder
* up {noun} => climb                  shin up the pole  (becomes the climb command)
* {noun} to {number}                  set the dial to 7
* {text} on|into {noun2}              type "xyzzy" into the terminal
```

Lines are tried in order; the first that fits wins. If none fits, the command is understood *loosely*: verb, first object, preposition, second object. Triggers still see it. If no trigger handles it, the player gets a helpful message ("I only understood you as far as wanting to polish").

### Testing grammar

* In the Studio, the **Parser lab** at the bottom of every command page shows how any sentence is understood, for example `polish [carefully] obj1=the lens prep=with obj2=the cloth [grammar: * {noun} with {held2}]`.
* In a terminal: `adventure-player --parse mygame.adventure "carefully polish the lens with the cloth"`.

## Vocabulary

The **Vocabulary** section holds words that aren't attached to items or commands. For each kind of word, your own words come first, each with **✕** to remove it; type new words in the box and press **Enter** or **Add** (several at once, separated by commas). Underneath are the words the parser already knows, or has learned from your items, for reference.

| Field | Use |
|---|---|
| Nouns | Words that should be recognised even though no item uses them (scenery mentioned only in descriptions and handled by triggers: *sky*, *sea*). Trigger nouns are added automatically. |
| Adverbs | Extra adverbs (the built-in list covers carefully, quietly, quickly, slowly, gently, hard, loudly, silently, stealthily and about 80 more). |
| Adjectives | Extra adjectives. |
| Prepositions | Extra prepositions. |
| Ignored Words | Words to drop silently (*please* and *very* are already ignored). |
| Replacements | A phrase and what it means. If the whole input equals the phrase it is replaced (`xyzzy` → `say xyzzy`, `sneak west` → `quietly go west`); single words are also replaced inside sentences (`lift` → `look under`). Type both parts and press **Add**. |
| Directions | Extra direction words mapped to an exit direction: `fore` means north, `aft` south, `starboard` east. Type the word, choose the direction and press **Add**. |

## Significant letters (retro style)

**Game › Settings › Significant Letters** makes only the first *n* letters of each word count, as PAWS (5) and the Quill (4) did:
*exami* then means *examine*, and *lante* means *lantern*. Imported games set this automatically. For new games, leave it at 0 and keep spelling correction on.
