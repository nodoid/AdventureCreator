# 1. Getting started

## The three programs

| Program | Platforms | Use it to |
|---|---|---|
| **Adventure Creator Studio** | macOS, Windows, iPad | Write, import, test and export games |
| **Adventure Player** | Android, iPhone, iPad, macOS, Windows | Play games; exported games are copies of the Player with one game built in |
| **adventure-player** (console) | macOS, Windows, Linux | Play games in a terminal; debug the parser; export tiny standalone text games |

## Building from source

You need the .NET 10 SDK with the MAUI workload (`dotnet workload install maui`). For Apple platforms you also need Xcode.

```bash
dotnet test tests/AdventureCreator.Tests                                 # run the test suite
dotnet build src/AdventureCreator.Studio -f net10.0-maccatalyst          # Studio for macOS
dotnet build src/AdventureCreator.Studio -f net10.0-windows10.0.19041.0  # Studio for Windows (on Windows)
dotnet build src/AdventureCreator.Player -f net10.0-android              # Player for Android (.apk)
dotnet build src/AdventureCreator.Player -f net10.0-ios                  # Player for iPhone/iPad
dotnet run --project src/AdventureCreator.ConsolePlayer -- --lighthouse  # play an example in the terminal
```

The built apps are under each project's `bin/` folder, for example
`src/AdventureCreator.Studio/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Adventure Creator Studio.app`.

## A tour of the Studio

The Studio window has three panes, like other desktop editors:

1. **Sidebar** (left). Choose what to edit:
   * **Game**: title, author, introduction, start room and settings.
   * **Map**: a map drawn automatically from the exits. Click a room to edit it.
   * **Rooms**, **Items & People** (including NPC behaviour), **Puzzles**, **Triggers**, **Random Events**, **Variables**.
   * **Commands**: your own verbs.
   * **Vocabulary**: extra words.
   * **Pictures**, **Sounds**.
   * **Messages**: the engine's built-in replies.
   * **Test Play**: play the game inside the Studio.
2. **List** (middle, for sections that hold many things). Use **+** to add, **⧉** to duplicate and **−** to delete. The filter box searches names and ids.
3. **Editor** (right). Changes take effect immediately; the window title shows *Edited* until you save.

The **status bar** at the bottom shows the file location and counts of rooms, items, triggers and puzzles, plus the maximum possible score.

### Fields that refer to other things

Fields that hold an id (a room, item, picture, variable, verb…) have a **…** button. It lists valid choices.
Some fields also accept patterns: for example, a trigger's Verb can be `take|get`, and item fields can be `$noun1`.

### Menus and shortcuts (Studio)

| Menu | Command | Shortcut |
|---|---|---|
| File | New Adventure | ⌘N |
| | Open… | ⌘O |
| | Open Recent ▸ | |
| | Open Example: The Lighthouse (and Genesis in development builds) | |
| | Save | ⌘S |
| | Save As… | ⌥⇧⌘S |
| | Import Game… | ⇧⌘I |
| | Export Standalone Game… | ⇧⌘E |
| Edit | New Room / Item / Trigger / Puzzle / Random Event | ⌥⌘R / ⌥⌘I / ⌥⌘T / ⌥⌘P / ⌥⌘E |
| | New Picture, New Command | |
| | Duplicate Selected | ⌘D |
| | Delete Selected… | |
| View | Game, Map, Rooms, … | ⌘1 … ⌘9 |
| Adventure | Test Play | ⌘R |
| | Validate | ⌘K |
| | Show Map | ⇧⌘M |
| Play | Previous / Next Command (in Test Play) | ⌥⌘↑ / ⌥⌘↓ |
| Help | Parser & Command Reference, Triggers, Conditions & Actions, About | |

On iPad, the same menus appear in the iPadOS menu bar or keyboard shortcut overlay (hold ⌘ with a keyboard attached).

## The Player

When the Player has a game built in, it opens straight into it with a **title screen**: the game's picture, title and author. It disappears when tapped or after a few seconds. Otherwise it offers **Open a game…** (any `.adventure` or `.json` file) and the example game.

The Player shows:
* a status bar with the location, score, turns (and health, if the game uses it), and a **☰** game menu
* the current picture
* the transcript
* a command box

On phones, a row of shortcut buttons (N S E W U D, Look, Inv) appears above the command box. The keyboard only appears when you tap the command box, so it never hides the game.

**Paged text (*more*).** Each turn's text starts at the top of the text area. When there's more than fits, a ***more*** button appears at the bottom: tap it, or press Enter with an empty command box, to see the next page. You can always scroll back through earlier text. Authors can turn this off with **Game › Settings › Paged Output**.

**Quitting.** QUIT (typed, from the menu, or ☰ › Quit) asks *"Are you sure?"*, saves your position, and closes the app on every platform. In the Studio's Test Play it shows a *Thanks for playing* screen instead.

| Menu (desktop) | Command | Shortcut |
|---|---|---|
| Game | Open Game… (only when no game is built in) | ⌘O |
| | Save Game… / Load Game… | ⌘S / ⌥⌘L |
| | Restart | ⇧⌘R |
| | Undo Move | ⌥⌘Z |
| | Quit Game… | |
| Commands | Previous / Next Command | ⌥⌘↑ / ⌥⌘↓ (on Windows also plain ↑ / ↓) |
| | Look, Inventory, Hint, Score | ⇧⌘L, ⇧⌘I, ⇧⌘H, — |
| Sound | Mute / Unmute | ⇧⌘M |

### Saving and loading games

Every game can be saved and loaded, on every platform.

* **Save Game…** (menu, the ☰ button, or typing **SAVE**) asks for a name, suggesting the current location and turn. Saving under an existing name asks before replacing it.
* **Load Game…** (menu, ☰, or typing **RESTORE** / **LOAD**) lists the saved games, newest first, with location, score, turn and date. It also offers **Delete a saved game…**.
* Players can type a name directly: **SAVE castle**, **RESTORE "before the troll"**.
* **Autosave**: the Player saves automatically after every move. Next time the game is opened it offers **Continue where you left off?**, showing the location, turn and score. The autosave is removed when a game ends.
* In the console player, SAVE without a name uses a default slot. RESTORE lists the saves when there are several.

Saves are stored per game in the app's data folder (see [chapter 9](09-file-format.md#save-games)).

## The console player

```text
adventure-player <game.adventure|game.json>     play a game file
adventure-player --lighthouse                    play The Lighthouse
adventure-player --example                       play Genesis (the development test game)
adventure-player --write-example <path> [lighthouse]
adventure-player --parse <game|example|lighthouse> "quietly open the door"
adventure-player --script <game> commands.txt    run commands from a file (one per line)
```

Run with no arguments, a console player that has a game appended to it (see chapter 8) plays that game.
`--parse` prints exactly how the parser understood a sentence. This is the quickest way to debug a grammar line.
