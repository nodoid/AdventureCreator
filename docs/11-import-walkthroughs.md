# 11. Import walkthroughs: PAWS, the Quill and GAC

This chapter follows one small game from each retro system through the importer. For each one it shows:
* **the original**, written the way that system's own editor (or a lister such as UnPAWS, UnQuill or reGAC) shows it
* **the import report**
* **where every part lands in the Studio**, entry by entry
* **the limitations you'll meet**, and how to fix them

Every result below is real output: the Studio's importers were run on the sample games in the test suite (`tests/AdventureCreator.Tests/Importers`). [Chapter 7](07-importing.md) is the reference for all three systems; this chapter shows it in practice.

---

## Before you start

The steps are the same for all three systems:

1. Take a snapshot at the game's **first prompt** (`.z80` or `.sna`; GAC can also use unprotected `.tap`/`.tzx` files). See [chapter 7, section 1](07-importing.md#1-getting-a-file-to-import).
2. **File › Import PAWS / Quill / GAC Game…** (⇧⌘I) and pick the file.
3. Read the **Import report**, then keep **Game › Notes** open while you check the game.
4. **Adventure › Validate** (⌘K), **Test Play** (⌘R), then **File › Save As…**.

### Where things go

| In the original | In the Studio | What to check |
|---|---|---|
| Locations / rooms | **Rooms** (`r0`, `r1`…) | The name is the first sentence of the description; shorten it if it's long. |
| Connections | Each room's **Exits** | Non-direction words (CLIMB, PORT…) become custom exit words. |
| Objects | **Items & People** (`o0`, `o1`…) | Nouns and adjectives, starting location, wearable/container flags. |
| Vocabulary | **Commands** (verbs) and **Vocabulary** (nouns, adjectives, adverbs, prepositions) | Verb ids are the first stored synonym, which may be truncated (`scor`). |
| Messages and system messages | Message actions; **Messages** overrides; `sys0`, `sys1`… | The engine's replies ("I can't go that way.") come from the game. |
| Flags / markers / counters | **Variables** (`f…`, `m…`, `c…`) plus `@score`, `@turns`, `@room`, `@carried` | Descriptions say what each system flag meant. |
| Response / process / condition tables | **Triggers**, one per entry | Each trigger's **Notes** name the original table and entry. |
| Pictures | **Pictures** (`p…`), attached to rooms | Spectrum attribute mode, sub-pictures as **Is Subroutine**. |
| Anything that couldn't be converted exactly | **Import report** and **Game › Notes** | Work through this list. |

All imported games have **Legacy Table Semantics** on (Game › Settings): every matching entry runs in table order until a **Done**, as in the original interpreters.

---

## Walkthrough 1: a PAWS game

### The original

A 48K PAWS game with three locations and five objects.

```
LOCATIONS  0  You are in the hall. A dusty place with a staircase.
           1  GARDEN
              Flowers grow everywhere.
           2  (empty)
CONNECTIONS 0: N 1        1: S 0
OBJECTS    0  A brass lamp      CARRIED  word LAMP  adjective BRASS  weight 2
           1  A small key       at 2 (inside object 2)  weight 1
           2  A wooden box      location 0   container, weight 5
           3  A cloak           WORN     wearable, weight 3
           4  Some sand         NOT CREATED  (no object words)
VOCABULARY  N NORTH S SOUTH GET TAKE OPEN LOOK L   LAMP LANTERN BOX KEY CLOAK SAND
            BRASS (adjective)  QUIETLY (adverb)  IN (preposition)  IT (pronoun)

RESPONSE (process 0)
  GET  LAMP   AT 0  CARRIED 0  MESSAGE 0  DONE
  OPEN BOX    PRESENT 2  ZERO 11  SET 11  MESSAGE 1  NOTZERO 12  MESSAGE 2  DONE
  _    _      ADVERB QUIETLY  PREP IN  NOUN2 _  PROCESS 3
  LOOK _      DESC
  GET  _      ADJECT1 BRASS  AUTOG  DONE
  N    _      AT 0  ISAT 1 2  SYSMESS 7  DONE
  _    _      TIMEOUT  MODE 4 0  MESSAGE 2
PROCESS 1     _ _  LISTOBJ
              _ _  AT 1  MES 3  PRINT 30  NEWLINE
PROCESS 2     _ _  NOTZERO 5  MINUS 5 1
              _ _  EQ 38 1  PLUS 30 10  COPYFF 30 100
PROCESS 3     _ _  ABILITY 6 20  PLACE 1 255  GOTO 1  OK
```

