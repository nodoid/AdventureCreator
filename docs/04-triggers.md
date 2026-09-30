# 4. Triggers, conditions and actions

A **trigger** is a rule: *when* something happens (the **event**), *if* the command matches the **pattern** and all
**conditions** are true, *then* run the **actions**. Triggers are how you write puzzles, special responses, timed
events and cut-scenes.

## Events

| Event | Fires | Notes |
|---|---|---|
| **BeforeCommand** | When the player enters a command, *before* the built-in behaviour. | The usual choice. By default it replaces the built-in behaviour (see [flow](#how-a-command-is-processed)). |
| **AfterCommand** | After a command's built-in behaviour **succeeded** (the item was taken, the door opened…). | For reactions: "As you take the idol, the floor trembles." |
| **EveryTurn** | At the end of every turn (after the command). | If Verb/Noun are set, only on turns whose command matches. Room restricts it to the player's location. |
| **Timer** | On turn number **Turn**, and/or every **Interval** turns. | For deadlines and recurring atmosphere. |
| **GameStart** | Once, before the first room description. | Set-up, opening cut-scenes. |
| **BeforeEnterRoom** | When the player arrives in a room, before it is described. Moving the player on (GoTo) here skips the room. | Room = which room (empty = any). |
| **EnterRoom** | After the player arrives in a room (after its description). | Room = which room (empty = any). |
| **LeaveRoom** | Just before the player leaves a room. | A **Done** action here cancels the move. |
| **ItemTaken / ItemDropped** | After an item is taken / dropped (by the player or by TakeItem/DropItem). | Subject = the item (empty = any). |
| **PuzzleSolved** | When a puzzle is solved. | Subject = the puzzle. Good for endings. |
| **AfterDescribe** | After every room description (arrival, LOOK, even "It is pitch dark"). | PAWS "Process 1". |
| **Subroutine** | Never automatically; only through the **RunTrigger** action. | Reusable action lists. RunTrigger runs every Subroutine trigger whose Id **or Name** matches, in list order, so several triggers sharing a Name form one subroutine table. |
| **Unhandled** | When the parser can't understand the input (unknown word, no verb). | If any Unhandled trigger fires, the normal error isn't shown. `{word}` is the unknown word. |
| **NpcArrives / NpcLeaves** | When an NPC enters / leaves the player's room. | Subject = the NPC (empty = any). |
| **NpcDefeated** | When the player defeats an NPC in combat. | Subject = the NPC. |
| **ItemGiven** | When an NPC accepts an item it wants. | Subject = the NPC, Noun1 = the item. |
| **PlayerHurt** | When the player loses health. | Subject = the attacking NPC, if any. |

## The pattern

The pattern fields apply to command events (BeforeCommand, AfterCommand) and optionally to EveryTurn and Subroutine.

| Field | Matches | Values |
|---|---|---|
| **Verb** | The command | A command id (`take`, `polish`), a verb word the player typed (`grab`), or a direction (`north` also matches *go north*). Separate alternatives with `\|`: `light\|burn`. |
| **Noun1** / **Noun2** | The direct / indirect object | An item id (`lamp`), or any word (`sky`, even if no item has that noun). Alternatives with `\|`. |
| **Adverb** | Adverbs in the sentence | Words (`quietly\|silently`), `*` = any adverb, `-` = no adverb. |
| **Preposition** | Prepositions used (*in*, *with*, *under*…) | Words, `*`, `-`. |
| **Room** | The player's current room | A room id. Empty = anywhere. |
| **Subject** | For command events: orders given to a character (`Tom, jump`). For ItemTaken/Dropped: the item. For PuzzleSolved: the puzzle. | Empty = only the player's own commands; `*` = orders to anyone. |

In every pattern field, **empty or `*` means "anything"** and **`-` means "must be absent"**. So `Noun1 = -` matches *jump* but not *jump over the fence*.

Other trigger properties:

| Property | Meaning |
|---|---|
| Enabled | Disabled triggers never fire (the EnableTrigger / DisableTrigger actions change this during play). |
| Once Only | Fires at most once per game. |
| Priority | Higher runs first; equal priorities run in list order (use ↑ ↓ in the list). |
| Stops Command | *(command events)* On by default: once this trigger has fired, the command is finished. Turn it off, or end the actions with **Continue**, to let later triggers and the built-in behaviour run too. |
| Turn, Interval | For Timer triggers. |
| Group | For imported games: a **Done** ends only the triggers of the same group. |
| Notes | Your own notes. |

## How a command is processed

1. The parser turns the input into one or more commands.
2. Objects are resolved against what the player can see. If a phrase is ambiguous, the player is asked "Which do you mean…?" and the command waits for the answer.
3. For each command (and for each object of *take all*):
   1. **BeforeCommand** triggers are checked in priority order. For each one whose pattern matches and whose conditions are true, its actions run:
      * **Done**, **Ok**, **Win** or **Lose** end the command.
      * **Continue** lets the command carry on.
      * Otherwise, if *Stops Command* is on, the command is finished.
   2. If no trigger finished it, the item's **Verb Responses** are used for this command, if any.
   3. Otherwise a **custom command** prints its Default Response and runs its Default Actions, or the **built-in** behaviour runs (take, open, go…).
   4. If that succeeded, **AfterCommand** triggers run.
4. End of turn (commands like SAVE, SCORE, HINT and UNDO don't use a turn):
   1. the turn counter goes up
   2. **Timer** triggers run
   3. **EveryTurn** triggers run
   4. **puzzles** are checked

## Conditions

All conditions must be true. Tick **not** to reverse one. Fields are **A**, **B** and **N** (a number).
Wherever an item is expected you can use `$noun1` / `$noun2` (the item the player named).

| Condition | True when | Fields |
|---|---|---|
| Always | always | — |
| PlayerIn | the player is in the room | A = room (alternatives with `\|`) |
| ItemCarried | the item is held or worn | A = item |
| ItemWorn | the item is worn | A = item |
| ItemPresent | the item is here: carried, in the room, or visible in an open container | A = item |
| ItemIn | the item is exactly at a location | A = item, B = room / container / `@carried` / `@worn` / `@here` / empty (nowhere) |
| ItemExists | the item is anywhere in the game (not nowhere) | A = item |
| ItemOpen / ItemLocked / ItemLit | the item is open / locked / lit | A = item |
| VarEquals / VarGreater / VarLess | compare a variable with N | A = variable, N |
| VarEqualsVar / VarGreaterVar / VarLessVar | compare two variables: A = B + N, A > B + N or A < B + N. N is an optional offset, usually 0 (for example *score > best − 5* is A = `score`, B = `best`, N = −5). | A, B = variables, N |
| Chance | random, N percent of the time | N = 0–100 |
| TurnsAtLeast / ScoreAtLeast | turns taken / score ≥ N | N |
| CarriedCountAtLeast | holding at least N items | N |
| RoomVisited | the player has been to the room | A = room |
| PuzzleSolved | the puzzle is solved | A = puzzle |
| TriggerFired | the trigger has fired at least once | A = trigger id |
| AdverbUsed | the command used this adverb (empty A = any adverb) | A = words, `\|`-separated |
| AdjectiveUsed | the player used this adjective in a noun phrase (*take the **red** key*) | A = words |
| WordUsed | the command contains this word anywhere | A = words |
| PrepositionIs | the command used this preposition | A = words |
| Noun1Is / Noun2Is | the first / second object is this item or word | A = item id or word |
| IsDark | the player is in darkness | — |
| ExitOpen | the room's exit in that direction exists and its door (if any) is open | A = room, B = direction |
| NpcFollowing / NpcHostile / NpcDefeated | the NPC is following the player / hostile / defeated | A = NPC |
| NpcIn | the NPC is in the room | A = NPC, B = room |
| NpcHasItem | the NPC holds the item | A = NPC, B = item |
| HealthAtLeast | the player's health ≥ N | N |
| RoomHasFlag | the room has a flag set with SetRoomFlag | A = room, B = flag |
| RoomFlooded / RoomTrapped | the room is flooded / has a trap | A = room |
| EventHappened | the random event has happened at least once | A = random event |

Room fields also accept `@here`, `@eventroom` (in random events) and `@randomroom`.

## Actions

Actions run in order. Fields: **A**, **B**, **N**, **Text**. Item fields accept `$noun1` / `$noun2`. Location fields accept `@here` (the player's room), `@carried`, `@worn`, or empty for nowhere.

**Text and display**

| Action | Does | Fields |
|---|---|---|
| Message | prints Text (with [placeholders](#text-placeholders)) | Text; N = 1 → no line break after it |
| Look | describes the room again | — |
| ShowPicture | shows a picture | A = picture (empty = the room's picture) |
| ClearScreen | clears the transcript | — |
| Pause | pauses output | N = milliseconds |
| Inventory / ShowScore / ShowTurns | as the INVENTORY / SCORE / TURNS commands | — |
| Beep | an alert (players may ignore it) | — |

**Items**

| Action | Does | Fields |
|---|---|---|
| TakeItem | the player takes the item, with the normal checks (too many, too heavy, not here). If it fails, the message is shown and the trigger stops. Silent on success. | A = item |
| DropItem, WearItem, UnwearItem | the same, for dropping, wearing and removing | A = item |
| DropAll | drops everything held | — |
| MoveItem | puts the item anywhere, with no checks | A = item, B = location |
| CreateItem | brings the item into play | A = item, B = optional location (default: the player's room; in random events, the event's room) |
| DestroyItem | removes the item from the game | A = item |
| SwapItems | exchanges two items' locations (a lamp for a lit lamp…) | A, B = items |
| SetOpen / SetLocked / SetLit | changes an item's state | A = item, N = 1 or 0 |
| SetItemDescription | replaces an item's examine text | A = item, Text |

**Player and rooms**

| Action | Does | Fields |
|---|---|---|
| GoTo | moves the player (runs Leave/Enter triggers and shows the description) | A = room, Text = optional travel message |
| SetExit | adds, changes or removes an exit | A = room, B = direction, Text = target room (empty removes it) |
| SetRoomDescription | replaces a room's description | A = room, Text |
| SetDark | makes a room dark or light | A = room (or `@here`), N = 1/0 |

**Variables, score and puzzles**

| Action | Does | Fields |
|---|---|---|
| SetVar / AddVar | sets / adds to a variable (N may be negative) | A = variable, N |
| CopyVar | A = B | A, B = variables |
| RandomVar | sets A to a random number 1…N | A = variable, N |
| AwardScore | adds N points. If A (a key) is set, the award is given only once per game. | N, A = optional key |
| SolvePuzzle | solves a puzzle now | A = puzzle |

**NPCs, health and the world** (see [chapter 10](10-npcs-and-events.md))

| Action | Does | Fields |
|---|---|---|
| SetNpc | changes an NPC's behaviour | A = NPC, B = Movement / Hostile / Following / Blocking / Active / Health, N = value, Text = movement mode |
| NpcGoTo | sends an NPC walking to a room (through exits, one room per turn) | A = NPC, B = room |
| NpcSay | the NPC says something (if the player is there) | A = NPC, Text |
| HurtPlayer / HealPlayer | changes the player's health | N = amount (Heal 0 = full), Text = message |
| Flood | floods or drains a room | A = room, N = 1/0 |
| SetTrap / ClearTrap | places or removes a trap | A = room, N = damage (−1 = deadly), Text = when sprung, B = when found |
| SetRoomFlag | sets or clears a room flag | A = room, B = flag, N = 1/0 |
| RunRandomEvent | makes a random event happen now | A = event |

**Sound**

| Action | Does | Fields |
|---|---|---|
| PlaySound | plays a sound | A = sound, N = 1 → loop (replaces the room's ambient sound) |
| StopSound | stops all sound | — |

**Flow**

| Action | Does |
|---|---|
| Done | stops this trigger; the command is finished |
| Ok | prints "OK." and stops |
| Continue | lets the command carry on (later triggers and the built-in behaviour still run) |
| RunTrigger | A = a Subroutine trigger's id or name: runs it (its conditions are checked) |
| EnableTrigger / DisableTrigger | A = trigger id |
| Win / Lose | prints Text, the win/lose message and the score, then ends the game (players can RESTART, RESTORE or UNDO) |
| Quit, Save, Restore, Restart | as the player commands |

## Text placeholders

Any text shown to the player (descriptions, messages, responses, hints) can contain placeholders:

| Placeholder | Becomes |
|---|---|
| `{noun1}` `{noun2}` | the name of the first / second object (or the word typed if no item matched) |
| `{the noun1}` `{The noun1}` `{a noun1}` (and `…noun2`) | with an article; a capital T gives a capital letter |
| `{verb}` | the verb words the player typed ("pick up") |
| `{adverb}` | the first adverb used (empty if none; any doubled space is tidied up) |
| `{direction}` `{text}` `{number}` | the direction, the free text / topic, the number in the command |
| `{score}` `{max}` `{turns}` | score, maximum score, turns |
| `{room}` `{title}` `{author}` | current room name, game title, author |
| `{actor}` `{the actor}` | the character being given an order |
| `{carried}` | list of carried items |
| `{var:name}` | a variable's value, e.g. `{var:coins}` or `{var:@turns}` |
| `{item:id}` `{room:id}` | the name of an item / room |
| `{word}` | the unknown word (in Unhandled triggers and the UnknownWord message) |
| `{newline}` | a line break |
| `{npc}` `{The npc}` `{a npc}` | the NPC concerned (NPC messages and NPC events) |
| `{health}` `{maxhealth}` | the player's health |
| `{eventroom}` | the random event's room name |

## Recipes

**A message plus the normal behaviour**
Verb `open`, Noun1 `door`; Actions: `Message "It creaks alarmingly."`, `Continue`.

**Refuse something politely**
Verb `take`, Noun1 `statue`; Actions: `Message "It's far too heavy."`.

**Do something the quiet way**
Verb `go`, Room `hall`, Conditions: `WordUsed north|n`, `AdverbUsed quietly|carefully` (tick **not**) → `Message "The guard hears you!"`, `GoTo cell`.

**A deadline**
Event Timer, Turn 100, Conditions `PuzzleSolved bomb` (not) → `Lose "The bomb explodes."`.

**A one-time reward**
In any action list: `AwardScore` N 10, A `found-map`. Repeating the action never awards the points twice.

**A lamp that runs out**
Variable `oil` = 50. EveryTurn trigger with conditions `ItemLit lamp`, `VarGreater oil 0` → `AddVar oil -1`. A second EveryTurn trigger with `ItemLit lamp`, `VarEquals oil 0` → `SetLit lamp 0`, `Message "Your lamp flickers and dies."`.

**A character who follows orders**
Verb `open`, Noun1 `hatch`, Subject `robot` → `Message "The robot wrenches the hatch open."`, `SetOpen hatch 1`. The player types *robot, open the hatch* or *tell the robot to open the hatch*.
