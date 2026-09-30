# 12. Z-code stories (Infocom and Inform)

Adventure Creator has a built-in **Z-machine**, the virtual machine used by Infocom's games (*Zork*, *Planetfall*,
*Trinity*…) and by games written with Inform 6, Inform 7 (Z-code builds), ZIL and Dialog. Z-code stories play
everywhere Adventure Creator games play: the Player on every platform, the Studio's Test Play, the console player,
and exported standalone apps.

## Supported files

| File | What it is |
|---|---|
| `.z1` – `.z5`, `.z7`, `.z8` | Z-code story files, versions 1 to 8 |
| `.z6` | Version 6 ("graphical") stories, with their pictures if they come in a Blorb file |
| `.zblorb`, `.zlb`, `.blb` | Blorb packages: the story plus pictures and cover art |
| `.dat` | Some Infocom distributions use this name for the story file; it is recognised by its contents |

Thousands of freely distributable Z-code games are on the IF Archive (<https://ifarchive.org/indexes/if-archive/games/zcode/>).
Only play and distribute stories you have the right to.

## Opening a story

* **Player:** *Open a game…* and choose the story file.
* **Studio:** **File › Import Game (PAWS, Quill, GAC, Z-code)…** (⇧⌘I). The story becomes a game document. Press
  **Test Play** (⌘R) to play it, save it as a `.adventure` package, and export it like any other game.
* **Console:** `adventure-player story.z5`

The game's title comes from the Blorb's metadata, or the file name. A Blorb's cover art becomes the intro picture.

## Playing

* The story's own text appears in the transcript. The Player shows its own `>` prompt.
* The **status bar** shows the story's status line: location, score and moves for version 1–3 stories, or the top line of the upper window for later versions.
* **UNDO** uses the story's own undo, and several levels are kept.
* **SAVE** and **RESTORE** use Adventure Creator's named save slots, with the usual Save / Load dialogs on desktop and phones. Positions are saved between turns. *Continue where you left off?* works too.
* Typing **QUIT** asks the story's own question, then ends the game.

## Version 6

Version 6 stories run on a virtual 640×400 screen with 8 windows:
* The window that asks for input is the main text.
* Text in the other windows (status bars, side panels) is shown in the status bar.
* Pictures drawn by the story (`draw_picture`) are shown in the picture area. Small pictures such as borders and icons are skipped. Pictures come from the Blorb file, so play Infocom's graphical games from their Blorb versions.

## Limitations

| Area | Limitation |
|---|---|
| Editing | Z-code is compiled, so its rooms, objects and logic can't be converted into editable Studio rooms and triggers. The story is played exactly as written. |
| Version 6 layout | Windows aren't drawn at their positions, so screen layouts (Zork Zero's map and borders, Journey's menus) become plain text. There are no mouse clicks and no menu bar (`make_menu`). |
| Styles and colours | Bold, italic, fixed-width text and colours are ignored. Beyond Zork's character-graphics font isn't drawn. |
| Timed input | Real-time input (Border Zone and some Inform games) waits for the player instead of timing out. |
| Sound | Only beeps. Sampled sounds in Blorb files aren't played. |
| Transcripts and command files | SCRIPT (transcripts) and command replay (`input_stream`) aren't supported. |
| Saves | Saves are Adventure Creator save slots, not Quetzal files, so they can't be moved to other interpreters. |

**Conformance:** the interpreter passes the standard test programs *CZECH* (all 406 tests, with output identical to the reference apart from interpreter-identity fields) and *Praxix* (all tests). Both run in Adventure Creator's automated tests.