It also has a picture for location 0 (lines, fill, shade, a BLOCK, INK, a scaled GOSUB and some ROM-font text), and a sub-picture.

### The import report

```
Imported with: PAWS (ZX Spectrum)
Format: PAWS (ZX Spectrum 48K, compressed database, version 2)

3 rooms, 5 items, 12 triggers, 2 pictures.

Warnings:
• PAWS condact LISTOBJ has no equivalent (1 use skipped).
• PAWS condact MODE has no equivalent (1 use skipped).
• PAWS condact TIMEOUT has no equivalent (1 use skipped).
```

### Rooms and exits

| Room | Name in the Studio | Why |
|---|---|---|
| `r0` | *You are in the hall* | The first sentence of the description. |
| `r1` | *Garden* | A description that starts with a line on its own uses that line, in title case. |
| `r2` | *Location 2* | Empty descriptions get a placeholder. Give it a real description. |

The connections become exits `r0` **north** → `r1` and `r1` **south** → `r0`. **Picture id** of `r0` is `p0`.

### Items

| Item | Location | Nouns / adjectives | Flags | Where it came from |
|---|---|---|---|---|
| `o0` brass lamp | carried | `lamp`, `lante` / `brass` | weight 2 | Location 254 = carried. "A" is split off the name. LANTERN is stored as `lante` because PAWS matches **5 significant letters**. |
| `o1` small key | inside `o2` | `key` | weight 1 | "At 2" meant *inside object 2*, so it's placed in the box. |
| `o2` wooden box | `r0` | `box` | **container**, weight 5 | PAWS container attribute. |
| `o3` cloak | worn | `cloak` | **wearable**, weight 3 | Location 253 = worn. |
| `o4` sand | nowhere | `sand` | weight 1 | Location 252 = not created. It had no object words, so its noun was taken from its name (noted). |

### Settings

| Setting | Value | From |
|---|---|---|
| Significant Letters | 5 | PAWS word matching |
| Darkness Variable | `f0` | PAWS flag 0; object 0 is the light source |
| Max Carried Items / Weight | 6 / 20 | the first ABILITY 6 20 (PAWS defaults are 4 / 10) |
| Auto List Items | on | the game used LISTOBJ |
| Text / background colour | green on black | the game's ink and paper |
| Legacy Table Semantics | on | always, for imports |

### The logic, entry by entry

Open **Triggers**. Ids are `t{table}_{entry}`, and each trigger's Notes say *PAWS process 0, entry 0* and so on.

**`GET LAMP  AT 0  CARRIED 0  MESSAGE 0  DONE`** becomes trigger **`t0_0`** *get lamp*:

