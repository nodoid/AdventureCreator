# 2. Creating an adventure — tutorial

This chapter builds a short but complete game, **The Quiet House**, and uses most of the Studio's features along the way:
* rooms and exits
* a container with something hidden in it
* a locked door and its key
* a dark room and a light source
* a character you can talk to
* a custom command that reacts to an adverb
* a puzzle with hints and a winning ending
* a picture and a sound

Allow about 30 minutes.

> Tip: keep **Test Play** (⌘R) handy. You can switch to it at any time; **↻ Restart with latest edits** picks up your changes.

---

## Step 1 — Start a new game

1. **File › New Adventure** (⌘N). The new game has one room, `room1`.
2. In **Game**, set:
   * **Title**: `The Quiet House`
   * **Author**: your name
   * **Introduction**: `Your great-aunt's house has stood empty for years. Tonight, for some reason, you have come back.`
3. Save with **File › Save As…** (⌥⇧⌘S), for example `QuietHouse.adventure`.

## Step 2 — Rooms

Open **Rooms** (⌘3). Select `room1` and change:
* **Id**: `hall`
* **Name**: `Hall`
* **Description**: `A dusty hall. A staircase climbs into darkness, a heavy door leads north and the garden door stands open to the east.`

Go back to **Game** and set **Start Room id** to `hall` with the **…** button.

Add three more rooms with **+**:

| Id | Name | Description | Is Dark |
|---|---|---|---|
| `garden` | Garden | An overgrown garden, silver with frost. A cracked plant pot sits by the wall. | no |
| `study` | Study | Book-lined walls, a cold fireplace and a writing desk. | no |
| `attic` | Attic | Rafters, cobwebs and the smell of old paper. | **yes** |

> **Ids** are how everything refers to a room or item. Use short, lower-case, unique words. The **Name** is what the player sees.

## Step 3 — Exits

Select **Hall**. Under **Exits**, click **+ Add** three times and fill in each row:

| Direction | Target Room id | Door Item id |
|---|---|---|
| north | study | *(leave empty for now)* |
| east | garden | |
| up | attic | |

Then click **Add return exits from destination rooms**. This adds `south` in the Study, `west` in the Garden and `down` in the Attic, all leading back to the Hall.

Open **Map** (⌘2) to see the result. The start room has a gold border; dark rooms are shaded.

Directions can be `north south east west northeast northwest southeast southwest up down in out`, or any word of your own (for example `portal`). The player then types *portal* or *go portal*.

## Step 4 — Items

Open **Items & People** (⌘4) and add:

**The plant pot** (a container that is already open, so its contents can be seen):
* Id `pot`, Name `plant pot`, Nouns `pot, plant pot`, Adjectives `cracked, clay`.
* Location `garden`.
* Portable **off**, Scenery **on** (it is part of the room description).
* Container **on**, Openable **off**, Is Open **on**.
* Description `A cracked clay pot full of frozen soil.`

**The key**, inside the pot:
* Id `key`, Name `iron key`, Nouns `key`, Adjectives `iron, small`.
* Location `pot` (choose *In: plant pot* from the **…** list).

**The study door**:
* Id `studydoor`, Name `study door`, Nouns `door`, Adjectives `study, heavy, oak`.
* Location `hall`. Portable off, Scenery on.
* Openable on, Is Open **off**, Lockable on, Is Locked **on**, Key Item id `key`.

Now go back to **Rooms › Hall** and set the north exit's **Door Item id** to `studydoor`. The exit is blocked while the door is closed.

**A candle** (a light source for the dark attic):
* Id `candle`, Name `candle`, Nouns `candle`, Adjectives `wax, white`, Location `study`.
* Light Source on, Is Lit off.
* Room Description `A single white candle stands on the desk.`

**A locket**, hidden in the attic:
* Id `locket`, Name `silver locket`, Nouns `locket`, Adjectives `silver, tarnished`, Location `attic`.
* Description `Inside is a faded photograph of a young woman, and a lock of hair.`
* Score On Take `5`.

**Nouns** are the words players can use for an item; multi-word nouns such as `plant pot` work. **Adjectives** tell similar items apart (*take the iron key*). If you leave Nouns empty, the last word of the Name is used.

Try it: **Test Play**, then *e*, *look in the pot*, *take key*, *w*, *unlock door with key*, *open it*, *n*, *take candle*, *light it*. The built-in commands already handle containers, locks, light and darkness.

## Step 5 — A character

Add an item:
* Id `ghost`, Name `ghost`, Article `the`, Nouns `ghost, woman, spirit, aunt`, Adjectives `pale, grey`.
* Location `study`. **Is Character on**, Portable off.
* Description `A pale, grey woman, drifting a little above the floor. She looks terribly sad.`
* Room Description `A pale ghost hovers by the fireplace.`

Under **Topics**, add:

| Keywords | Response |
|---|---|
| `hello, talk` | `"You came back," she whispers. "I lost something, long ago. Up there, in the dark."` |
| `locket, photograph, lost` | `"It was mine. Please... bring it to me."` |
| `attic, dark` | `"The attic. Take a light."` |

Players can type *talk to the ghost*, *ask her about the locket* or *say "hello"*.

## Step 6 — A custom command and an adverb

