# 7. Importing games from other adventure systems

The Studio can convert games written with other adventure systems into fully editable Adventure System games.

**Classic 8-bit systems:**

| System | Publisher, year | Machines supported here |
|---|---|---|
| **PAWS** (Professional Adventure Writing System) | Gilsoft, 1986 | ZX Spectrum 48K and 128K |
| **The Quill**, with pictures from **The Illustrator** | Gilsoft, 1983–85 | ZX Spectrum (versions A and C); Amstrad CPC and Commodore 64 *(experimental)* |
| **GAC** (Graphic Adventure Creator) | Incentive Software, 1985 | ZX Spectrum; Amstrad CPC *(unverified)* |

**Open-source adventure creators** (see [sections 7–9](#7-scott-adams-format-scottkit-and-scottfree)):

| System | Files | Graphics |
|---|---|---|
| **Scott Adams format**: games compiled with the open-source *ScottKit*, played by *ScottFree*, and Scott Adams' own classics | `.dat`, `.sao` | none (the format has none) |
| **Quest 5** (textadventures.co.uk, MIT licence) | `.aslx` source, `.quest` packages | room pictures and `picture` commands, from the package or the `.aslx` file's folder |
| **Twine 2 and 1** (Harlowe, SugarCube, Chapbook, Snowman) | published `.html`, Twee `.twee` / `.tw` | `<img>` and `[img[…]]`, embedded or next to the story |

The result is an ordinary game:
* rooms, items, vocabulary, messages and pictures
* the original logic tables, turned into triggers

You can play it straight away, edit it, add sound and new puzzles, and export it to any platform.

Only import games you have the right to use: your own, public-domain or freely distributable titles, or for private study.

**Z-code stories** (Infocom and Inform, `.z3`, `.z5`, `.z8`, `.zblorb`…) can be imported the same way, but they aren't converted: they play on the built-in Z-machine. See [chapter 12](12-z-code.md).

For a worked example of each system, showing an original listing, the import report and exactly what every entry becomes in the Studio, see **[chapter 11, Import walkthroughs](11-import-walkthroughs.md)**.

---

## 1. Getting a file to import

The importers read **memory snapshots**: a copy of the machine's memory taken by an emulator while the game is running. GAC can also read **tape images**.

| System | Accepted files |
|---|---|
| PAWS | `.sna` and `.z80` snapshots, 48K or 128K (all versions of the .z80 format) |
| Quill (+ Illustrator) | Spectrum `.sna` / `.z80`; Amstrad CPC `.sna` (CPCEMU "MV - SNA" format); Commodore 64 VICE snapshots `.vsf` |
| GAC | Spectrum `.sna` / `.z80`; `.tap` / `.tzx` tape images (unprotected compiled games, or data files saved from the GAC editor); Amstrad CPC `.sna` |

### Making a snapshot

1. Load the game in a Spectrum emulator (for example Fuse, ZEsarUX, Spectaculator or Retro Virtual Machine). Use the machine the game was made for: 48K or 128K.
2. Wait until the game has **fully loaded** and shows its **first prompt**, *before typing anything*. For 128K PAWS games, make sure every part has loaded.
3. Use the emulator's *Save snapshot* command and choose `.z80` (preferred) or `.sna`.

Snapshots taken later in a game import the *original* database. PAWS, the Quill and GAC keep their starting positions separately from the current state, so it doesn't matter where you are when you save. Taking the snapshot at the first prompt is simply the safest option.

**Tape images (GAC only).** A `.tap` or `.tzx` works when the game data is stored in ordinary, unencrypted blocks. That covers:
* the GAC editor's own *save data* files
* unprotected compiled games

If a tape uses a protected loader, load it in an emulator and take a snapshot instead. When a tape holds several GAC data files (the original GAC tape has *QS*, *ADVINMAN* and *RANSOM*), the importer picks the largest one whose text can be decoded, and lists the others in the report.

## 2. Importing

1. In the Studio choose **File › Import Game…** (⇧⌘I). If the current game has unsaved changes you'll be asked to save it first.
2. Pick the snapshot or tape. The Studio works out which system made it: PAWS is tried first, then the Quill, then GAC.
3. The **Import report** shows:
   * the system and version detected
   * how many rooms, items, triggers and pictures were imported
   * any **warnings**: things that couldn't be converted exactly
4. The game opens as a new, unsaved document. Look at **Game › Notes** for the full list of approximations.
5. Run **Adventure › Validate** (⌘K), then **Test Play** (⌘R). Save it with **File › Save As…** as a `.adventure` file.

If the file isn't recognised you'll see *"This file doesn't contain a PAWS, Quill … or Graphic Adventure Creator game that I can find."* See [troubleshooting](#troubleshooting).

## 3. What you get

### Common to all three systems

| Original | Becomes |
|---|---|
| Locations | Rooms `r0`, `r1`, … Names come from the first sentence of each description. |
| Objects | Items `o0`, `o1`, … with starting locations: *carried*, *worn*, *not created* (nowhere) or a room. Articles ("a", "the") are split off the names. |
| Vocabulary | Verbs become commands (id = the first synonym); nouns, adjectives, adverbs and prepositions go into the Vocabulary. Words that shared a number in the original stay synonyms. |
| Movement table / connections | Exits. Words that are obviously directions (N, NORTH, U, UP…) become standard directions; others (e.g. CLIMB) become custom exit words. |
| Messages / system messages | Message actions, and **Messages** overrides for the engine's own replies (e.g. "I can't go in that direction."). All system messages are also kept as `sys0`, `sys1`, … |
| Flags / markers / counters | Variables (`f0`–`f255` for PAWS/Quill, `m…` markers and `c…` counters for GAC). Well-known system flags are mapped to `@score`, `@turns`, `@room` and `@carried`. |
| Condition/action tables | Triggers, one per table entry. See below. |
| Pictures | Pictures in **Spectrum attribute** mode (authentic colour clash), attached to their rooms. Sub-pictures become **Is Subroutine** pictures drawn with Call commands. |
| Colours | The game's ink and paper colours become the Player's text and background colours. |

### How imported games behave

Imported games have **Legacy Table Semantics** switched on (Game › Settings). This makes the engine behave like the original interpreters:

* Every table entry that matches runs in order, just as the original scanned its table. Triggers have *Stops Command* off, and a **Done** action (DONE, OK, WAIT…) ends the scan.
* If any entry ran, the command counts as handled, so the built-in verbs don't add their own replies.
* The **Group** field keeps process tables separate: a DONE in one table doesn't stop another.
* Darkness follows the original's darkness flag (**Darkness Variable**).
* Word matching uses the original's significant letters: PAWS 5, the Quill 4. GAC matches any typed prefix of a stored word.

You can freely add modern triggers, commands, puzzles, hints and sound to an imported game. Leave Legacy Table Semantics on, so that the original logic keeps working.

---

## 4. PAWS

### What is converted

* **Databases:** 48K and 128K. In 128K games, the data spread across RAM pages 0, 1, 3, 4, 6 and 7 is gathered automatically. Compressed and uncompressed text are both supported.
* **Objects:** weights, and the *container* and *wearable* attributes. Nouns and adjectives come from the object-word table. Objects without object words get nouns and adjectives taken from their names.
* **Containers:** objects located "at" another object's number are placed inside that item.
* **Process tables:**

  | PAWS table | Becomes |
  |---|---|
  | Response (table 0) | BeforeCommand triggers `t0_…` |
  | Process 1 | **AfterDescribe** triggers, group `PRO1` (PAWS runs it after describing a location) |
  | Process 2 | EveryTurn triggers, group `PRO2` |
  | Tables called with PROCESS *n* | Subroutine triggers named `proc{n}`. **RunTrigger** calls them. |

* **Mixed entries:** PAWS entries can have a condition *after* an action. These are split into chained triggers (`t{table}_{entry}_{k}`), linked by a hidden `chain_…` variable.
* **Condacts:** all 108 condacts are recognised; everything with an equivalent is converted. For example:
  * AT, PRESENT, CARRIED, ZERO, EQ, CHANCE, ADJECT1, ADVERB, PREP, NOUN2 → conditions
  * GET, DROP, WEAR, REMOVE, CREATE, DESTROY, SWAP, PLACE, PUTO → item actions
  * AUTOG / AUTOD / AUTOW / AUTOR / AUTOP / AUTOT → the same actions on `$noun1`
  * SET, CLEAR, LET, PLUS, MINUS, COPYFF, RANDOM → variable actions
  * MESSAGE / MES / SYSMESS / PRINT / NEWLINE → Message actions
  * GOTO, DESC, DONE, OK, END, QUIT, SAVE, LOAD, RAMSAVE, RAMLOAD, SCORE, TURNS, INVEN, CLS, ANYKEY, PAUSE, BEEP, PROCESS, PICTURE, DROPALL → their equivalents. PICTURE draws over the picture already on screen, as PAWS does (many games draw a frame round each location picture this way).
* **Flags:** 0 = darkness (`f0`, with object 0 as the light source), 1 → `@carried`, 30 → `@score`, 31 → `@turns`, 38 → `@room`. All others become `f{n}` variables, with the system flags described in **Variables**.
* **Carrying limits:** the first ABILITY in the game (or the PAWS defaults of 4 objects and weight 10) sets **Max Carried Items / Weight**.
* **Pictures:** every location picture, with PLOT, LINE, FILL, SHADE (including inverse patterns), BLOCK, GOSUB with scale, INK, PAPER (including 8, transparent, and 9, contrast), BRIGHT, OVER and INVERSE.
  * TEXT in the ROM font becomes a Text command.
  * Text using the game's own character sets or UDGs is drawn pixel by pixel from the snapshot.
* **Text in the game's own character set** is shown in the engine's font. Characters that set redraws as lines or blocks (for example `$` drawn as a bar, printed in a row as a rule) are shown as line characters such as ─ ━ ▁ █, and turned back into the game's own characters when you export it.
* **Settings:**
  * Significant Letters 5, Auto List Exits off, Spelling Correction off.
  * Auto List Items is on only if the game used LISTOBJ.
  * The prompt text is taken from the game's system message.

### Limitations

| Area | Limitation |
|---|---|
| Skipped condacts | COPYOF, COPYFO, COPYOO, WEIGH, WEIGHT, ADD, SUB, DOALL, LISTAT, WHATO, PARSE, NEWTEXT, RESET, EXTERN (machine code). Each use is listed in the Notes. |
| Screen control | MODE, LINE, PROTECT, PRINTAT, SAVEAT, BACKAT, INPUT, PROMPT, GRAPHIC, CHARSET, INK, PAPER, BORDER and TIME are skipped. The Player has its own layout. |
| TIMEOUT, MOVE | Input time-outs and flag-driven characters walking the map aren't modelled. Entries that *test* TIMEOUT or use MOVE never fire. |
| Approximations | NOTDONE acts like DONE. ADJECT2 is treated as "adjective used" for either noun. PUTO uses the first noun (PAWS uses the last referenced object). RAMSAVE/RAMLOAD use normal save slots. |
| Flags 2–10 | PAWS counts these down automatically; here they are ordinary variables. |
| Flag 1 | Writes to it are dropped; the engine counts carried objects itself. |
| "Current object" system | Flags 51 and 54–57 aren't maintained. |
| Pictures | BRIGHT 8 and FLASH are ignored. Pictures a game draws with machine code (EXTERN), such as character portraits, are not in the picture data and don't appear. |
| Fonts | Custom character sets for game *text* aren't used; the Player uses its own font. |
| Not supported | CP/M PAWS, tape images (use a snapshot), and games whose database has been deliberately scrambled. |

Verification: tested on 186 freely distributable PAWS games from the Zenobi archive (48K and 128K). Every game imported without errors, and pictures rendered recognisably.

---

## 5. The Quill and The Illustrator

### What is converted

* **Versions:** Spectrum Quill *version A* (early, database at 6D04h) and *version C* (later, 6B85h). If neither address holds a valid database, the whole snapshot is searched. A candidate is accepted only if all its tables check out, so PAWS games are never mistaken for Quill games.
* **Text:** decoded, including the later versions' dictionary compression.
  * The Quill laid text out for a 32-column screen, and authors often relied on a word ending at the right-hand edge to "provide" the space before the next word. The importer puts those spaces back.
  * Runs of padding spaces are collapsed.
* **Objects:**
  * Later versions: nouns come from the object-word table.
  * Early versions have no such table, so nouns are guessed from the object text.
  * All Quill objects are portable. Objects that the tables WEAR or REMOVE are made wearable.
* **Tables:**

  | Quill table | Becomes |
  |---|---|
  | Response ("event") table | BeforeCommand triggers `resp0`, `resp1`, … |
  | Process ("status") table | EveryTurn triggers `proc0`, … The Quill runs this whole table every turn; its word labels are only reminders, so they are ignored. |

* **Condacts:** all conditions and actions, with version-specific numbering (the early version and the CPC/C64 numbering are converted to one common set).
* **Flags:**
  * 0 = darkness, with object 0 as the light source
  * 1 → `@carried`
  * 30 → `@score` (PLUS/MINUS 30 become AwardScore)
  * 31 → `@turns`
  * 35 → `@room` (LET 35 → GoTo)
  * **Flags 2–10** count down automatically in the Quill. When a game uses them, extra `flagtimer…` triggers imitate this: flags 5–10 every turn, flags 2–4 on entering a room.
* **Special PAUSE functions:** restart, quit and clear screen are converted.
* **Messages:** Quill's GET/DROP/WEAR/REMOVE succeed silently, so the engine's *Taken*, *Dropped*, *Worn* and *Removed* messages are set to print nothing.
* **Settings:**
  * Significant Letters 4, Auto List Exits off.
  * Max Carried Items comes from the database.
  * Text and background colours come from the header.

### The Illustrator's pictures

* Supported for **later Spectrum Quill games** (the Illustrator's control block at FAB8h). Picture *n* belongs to location *n*.
* Every drawing operation is converted: absolute and relative moves, lines, PLOT, FILL, SHADE, BLOCK, INK, PAPER, BRIGHT, OVER and INVERSE. The meanings were confirmed by disassembling the Illustrator's own drawing routines.
  * SHADE uses its pattern byte as a 4×2-pixel tile, repeated to 8×8.
  * Coordinates are flipped from the Spectrum's bottom-up system.
* GOSUB to a pure "shape" picture (one with no absolute commands) becomes a scaled Call. Other GOSUBs are drawn inline, which is exact. After a GOSUB, drawing continues from where the sub-picture finished, as on the Spectrum.

### Limitations

| Area | Limitation |
|---|---|
| Machines | Amstrad CPC and C64 support is **experimental and untested**. It assumes the standard database addresses (CPC 1BD1h, C64 0804h) and has no Illustrator pictures. Compressed CPC v3 snapshots and 128K Spectrum Quill games aren't supported. |
| Files | Snapshots only (no `.tap`/`.tzx`). |
| Turn order | The Quill tries a location's exits *before* the response table and runs the status table before each input (including once at the start). The engine tries triggers first and runs EveryTurn after the command. Games rarely depend on this, but it is noted. |
| Arithmetic | PLUS/MINUS clamp flags to 0–255 in the Quill; the imported AddVar actions don't. |
| Turns | Flag 31 is only the low byte of the turn counter, so tests of it differ after 255 turns. |
| Screen control | PAPER/INK/BORDER actions, and PAUSE functions for sound, fonts, keyboard click, screen effects, pictures on/off (19) and RAM save (21), are ignored. PAUSE functions 10 and 11 (carrying limits) aren't supported at run time. |
| Pictures | FLASH is ignored. BSHADE is imported as a normal SHADE. Flag 29 (when to draw pictures) isn't modelled, and pictures are always 256×176 (the Illustrator's split-screen height isn't used). |
| Early version | No object-word table: nouns are guessed from object descriptions and may need correcting in **Items**. |

Verification: compared against John Elliott's UnQuill on *Very Big Cave Adventure*, *Bored of the Rings*, *Lost in Time*, *Moreby Jewels* and *Subsunk*. Counts, texts, tables and system messages all matched, and pictures rendered recognisably.

---

## 6. GAC — Graphic Adventure Creator

### What is converted

* **Rooms:** descriptions and pictures. GAC room numbers are kept (`r{n}`), and the start room comes from the database.
* **Objects:** names, weights and starting rooms. GAC doesn't link objects to nouns, so each item gets the noun groups whose words appear in its name (falling back to the noun with the same number).
* **Vocabulary:** verbs, nouns and adverbs, with their synonyms. Movement verbs become standard directions.
* **Messages:** GAC's word-token text compression is decoded. Messages that run on into object names or numbers keep that behaviour.
* **Condition tables:** GAC's turn order is reproduced:

  | GAC | Becomes |
  |---|---|
  | High priority conditions (every turn, before input) | EveryTurn triggers, highest priority. Those that don't test the command also get a GameStart copy, because GAC runs this table once before the first input. |
  | Connections | Exits, tried **before** the other tables (**Exits Before Triggers** is on), as GAC moves the player first |
  | Local conditions (per room) | BeforeCommand triggers restricted to that room |
  | Low priority conditions | BeforeCommand triggers, lower priority |

* **GAC's condition expressions:**
  * GAC stores conditions as full boolean expressions. They are converted to *disjunctive normal form*: AND becomes a list of conditions, NOT becomes *not*, and each OR / XOR branch becomes its own trigger.
  * Care is taken that an action can't run twice when several branches are true.
  * `NO1 < k` style tests are expanded into one trigger per noun.
* **Variables:**
  * markers → `m{n}` (0/1; marker 1, "light", starts at 1)
  * counters → `c{n}`
  * counter 0 is the score (`@score`; additions become AwardScore)
  * TURN and counters 126/127 → `@turns`
  * ROOM → `@room` when it only names the room (`ROOM = 5` becomes **PlayerIn**). When a game compares ROOM with < or > or uses it as a number, it becomes the variable `gac_room`, kept equal to the GAC room number by a `gac_room_n` Before Enter Room trigger for each room. (GAC rooms can be numbered up to 9999, which is too many to match by position.) A room you add in the Studio has no such trigger, so give it one if the game compares ROOM there.
  * Comparisons of two values (`CTR 3 < CTR 4`, `CTR 7 + 2 > TURN`, `ROOM = CTR 5`) become **VarLessVar**, **VarGreaterVar** or **VarEqualsVar** conditions, with any added number kept as the offset.
* **Darkness:** GAC is dark when markers 1 and 2 are both clear. This is kept in a `gac_dark` variable (the Darkness Variable) and updated after every change to those markers.
* **Actions:**
  * MESS, LOOK, GET, DROP, TO, SWAP, GOTO → their equivalents
  * SET/RESE, CSET/INC/DEC → variable actions
  * OKAY, WAIT, LIST, PICT, PRIN, WAIT/PAUSE → their equivalents
  * EXIT / END / QUIT
* **Pictures:**
  * Spectrum: 256×128 pictures in attribute mode, with lines, ellipses, rectangles, plots, fills, shading, INK and PAPER, and sub-picture calls (Call).
  * CPC: full-colour pictures using the CPC palette.
* **Settings:**
  * Auto List Exits off, Auto List Items on.
  * Max Carried Weight comes from the game's strength (STRE; default 250).
  * Prompt comes from message 240.
  * Spelling Correction off.

### Limitations

| Area | Limitation |
|---|---|
| Machines | Amstrad CPC snapshots are implemented from published documentation but **not verified** against a real game. CPC `.dsk` disk images, Commodore 64 and BBC Micro GAC aren't supported. |
| Protected tapes | Encrypted or custom loaders can't be read from `.tap`/`.tzx`; use a snapshot. |
| Unconvertible conditions | Comparisons of a random number (RAND), a noun number (NO1/NO2) or the verb number (VBNO) with another computed value, and CONN outside a room's local table, are **skipped**. The original line is quoted in the Notes so you can rewrite it as a trigger. (Comparing two counters, TURN or ROOM with each other *is* converted.) |
| Actions without equivalents | FIND (go to where an object is), DESC of another room, LIST of another room's objects, setting the score to a fixed value, changing the turn counter, and TEXT (pictures off). Each is noted. |
| EXIT | Becomes **Win** if the message just before it looks like a victory text, otherwise **Lose**. Check the ending triggers. |
| Marker 0 | "A room was just described" isn't maintained. |
| Pictures | GAC's fill spreads up and down one column at a time; here it is a true flood fill, so a few pictures may look slightly different. Ink/paper values 8 and 9, BORDER and FLASH are ignored. |
| Garbled text | If a game's word dictionary can't be decoded (unusual or protected formats), the report warns "The word dictionary could not be decoded". Vocabulary and text will then be wrong. |

Verification: the example game from the GAC manual (*ADVINMAN*, from the original GAC editor tape) decodes completely: rooms, objects, verbs, nouns, all messages, every condition line, and a 199-command picture that matches the original byte for byte. *RANSOM* on the same tape is structurally correct, but its tape image is damaged after about address C600h, so its text doesn't decode. Compiled-game snapshots are supported, but were only tested with synthetic data.

---

## 7. Scott Adams format (ScottKit and ScottFree)

The `.dat` format of Scott Adams' 1978–84 adventures is still used by the open-source **ScottKit** compiler (which turns a readable `.sck` source into `.dat`/`.sao`) and played by **ScottFree**. Compile ScottKit games first, then import the result.

### What is converted

| Scott Adams | Becomes |
|---|---|
| Rooms 1…n (room 0 is the store room) | Rooms `r1`…. Descriptions starting with `*` are shown as they are; others get "I'm in a …", as ScottFree does. Names drop the "I'm in" part. |
| Exits N S E W U D | Exits |
| Items (with `/WORD/` auto-get words) | Items `o0`…; items with an auto-get word are portable, the others can only be moved by actions. Items marked `*…*` are treasures. Item 9 is the light source. |
| Words (with `*` synonyms) | Commands and nouns. GET and DROP (words 10 and 18), and words that abbreviate a built-in verb (LOO, INV, SCO…), extend the built-in verbs, so the usual behaviour still applies when no action does. Trigger patterns list every synonym, so CLIMB TREE works where CLIMB is a synonym of GO. |
| The action table | Triggers `a0`…: command actions stop after the first match, as in ScottFree; automatic actions (verb 0) run every turn with their percentage chance, and once before the first command; CONTINUE runs the following entries. |
| Conditions (all 20) | Item, room, flag and counter conditions |
| Actions (all except 80, 87 and 89) | Messages, GET/DROP, GOTO, moving items, flags, darkness (flag 15), death (to the last room), game over, LOOK, SCORE, INVENTORY, lamp refill, SAVE, swaps, counters and the current counter, printing nouns |
| SCORE | Counts the treasures in the treasure room; storing them all wins |
| Light time | The lamp burns down each turn, warns when dim and goes out at zero |

Settings: Significant Letters = the game's word length; the carrying limit; "Tell me what to do?" prompt; ScottFree's messages ("O.K.", "I can't go in that direction.").

### Limitations

| Area | Limitation |
|---|---|
| Actions 80 and 87 | Swapping with a saved room (used by a few games for time travel) isn't converted. |
| Action 75 | PUT item WITH item is approximated: the item goes to the other item's starting place. |
| GET | GET in actions doesn't check the carrying limit. |
| Moving in the dark | ScottFree's "I fell down and broke my neck" when moving in the dark isn't reproduced. |
| Pictures | The format has no pictures (SAGA and TI-99 graphics are separate files). |

## 8. Quest 5

**Quest** (by Alex Warren and others; open source under the MIT licence) is the system behind textadventures.co.uk. Games are XML: `.aslx` source files, or `.quest` packages (a zip with the game and its pictures and sounds). Both import. Published `.quest` files include Quest's whole standard library; it is recognised and skipped.

### What is converted

* **Rooms** (alias, description, dark, **picture**), and **exits** with directions or custom words. Locked exits are blocked by a variable that `UnlockExit` clears, with the lock message.
* **Objects**: name and alias, alternative names (`alt`), look text, take, scenery, containers (open/closed), surfaces, wearables, switchables, light sources, edible and readable objects, characters. Objects inside the player start carried.
* **Verbs** on objects: text responses become the item's responses; scripts become triggers.
* **Commands** (`pattern`, including regular-expression patterns): new commands with grammar lines. A command that starts with a built-in verb (put, wear…) extends it.
* **Turn scripts**, **timers** (every *n* turns instead of *n* seconds), the game's **start** script, objects' `_initialise_`, and rooms' **enter**, **beforeenter**, **firstenter**, **beforefirstenter** and **onexit** scripts.
* **Scripts**, statement by statement:
  * `msg`, `MoveObject`, `obj.parent = …`, `player.parent = …`, `AddToInventory`, `RemoveObject`, `MoveObjectHere`
  * attributes and flags (`obj.attr = true/5`, `+ 1`, `SetObjectFlagOn`) as variables; `visible`, `isopen`, `switchedon` and `worn` as the matching item states
  * `IncreaseScore`, `game.score`, `finish` (a win, unless the last message talks of losing or dying)
  * `picture`, `play sound`, `UnlockExit`/`LockExit`, `HelperOpenObject`, `SwitchOn`, enabling turn scripts and timers
  * **`if` / `else if` / `else`** and **`switch`**: each branch becomes a subroutine trigger, and a guard variable makes sure only the first true branch runs
  * **`firsttime` / `otherwise`**
  * conditions: `Got`, `obj.parent =`, `player.parent =`, `GetBoolean`, attribute comparisons, `ListContains(ScopeVisible(), …)`, `IsSwitchedOn`, `RandomChance`, with `and`, `or` and `not`. Simple boolean functions (`return (condition)`) are used inline.
  * text built up in a local variable (`s = s + "…"`) is printed piece by piece
* **Text processor** commands: formatting is removed; `{object:…}` and `{command:…}` show their text; conditional and random text keep the first choice (noted).
* **Gamebooks** (Quest's choice-based mode): pages become rooms and options become numbered choices, as for Twine.

### Limitations

| Area | Limitation |
|---|---|
| Functions | Calls to the game's own functions (other than inline conditions), loops (`foreach`, `for`, `while`), `wait`, `ShowMenu`, `do`, and change handlers (`=>`) aren't converted. Each is quoted in the trigger's Notes. |
| Commands | A command whose script can't be fully converted is dropped if the engine has a built-in verb of that name, so the built-in behaviour is used. |
| Text | Conditional text processor commands (`{either}`, `{if}`, `{random}`, `{notfirst}`) keep only their first choice. |
| Lists and strings | String attributes, lists and dictionaries aren't converted. |
| Timers | Timers count turns, not seconds. |
| Conversations | ConvLib dialogue pages are skipped. |
| Presentation | Colours are imported; fonts, the command bar, panes, hyperlinks and the map aren't. |
| Pictures | Large photographs are kept at full size in the game package (shown scaled down). |

## 9. Twine

**Twine** (by Chris Klimas; open source under the GPL) writes choice-based stories. Import a **published story** (`.html`, from Twine 2's *Publish to File* or Twine 1's *Build*) or **Twee** source (`.twee`, `.tw`).

### What is converted

* **Passages** become rooms; the start passage is the start room.
* **Links**, `[[text->target]]`, `[[target<-text]]`, `[[text|target]]`, `[[target]]`, Harlowe `(link-goto:)` and `(link:)[(goto:)]`, SugarCube `<<link>>`, become **numbered choices**: the passage lists them as "1. …", "2. …", and the player types the number. The link text stays in the prose.
* **Variables**: Harlowe `(set: $x to 5)`, `(set: $x to it + 1)`, `(put:)`, SugarCube `<<set $x to 5>>`, `+=`, `-=`, with numbers and true/false. They are set as the passage is entered, before its text is shown. `StoryInit` (SugarCube) and `startup` passages (Harlowe) run at the start. `$x` in the text shows the value.
* **Conditional text**: Harlowe `(if:)[…](else-if:)[…](else:)[…]` and `(unless:)`, SugarCube `<<if>>…<<elseif>>…<<else>>…<</if>>`, with `is`, `is not`, `>`, `<`, `>=`, `<=`, `gt`, `lt`, `eq`, `and`, `or`, `not`, and visited passages. The text (and any choices in it) appears after the passage, only when the condition holds.
* **Jumps**: `(goto:)` and `<<goto>>` move straight on without showing the passage.
* **Images**: the first `<img src>` or `[img[…]]` in a passage becomes its room picture, from embedded `data:` images or files next to the story.
* **Formatting** (`''bold''`, `//italic//`, HTML tags) is removed.

### Limitations

| Area | Limitation |
|---|---|
| Choices | At most 20 choices per passage. Links inside nested conditions aren't converted. |
| Macros | Other macros (`(either:)`, `(cycling-link:)`, `(click:)`, `<<textbox>>`, `<<audio>>`, widgets, Snowman JavaScript, Chapbook inserts…) are skipped and counted in the report. |
| Variables | Only numbers and true/false; strings, arrays and datamaps aren't converted. |
| Layout | Conditional text is shown after the passage's other text rather than in the middle of it. |
| Images | Web addresses (`http…`) can't be fetched; only PNG, JPEG and GIF images are used. |
| Styles and scripts | Story stylesheets and JavaScript are ignored. |

## Exporting back

An imported game can be saved in the format it came from with *File › Export Game File…*, or in Adventure System's own format. For PAWS, Quill and GAC, only the parts you changed are rebuilt. See [chapter 8](08-exporting.md#exporting-the-game-file--file--export-game-file-x).

## Troubleshooting

| Problem | What to try |
|---|---|
| *Not recognised* | Make sure the snapshot was taken **after the game finished loading**, on the right machine (48K vs 128K). For GAC tapes, try a snapshot instead. Games using a heavily modified or protected interpreter can't be read. |
| Text has odd spacing or glued words | For Quill games, check the room in **Rooms** and fix it by hand; the 32-column repair is heuristic. |
| An item can't be referred to | Add nouns to the item in **Items** (common with early Quill games and GAC). |
| A puzzle doesn't work | Check **Game › Notes** for skipped condacts or conditions in the trigger concerned (trigger ids show the original table and entry). |
| Wrong ending (GAC) | Check the Win/Lose actions converted from EXIT. |
| Pictures slightly different | Expected for GAC fills and PAWS/Quill FLASH. You can touch them up in the picture editor. |
| The game is dark everywhere | Look at the Darkness Variable (Game › Settings) and object `o0` (the Quill/PAWS light source). |

## Reading the converted logic

Imported triggers keep the original structure, so you can compare them with a listing from UnQuill, UnPAWS or reGAC:

* **Names** show the original verb and noun words, or the table and entry (e.g. `PRO2 #14`).
* **Notes** give the table and entry number.
* **Ids** encode the table: `resp12` / `proc3` (Quill), `t0_45` (PAWS table 0, entry 45).

**Help › Triggers, Conditions & Actions** explains every condition and action used.
