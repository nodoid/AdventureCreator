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
   * **Rooms**, **Items & People**, **Puzzles**, **Triggers**, **Variables**.
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
| | Import PAWS / Quill / GAC Game… | ⇧⌘I |
| | Export Standalone Game… | ⇧⌘E |
| Edit | New Room / Item / Trigger / Puzzle | ⌥⌘R / ⌥⌘I / ⌥⌘T / ⌥⌘P |
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

When the Player has a game built in, it opens straight into it. Otherwise it offers **Open a game…** (any `.adventure` or `.json` file) and the example game.

The Player shows:
* a status bar with the location, score and turns
* the current picture
* the transcript
* a command box

On phones, a row of shortcut buttons (N S E W U D, Look, Inv) appears above the command box.

| Menu (desktop) | Command | Shortcut |
|---|---|---|
| Game | Open Game… (only when no game is built in) | ⌘O |
| | Save Position / Restore Position | ⌘S / ⌥⌘L |
| | Restart | ⇧⌘R |
| | Undo Move | ⌥⌘Z |
| Commands | Previous / Next Command | ⌥⌘↑ / ⌥⌘↓ (on Windows also plain ↑ / ↓) |
| | Look, Inventory, Hint, Score | ⇧⌘L, ⇧⌘I, ⇧⌘H, — |
| Sound | Mute / Unmute | ⇧⌘M |

Saved positions are stored per game in the app's data folder.

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