Players will want to comfort the ghost. Open **Commands** (⌘9) and add a command:
* **Id** `comfort`.
* **Words** `comfort, console, soothe, reassure`.
* **Grammar** (one line each):
  ```
  * {person}
  * {person} with {held2}
  ```
* **Default Response** `You murmur a few kind words. Nothing seems to change.`

Try the **Parser lab** at the bottom of the Commands page: type `gently comfort the pale ghost`. It shows `comfort [gently] obj1=the pale ghost`.

Now make the adverb matter. Open **Triggers** (⌘6) and add a trigger:
* **Name** `Comfort gently`, **Event** `BeforeCommand`.
* **Verb** `comfort`, **Noun1** `ghost`, **Adverb** `gently|softly|kindly|tenderly`.
* **Actions**: `Message`, Text `The ghost's face softens. "Thank you," she breathes. "You were always kind."`

Players who add *gently* (anywhere in the sentence) get this response; anyone else gets the default.

## Step 7 — The puzzle, scoring and the ending

1. **Variables** (⌘8): add `rested`, Initial Value `0`.
2. **Triggers**: add
   * **Name** `Give the locket`, **Event** `BeforeCommand`, **Verb** `give|show`, **Noun1** `locket`, **Noun2** `ghost`.
   * **Conditions**: `ItemCarried` A = `locket`.
   * **Actions**:
     1. `Message` — `The ghost takes the locket in hands that are almost not there. For a moment she is young again, smiling. Then she is simply gone.`
     2. `DestroyItem` A = `ghost`
     3. `DestroyItem` A = `locket`
     4. `SetVar` A = `rested`, N = `1`
     5. `PlaySound` A = *(your sound, see step 8)*
3. **Puzzles**: add
   * **Id** `pz_rest`, **Name** `Lay the ghost to rest`, **Points** `20`.
   * **Solved When**: `VarEquals` A = `rested`, N = `1`.
   * **Hints** (one per line): `The ghost lost something.`, `It's somewhere dark.`, `Light the candle, go up to the attic, and give the locket to the ghost.`
   * **Hint Room ids** `study, attic` (leave empty to offer the hints anywhere).
4. The ending: add a trigger
   * **Name** `The house is quiet`, **Event** `PuzzleSolved`, **Subject** `pz_rest`.
   * **Actions**: `Win`, Text `The house sighs, and settles, and is quiet at last.`

The puzzle solves itself at the end of the turn in which `rested` becomes 1. It then awards its 20 points and fires the PuzzleSolved trigger, which wins the game.

The **maximum score** is worked out automatically from:
* puzzle points
* item Score On Take
* room Score On First Visit
* AwardScore actions

You can override it in **Game › Settings › Max Score**.

## Step 8 — A picture and a sound

1. **Pictures** (⌘… or View › Pictures): add a picture. Its id is set for you (`pic1`); rename it `pic_study`.
2. Draw it. For example:
   * pick dark brown in the palette (right) and click **Use as background**
   * choose **▬ Box** and drag a desk
   * choose **⬤ Disc** and drag a pale grey ghost
   * choose **▨ Fill** and click inside shapes to flood them with the ink colour
   * choose **↖ Select**, click the ghost and drag it into place, or pick another colour to recolour it
3. In **Rooms › Study**, set **Picture id** to `pic_study`.
4. **Sounds**: click **+** and choose an audio file (WAV, MP3 or M4A). Give it the id `snd_sigh` and preview it with **▶ Play**.
5. Use it in the *Give the locket* trigger's PlaySound action. To play a sound on a loop while the player is in a room, set that room's **Sound id** instead.

See [chapter 6](06-pictures-and-sound.md) for all drawing tools.

## Step 9 — Test, validate, save

1. **Adventure › Validate** (⌘K) lists broken references, unreachable rooms, locked doors with no key and similar problems.
2. **Test Play** (⌘R). Play the whole game:
   ```
   e. look in pot. take key. w
   unlock the door with the key then open it
   n. talk to the ghost. ask her about the locket
   take the candle and light it
   s. u. take the locket. d. n
   gently comfort the ghost
   give the locket to the ghost
   ```
   The **Watch** panel shows the room, score, carried items, variables, solved puzzles and fired triggers after every move. **Run commands…** runs a semicolon-separated list for quick retesting.
3. **File › Save** (⌘S).

## Step 10 — Share it

**File › Export Standalone Game…** (⇧⌘E) saves a `.adventure` package for any Adventure Player, or builds a standalone app. See [chapter 8](08-exporting.md).

---

## Design tips

* **Describe things players will try to use.** If a noun appears in a description, make it an item (Scenery on) with its own Description, or players get *"You can't see any such thing."*
* **Prefer items and properties to triggers.** Doors, keys, containers, light and wearables work without any triggers.
* **Use triggers for the exceptions**, and use `Continue` when you want to add a message but still let the built-in behaviour happen (*"The door creaks."* + the normal open).
* **Give every puzzle hints.** Players type HINT; each request reveals the next hint for an unsolved puzzle in the current room.
* **Use Item › Verb Responses for one-liners.** They give a quick custom reply without writing a trigger (key = command id, e.g. `smell` → `It smells of lavender.`).
