# 3. Rooms, items and characters — reference

This chapter lists every property you can edit. Property names match the labels in the Studio.

## Game (Game section)

| Property | Meaning |
|---|---|
| Title, Author, Version, Description | Shown by players and used for exported app names. |
| Introduction | Printed when the game starts, before the first room. Supports [placeholders](04-triggers.md#text-placeholders). |
| Intro Picture id / Intro Sound id | Shown / played at the start. |
| Start Room id | Where the player begins. |

### Settings

| Setting | Default | Meaning |
|---|---|---|
| Max Score | 0 | 0 = calculated automatically (see below). Set a number to override. |
| Max Carried Items | 10 | How many things the player can hold (worn items don't count). 0 = unlimited. |
| Max Carried Weight | 0 | Total weight limit, including the contents of carried containers. 0 = unlimited. |
| Show Pictures | on | Players show room pictures. |
| Auto List Items | on | Room descriptions list the items in the room. |
| Auto List Exits | on | Room descriptions end with "Exits: …". Hidden exits are never listed. |
| Allow Undo | on | Players may UNDO (up to 50 moves). |
| Verbose | on | on: the full description on every visit. off: the Short Description after the first visit. Players can type VERBOSE / BRIEF. |
| Significant Letters | 0 | If non-zero, only this many leading letters of each word matter (PAWS used 5, the Quill 4). |
| Spelling Correction | on | Unknown words are corrected to the nearest known word ("(taking 'lamb' to mean 'lamp')"). |
| Default Picture id | — | Shown in rooms that have no picture. |
| Text Color / Background Color | `#1F1F1F` / `#FBFAF6` | Player colours (`#RRGGBB`). |
| Font Family | — | Optional font name for the transcript. |
| Prompt | `> ` | Echoed before each command in the transcript. |
| Legacy Table Semantics | off | PAWS/Quill/GAC behaviour (set by importers; see [chapter 7](07-importing.md#how-imported-games-behave)). |
| Darkness Variable | — | If set, the room is dark while this variable is non-zero (Quill/PAWS flag 0). |
| Exits Before Triggers | off | Movement is tried before BeforeCommand triggers (GAC behaviour). |
| Show Status Bar | on | Players show location / score / turns. |

**Maximum score** (automatic) = puzzle Points + item Score On Take + room Score On First Visit + every positive AwardScore action in triggers, conversation topics, puzzle On Solved actions and command default actions. Awards that share a key are counted once.

## Rooms

| Property | Meaning |
|---|---|
| Id | Unique id (also unique across items). |
| Name | Title shown above the description and in the status bar. |
| Description | Full description. |
| Short Description | Used after the first visit when the game or the player chooses BRIEF. |
| Is Dark | Dark unless a lit light source is carried or in the room. In darkness the player sees only "It is pitch dark" and cannot see or take things, but can still move and use carried items. |
| Picture id | Picture shown on entry and on LOOK. |
| Sound id, Loop Sound | Ambient sound started on entry (looped by default). It stops when the player enters a room without a sound, and continues across rooms that share it. |
| Score On First Visit | Points for reaching the room. |
| Exits | See below. |

### Exits

| Property | Meaning |
|---|---|
| Direction | `north` `south` `east` `west` `northeast` `northwest` `southeast` `southwest` `up` `down` `in` `out`, or any word of your own (`portal`, `hatch`). |
| Target Room id | Destination. |
| Door Item id | An openable item. The exit is blocked while it is closed ("The door is closed" / "is locked"). Typing *enter door* or *go through door* uses this exit. |
| Conditions | All must be true to pass (see [conditions](04-triggers.md#conditions)). |
| Blocked Message | Shown when a condition fails (otherwise "You can't go that way"). |
| Hidden | Not listed in "Exits:" but still usable. |
| Travel Message | Printed as the player goes through. |

The room editor's **Add return exits from destination rooms** button creates the opposite exit in each destination room (north↔south, up↔down, in↔out…), including the same door.

## Items (and characters)

| Property | Meaning |
|---|---|
| Id, Name | Unique id; the name shown in lists ("brass lamp"). |
| Article | `a`, `an`, `some`, `the`, or empty for proper names ("Ronson"). |
| Nouns | Words that refer to the item. Multi-word nouns work (`control panel`). If empty, the last word of the Name is used. |
| Adjectives | Words that tell similar items apart. Words from the Name also count as adjectives. |
| Description | Shown by EXAMINE. |
| Room Description | A sentence of its own in the room description ("A lantern hangs from a hook."). It is used until the item has been taken once; after that, the item is listed normally. |
| Location | A room id, a container/supporter item id, *Carried*, *Worn*, or empty (*nowhere*: not yet in the game, or destroyed). |
| Portable | Can be taken. |
| Scenery | Never listed and cannot be taken. Anything on or in scenery is still mentioned ("On the table you can see an oil can."). |
| Wearable | Can be worn (WEAR / PUT ON / REMOVE / TAKE OFF). |
| Edible / Drinkable | EAT / DRINK consume it. |
| Container, Supporter | Things can be put *in* / *on* it. Contents are visible when a container is open or Transparent. |
| Transparent | Contents visible even when closed. |
| Capacity | Maximum number of items inside or on top. |
| Openable, Is Open | Can be opened and closed; starting state. |
| Lockable, Is Locked, Key Item id | Can be locked. UNLOCK needs the key; *unlock door* works without naming the key if the player carries it. |
| Light Source, Is Lit, Switchable | Lights dark rooms while lit. LIGHT / SWITCH ON / EXTINGUISH / TURN OFF work on light sources and switchable items. |
| Readable, Read Text | READ shows Read Text (or the Description). |
| Weight | For Max Carried Weight. |
| Score On Take | Points the first time it is taken. |
| Is Character | A person or creature: can be talked to and given orders; cannot be taken. |
| Plural | Grammar help ("some crates"): *them* refers to it. |
| Picture id | Shown when the item is examined. |
| Topics | Conversation (characters). |
| Verb Responses | Quick custom replies: key = command id (`smell`, `push`, `polish`…), value = text. Used when no trigger handles the command. |

### Conversation topics

Players can talk in several ways:
* *ask Tom about the key*
* *tell Tom about the storm*
* *talk to Tom*
* *say "hello"*, which is heard by the first character present

Each topic has:
* **Keywords**: any word of the player's topic text matches (and multi-word keywords match as phrases). *talk to* matches keywords `hello`, `talk`, `greeting` or `hi`.
* **Conditions**: the topic is only used while these are true. Put conditional topics *before* general ones with the same keywords; the first matching topic wins.
* **Response** and **Actions**: printed and run. Actions can give items, award points, set variables and so on.

If no topic matches, the character "doesn't seem to know anything about that".

### Orders

*Ronson, open the door* and *tell Ronson to open the door* are orders. Orders only reach triggers whose **Subject** is that character (or `*`). Without such a trigger, the character "ignores you".

## Puzzles

| Property | Meaning |
|---|---|
| Id, Name, Description | Name is shown by HINT. |
| Points | Awarded once when solved. |
| Solved When | Conditions checked at the end of every turn; when all are true, the puzzle is solved. Leave empty to solve it only with the SolvePuzzle action. |
| On Solved | Actions run when solved. |
| Solved Message | Printed when solved. |
| Hints | Progressive hints. Each HINT shows the next one; the last one repeats. |
| Hint Room ids | Rooms where the hints are offered (empty = anywhere). Puzzles for the current room are preferred. |

SCORE also reports "Puzzles solved: n of m". A trigger with the **PuzzleSolved** event can react to a puzzle being solved, for example to end the game.

## Variables

Named whole numbers, starting at their **Initial Value**. Test them with VarEquals / VarGreater / VarLess, change them with SetVar / AddVar / CopyVar / RandomVar, and print them with `{var:name}`.

Built-in pseudo-variables:

| Name | Value | Can be set? |
|---|---|---|
| `@score` | the score | yes |
| `@turns` | turns taken | yes |
| `@room` | position of the current room in the Rooms list (0 = first) | yes (moves the player) |
| `@carried` | number of items held (not worn) | no |
| `@maxscore` | maximum score | no |

## Messages

The **Messages** section lists every built-in reply ("You can't go that way.", "Taken.", "I don't understand that."…) with its default text shown in grey. Type a replacement to change it. Clear the box to restore the default.

A message deliberately set to an empty string (for example by an importer) prints nothing. Messages can use [placeholders](04-triggers.md#text-placeholders) such as `{The noun1}`, `{list}` and `{direction}`.