| Field | Value |
|---|---|
| Event | BeforeCommand (Response is checked before the built-in verbs) |
| Verb / Noun | `get` / `lamp` |
| Group | `PRO0` (so a DONE here doesn't stop the other tables) |
| Stops Command | off (every matching entry runs, as in PAWS) |
| Conditions | **Player in** `r0`; **Item** `o0` **in** `@carried` |
| Actions | **Message** "You already hold the lamp."; **Done** |

**`OPEN BOX  PRESENT 2  ZERO 11  SET 11  MESSAGE 1  NOTZERO 12  MESSAGE 2  DONE`.** PAWS allows a condition (NOTZERO 12) *after* actions have run. A trigger checks all its conditions first, so the entry is **split into a chain**:
* `t0_1`: if `o2` is present and `f11` = 0, then set `f11`, print "The box creaks open." and stamp the hidden variable `chain_t0_1` with the turn number.
* `t0_1_1`: if `chain_t0_1` = `@turns` (part 1 ran this turn) and `f12` ≠ 0, then print "Something glints." and **Done**.

**`_ _  ADVERB QUIETLY  PREP IN  NOUN2 _  PROCESS 3`** becomes `t0_2`, with conditions **Adverb used** `quiet` (5 letters), **Preposition is** `in` and **Noun2 is** `-` (none). Its action is **Run trigger** `proc3`. PROCESS 3 became the **Subroutine** trigger `proc3`:

| PAWS | Studio action |
|---|---|
| ABILITY 6 20 | Set `f37` = 6, Set `f52` = 20 (the carrying-limit flags) |
| PLACE 1 255 | Move item `o1` to `@here` (255 = the current location) |
| GOTO 1 | Go to `r1` |
| OK | Ok |

**Other entries:**
* `GET _  ADJECT1 BRASS  AUTOG` becomes **Adjective used** `brass` → **Take item** `$noun1`.
* `N _  AT 0  ISAT 1 2  SYSMESS 7` becomes **Player in** `r0` and **Item** `o1` **in** `o2`, then prints system message 7, "You can't go that way."
* **Process 1** entries become **AfterDescribe** triggers (group `PRO1`). `MES 3  PRINT 30` becomes two Message actions: "Score: " and `{var:@score}`.
* **Process 2** entries become **EveryTurn** triggers (group `PRO2`):
  * EQ 38 1 → **Variable** `@room` = 1
  * PLUS 30 10 → add 10 to `@score`
  * COPYFF 30 100 → copy `@score` into `f100`

### Pictures

`p0` (*Location 0*) is 256×176 in **Spectrum attributes** mode:

```
Plot 10,155            ← PLOT 10,20 (PAWS measures y from the bottom; the Studio from the top)
Line 10,155 60,155     ← relative LINE +50,+0, made absolute
Line 60,155 60,165
Fill 65,160
Shade 57,163           ← SHADE with patterns 1|2
AttributeBlock 24,32 47,47   ← BLOCK at character column 3, row 4
SetInk 2
Call p1 @60,77 x4/8    ← GOSUB sc=4 → a half-size sub-picture
Line 68,165 78,165     ← continues from where the sub-picture left the pen
Text 40,16 "A"         ← TEXT in the ROM font
Plot 1,174             ← PLOT with OVER
```

A PLOT with OVER+INVERSE only moves the pen, so it produces no command. `p1` is marked **Is Subroutine**. Picture 2 was empty and isn't imported. Open any picture in the designer to see it, step through it with **Step view**, or touch it up.

### What the Notes list, and what to do

| Note | What it means for you |
|---|---|
| LISTOBJ has no action; Auto List Items is on instead | Objects are listed after every room description, which is the usual effect of LISTOBJ in Process 1. |
| MODE skipped | The Player has its own screen layout. Nothing to do. |
| TIMEOUT: entries testing it never fire | `t0_6` has the condition *not Always*, so it can't fire. Delete it, or rebuild the idea with a **Timer** trigger. |
| Flags 2–10 aren't counted down | PAWS decrements these automatically. This game does its own countdown for `f5` in Process 2, so nothing is lost. If a game relies on the automatic countdown, add an EveryTurn trigger: *if `f5` > 0, add −1 to `f5`*. |
| Objects without words got nouns from their names | Check `o4` *sand* in Items and add more nouns if needed. |
| Entries were split into chained triggers | Leave `t0_1` / `t0_1_1` and their `chain_` variable together. |

**PAWS limitations you may meet:** see the [full table in chapter 7](07-importing.md#limitations). The ones that most often need attention are skipped condacts (COPYOF, WEIGH, DOALL, PARSE, EXTERN…), TIMEOUT, NOTDONE acting like DONE, and custom text fonts.

---

## Walkthrough 2: a Quill game with Illustrator pictures

### The original

*The Dark Castle*: a later-version (C) Spectrum Quill game with three pictures from The Illustrator.

```
LOCATIONS  0  You are in a dark cellar. Stairs lead up.
           1  Rows of dusty bottles line walls|here. I'm in the kitchen.      (| = end of a 32-column line)
           2  the garden.  [INK 2]                                              ("the " is a compression token)
CONNECTIONS 0: UP 1     1: DOWN 0  N 2  PORT 0     2: S 1
OBJECTS    0  a brass lamp       location 0
           1  some gold coins    CARRIED
           2  the old map        NOT CREATED
EVENT (response) TABLE
  GET  LAMP   PRESENT 0          GET 0  OK
  GET  _                         AUTOG  DONE
  OPEN DOOR   AT 1  ZERO 11      MESSAGE 0  SET 11  PLUS 30 5  PLACE 2 1  LET 5 3  DONE
  SCOR _                         SCORE  DONE
  WEAR _      NOTWORN 0          AUTOW  OK
  _    _                         LET 28 12  PAUSE 0  INK 3  INVEN
STATUS (process) TABLE
  _    _      CARRIED 2  GT 11 0     MESSAGE 1  END          (message 1 = "You have won!")
```

### The import report

```
Imported with: The Quill / Illustrator (ZX Spectrum, Amstrad CPC, C64)
Format: Quill (ZX Spectrum, later version C) + Illustrator

3 rooms, 3 items, 8 triggers, 3 pictures.

Warnings:
• PAPER/INK/BORDER actions (screen colour changes) have no equivalent and were ignored.
```

The title, *The Dark Castle*, comes from the file name (`the_dark_castle.sna`). The Quill doesn't store one.

### Rooms, exits and text

| Room | Name | Notes |
|---|---|---|
| `r0` | *A dark cellar* | "You are in a dark cellar" is shortened to its noun phrase. |
| `r1` | *Rows of dusty bottles line walls here* | The author relied on "walls" ending exactly at column 32 to separate it from "here". The importer **restored the space**. |
| `r2` | *The garden* | The compression token was expanded, and the INK colour code was removed from the text. |

Exits: `r0` up; `r1` down, north and **`port`**. PORT isn't a direction, so it became a custom exit word, and the player can type PORT there. `r2` south.

### Items

| Item | Location | Nouns | Note |
|---|---|---|---|
| `o0` brass lamp | `r0` | `lamp` | the lamp is object 0, the Quill's light source |
| `o1` gold coins | carried | `coin`, `coins` | "some" split off the name |
| `o2` old map | nowhere | `map` | |

All three are **wearable**. The event table has `WEAR _ … AUTOW`, which can wear any object, so the importer can't rule any out. Untick **Wearable** on items that shouldn't be worn.

### Settings and messages

* **Significant Letters 4.** Words are stored as 4 letters (`NORT`, `SCOR`), so SCORE, SCORED and SCORING all match.
* **Max Carried Items 4**, from the database.
* **Colours:** yellow text on blue, from the game's INK 6 / PAPER 1 header.
* **Messages:** Quill's GET, DROP, WEAR and REMOVE print nothing on success, so **Taken**, **Dropped**, **Worn** and **Removed** are set to empty. The other system messages ("I can't go in that direction.", "I have with me:") replace the engine's own.

### The logic, entry by entry

Response entries become `resp0`–`resp5` (BeforeCommand), and the status table becomes `proc0` (EveryTurn).

| Quill entry | Studio trigger |
|---|---|
| `GET LAMP  PRESENT 0  GET 0  OK` | `resp0`: **Item present** `o0` → **Take item** `o0`, **Ok** |
| `GET _  AUTOG  DONE` | `resp1`: **Take item** `$noun1`, **Done** |
| `OPEN DOOR  AT 1  ZERO 11  MESSAGE 0  SET 11  PLUS 30 5  PLACE 2 1  LET 5 3  DONE` | `resp2`: **Player in** `r1`, `f11` = 0 → Message; Set `f11` = 255; **Award score** 5 (PLUS on flag 30, the score); **Move item** `o2` to `r1`; Set `f5` = 3; Done |
| `SCOR _  SCORE  DONE` | `resp3`: **Show score**, Done |
| `WEAR _  NOTWORN 0  AUTOW  OK` | `resp4`: **not Item worn** `o0` → **Wear item** `$noun1`, Ok |
| `_ _  LET 28 12  PAUSE 0  INK 3  INVEN` | `resp5`: Set `f28` = 12, **Restart** (PAUSE with flag 28 = 12 is the Quill's "restart" function), **Inventory**, Done. INK 3 was dropped (the warning above). |
| `_ _  CARRIED 2  GT 11 0  MESSAGE 1  END` | `proc0`: **Item** `o2` **in** `@carried`, `f11` > 0 → Message "You have won!", **Lose** "END OF GAME" |

Because `resp2` sets flag 5, and the Quill counts flags 2–10 down by itself, the importer added **`flagtimer5`**: every turn, if `f5` > 0, add −1. This imitates the original timer.

### Pictures

The Illustrator pictures belong to the location with the same number.

```
p0 (r0)  Plot 10,155
         Line 10,155 40,165      ← relative LINE +30,-10
         SetInk 2
         Fill 35,162
         Shade 36,161            ← pattern 55h as a 4×2 tile
         AttributeBlock 32,40 55,55
         Call p2 @36,161 x4/8    ← GOSUB 2 at scale 4; p2 is a pure "shape"
         Plot 41,157             ← RPLOT east, continuing from the shape's end
p1 (r1)  (no drawing commands – it only moved the pen)
p2       Is Subroutine: Line 0,0 8,-8
```

### What to fix after importing this game

1. **The ending.** The Quill's END just means *game over*; it has no idea of winning, so the importer uses **Lose**. Here the message just before it is "You have won!". Open `proc0` and change the **Lose** action to **Win**. (GAC imports guess this from the text; Quill imports don't.)
2. **Verb ids.** The SCORE command is called `scor` because that's how the Quill stored it. It works, because 4 letters are significant, but you can rename it in **Commands** and add the full word.
3. **Wearable items.** Untick Wearable on the lamp and the map (see Items above).
4. **`p1`** is empty. Draw the kitchen in the picture designer, or clear `r1`'s Picture id.

**Quill limitations you may meet:**
* the turn order: the Quill tries exits *before* the event table, and runs the status table before each input
* PLUS/MINUS don't stop at 0–255
* flag 31 holds only the low byte of the turn count
* screen-control PAUSE functions are ignored
* FLASH and BSHADE aren't reproduced
* Amstrad CPC and C64 support is experimental

See the [full table in chapter 7](07-importing.md#limitations-1).

---

## Walkthrough 3: a GAC game

### The original

A Spectrum GAC game with two rooms, three objects and conditions in all three tables. GAC writes its conditions as expressions. The Studio shows each one in this readable form in the trigger's name and Notes:

```
ROOMS      1  You are in a small hall. A door leads north.    NORTH → 2    picture 1
           2  You are in the garden.                           SOUTH → 1    picture 2
OBJECTS    1  a brass lamp   weight 5    room 1
           2  the key        weight 1    room 255 (carried)
           3  lever          weight 100  room 0 (nowhere)
VERBS      1 NORTH N   2 SOUTH S   3 GET TAKE   4 DROP   5 OPEN   6 PULL   7 PUSH   8 LOOK L
NOUNS      1 LAMP   2 KEY   3 DOOR   4 LEVER   255 IT          ADVERBS  1 QUICKLY
MESSAGES   1 The door creaks open.   2 Click!   240 What now?   241 You can't do that.   254 Okay.

HIGH PRIORITY
  IF ( RES? 5 ) SET 5 3 CSET 7 END
LOCAL CONDITIONS, room 1
  IF ( VERB 5 AND NOUN 3 ) MESS 1 SET 10 WAIT END
LOW PRIORITY
  IF ( VERB 6 OR VERB 7 ) MESS 2 END
  IF ( VERB 3 AND NOUN 1 AND HERE 1 ) GET 1 OKAY END
  IF ( NOT CARR 2 AND ( 3 < CTR 7 ) ) INCR 0 END
  IF ( VERB 1 ) MESS 2 WAIT END
  IF ( NO1 < 3 AND VERB 4 ) DROP NO1 OKAY END
  IF ( CTR 7 < CTR 8 ) MESS 1 END
```

### The import report

```
Imported with: Graphic Adventure Creator
Format: GAC (ZX Spectrum, sna48 snapshot)

2 rooms, 3 items, 10 triggers, 3 pictures.

Warnings:
• Pronoun words (it) refer to the previous noun (GAC noun 255); the engine's own pronoun handling is used.
• GAC fills spread up and down the seed's column only (not a true flood fill); pictures were converted
  to flood fills and may differ slightly.
• Skipped low priority condition "IF ( CTR 7 < CTR 8 ) MESS 1 END": comparing two computed values cannot be converted.
```

### Rooms, items and vocabulary

* **Rooms keep their GAC numbers:** `r1` and `r2` (GAC has no room 0), and the start room `r1` comes from the database. Exits: `r1` north → `r2`, `r2` south → `r1`.
* **Items:**

  | Item | Location | Noun | Why |
  |---|---|---|---|
  | `o1` brass lamp | `r1` | `lamp` | GAC doesn't link objects to nouns. The importer uses the noun whose word appears in the name. |
  | `o2` key | carried | `key` | room 255 = carried; "the" split off |
  | `o3` lever | nowhere | `lever` | room 0 = nowhere |

  Weights are kept. **Max Carried Weight** is 250, GAC's default strength.
* **Vocabulary:** all eight verbs become commands, and NORTH/N and SOUTH/S are also the directions for the exits. QUICKLY is an adverb. Noun 255 (IT) is left to the engine's own pronoun handling (the first warning).
* **Messages:** 240 becomes the **Prompt** ("What now? "), 241 becomes **Can't do that**, and 254 becomes **Ok** ("Okay.").
* **Settings:**
  * **Exits Before Triggers** is on, because GAC moves the player before looking at its tables.
  * **Significant Letters** is 0: GAC accepts any prefix of a word, so GE or GET both work.
  * Legacy Table Semantics is on.

### The logic, entry by entry

GAC's turn order is *high priority → connections → local conditions → low priority*. The triggers reproduce it with priorities: 30000 for high priority, 20000 for local conditions and 10000 downwards for low priority.

| GAC condition | Studio trigger(s) |
|---|---|
| `IF ( RES? 5 ) SET 5 3 CSET 7 END` (high) | **`hp1`** EveryTurn, priority 30000: `m5` = 0 → Set `m5` = 1; Set `c7` = 3. It doesn't test the command, so there's also **`hp1s`**, a **GameStart** copy, because GAC runs this table once before the first input. |
| `IF ( VERB 5 AND NOUN 3 ) MESS 1 SET 10 WAIT END` (local, room 1) | **`lc1_1`** BeforeCommand, **Room** `r1`, verb `open`, noun `door`: Message "The door creaks open."; Set `m10` = 1; **Done** (WAIT ends the turn). |
| `IF ( VERB 6 OR VERB 7 ) MESS 2 END` | **OR is split into two triggers:** `lp1a` (verb `pull`) and `lp1b` (verb `push`), each printing "Click!". |
| `IF ( VERB 3 AND NOUN 1 AND HERE 1 ) GET 1 OKAY END` | **`lp2`**: get `lamp`, **Item** `o1` **in** `@here` → Take item `o1`; Ok. |
| `IF ( NOT CARR 2 AND ( 3 < CTR 7 ) ) INCR 0 END` | **`lp3`**: **not Item carried** `o2`, **Variable** `c7` **>** 3 → **Award score** 1 (counter 0 is the score). |
| `IF ( VERB 1 ) MESS 2 WAIT END` | **`lp4`**, verb `north`, with an **extra condition, not Player in `r1`**. In room 1, NORTH is a real exit, so GAC would already have moved the player before reaching this line. The condition keeps that behaviour. |
| `IF ( NO1 < 3 AND VERB 4 ) DROP NO1 OKAY END` | **Expanded into one trigger per noun:** `lp5a` (drop `lamp` → Drop item `o1`) and `lp5b` (drop `key` → Drop item `o2`). |
| `IF ( CTR 7 < CTR 8 ) MESS 1 END` | **Skipped**, with a note (see below). |

Markers became `m5` and `m10` (0 or 1), and counters became `c7` and `c8`.

### Pictures

GAC pictures are 256×128, in Spectrum attribute mode. GAC measures y from the bottom, so coordinates are flipped:

```
p1 (r1)  SetInk 2
         Line 0,0 255,127        ← LINE 0,175 → 255,48
         Ellipse 108,54 148,74   ← ELLIPSE centre 128,111, radii 20×10, as a bounding box
         Rectangle 10,115 30,125
         Fill 10,5
         Shade 20,115
         Plot 1,1
         Call p3 @0,0 x8/8       ← CALL 3
p2 (r2)  SetPaper 1
         Line 0,75 10,75
p3       Is Subroutine: Plot 5,5
```

### What to fix after importing this game

1. **The skipped condition.** The engine can compare a variable with a number, but not two variables with each other, so `IF ( CTR 7 < CTR 8 ) MESS 1 END` wasn't imported. The original line is quoted in the report and the Notes. To rebuild it, track the comparison yourself: wherever the game changes `c7` or `c8`, set a marker variable to say which is larger. Then write a trigger that tests that marker and prints "The door creaks open." If the counters only take a few values, one trigger per combination also works.
2. **Fills.** GAC's fill spreads up and down one column at a time; the Studio uses a true flood fill. Compare the pictures with the original and adjust any that differ, using **Step view** in the picture designer.
3. **Endings.** A GAC EXIT becomes **Win** if the message before it reads like a victory, otherwise **Lose**. Check them. This game has none.
4. **Nouns.** Items only get nouns whose words appear in their names. If the player can't refer to an item, add nouns in **Items & People**.

**GAC limitations you may meet:**
* comparisons of two counters
* CONN outside local tables
* FIND, DESC or LIST of another room
* setting the score or turns directly
* marker 0 isn't maintained
* ink/paper 8 and 9, BORDER and FLASH are ignored
* Amstrad CPC games are unverified

See the [full table in chapter 7](07-importing.md#limitations-2).

---

## After any import

* The original structure is kept, so the game can be checked line by line against a listing from UnPAWS, UnQuill or reGAC.
* You can add anything the original couldn't do:
  * sound: room ambience, and the PlaySound action
  * NPCs and random events ([chapter 10](10-npcs-and-events.md))
  * hints and puzzles
  * smooth or animated pictures ([chapter 6](06-pictures-and-sound.md))

  Keep **Legacy Table Semantics** on, so the original logic keeps working.
* Save the result as a `.adventure` package, then export it like any other game ([chapter 8](08-exporting.md)). Only publish games you have the right to distribute.
