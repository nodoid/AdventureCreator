# Adventure Creator

A .NET 10 / .NET MAUI system for writing, importing and publishing text and graphic adventure games.

* **Adventure Creator Studio**: the editor, for **macOS**, **Windows** and **iPad**. It has a desktop-style menu bar, keyboard shortcuts and a sidebar, list and detail layout.
* **Adventure Player**: plays games on **Android**, **iPhone/iPad**, **macOS** and **Windows**. Exported games are copies of the Player with the game built in.
* **Console player**: a single-file terminal executable that runs any game, or a game appended to it.

## Documentation

The full **[User Guide](docs/README.md)** covers:
* getting started, and a step-by-step tutorial for creating an adventure
* a reference for rooms, items, triggers, the parser and custom commands
* pictures and sound
* **importing PAWS, Quill/Illustrator and GAC games, with their limitations**
* exporting standalone games, and the file format

The same guide is available in the Studio under **Help › User Guide…** (⇧⌘?).

## Solution layout

| Project | What it is |
|---|---|
| `src/AdventureCreator.Core` | Game model, parser, engine, picture renderer, packaging, exporters and example games. No UI dependencies. |
| `src/AdventureCreator.Importers` | Importers for PAWS, The Quill (+ Illustrator) and Graphic Adventure Creator. |
| `src/AdventureCreator.Maui` | Shared MAUI UI: `GamePlayerView`, audio (Plugin.Maui.Audio), picture display, light theme. |
| `src/AdventureCreator.Studio` | The Studio app (Mac Catalyst, Windows, iPadOS). |
| `src/AdventureCreator.Player` | The Player app (Android, iOS, Mac Catalyst, Windows). It embeds a game when built with `-p:EmbeddedGame=…`. |
| `src/AdventureCreator.ConsolePlayer` | `adventure-player`, the console player and debugging tool. |
| `tests/AdventureCreator.Tests` | xUnit tests: parser, engine, walkthroughs, graphics, packaging and all three importers. |
| `examples/` | `Genesis.adventure` (the main test game) and `TheLighthouse.adventure`. |
| `build/build-templates.sh` | Builds the player templates used for SDK-free exports. |

## Features

### Language parser
* Grammar lines with direct and indirect objects: `put {multiheld} in|into {noun2} => insert`.
* Phrasal verbs, split or together: *pick up the lamp* / *pick the lamp up*.
* **Adverbs** anywhere in the sentence (*quietly open the door*, *open the door quietly*). Unknown words ending in *-ly* are treated as adverbs.
* **Adjectives** for choosing between items. If a phrase is still ambiguous, the parser asks *"Which do you mean…?"*.
* Multi-word nouns, plurals, **ALL / EXCEPT**, ordinals (*second key*), numbers and quoted text.
* Pronouns: IT, THEM, HIM, HER.
* Several commands on one line: *take lamp and go north then light it*.
* Giving orders: *robot, go north* or *tell the robot to go north*.
* Idioms (*where am I*) and filler (*I want to…*, *please*) are handled.
* Spelling correction, OOPS, AGAIN, UNDO.
* PAWS/Quill-style significant-letter word matching.
* **New commands**: authors add verbs with their own words, grammar and default responses. Reusing a built-in id extends that verb. The Studio's Commands section has a *Parser lab* that shows how any sentence is understood.

### World model and engine
* Rooms, exits (with doors, conditions, hidden exits and travel messages), and items.
* Item types: containers, supporters, openable/lockable items, keys, light sources, wearables, food and drink, readable items.
* Characters with conversation topics. Darkness.
* **Triggers** fire on events:
  * before or after a command
  * every turn
  * timers
  * entering or leaving a room
  * taking or dropping an item
  * solving a puzzle
  * after a room description
  * subroutines and unhandled input
* Triggers can match verb, noun, adverb, preposition, room or addressed character. They have 30 condition types and about 50 action types.
* **Puzzles** solve themselves when their conditions are met. They award points and give progressive hints.
* **Score**, turns, save/restore, undo, restart, and verbose/brief modes.
* All built-in messages can be overridden.

### Graphics
* Vector pictures: lines, rectangles, ellipses, polygons, freehand strokes, flood fill, pattern shading, text, sub-picture calls, bitmap stamps and Spectrum attribute blocks.
* Rendering is done by a platform-independent software renderer. It has a full-colour mode and an authentic ZX Spectrum attribute mode (colour clash included) for imported games.
* The Studio's picture editor draws directly onto the same renderer. It can import PNG, JPEG or HEIC images as backgrounds or stamps.

### Audio
* Rooms can have looping ambient sound. The `PlaySound` and `StopSound` actions control audio from triggers.
* WAV, MP3 and M4A play on every platform.

### Importing legacy games
*File › Import PAWS / Quill / GAC Game…* accepts `.sna`, `.z80` (48K and 128K), `.tap` and `.tzx` files.

| System | Status |
|---|---|
| **PAWS** (Spectrum 48K/128K) | Vocabulary, messages, objects, connections, all process tables and the full condact set, plus pictures. Tested on 186 Zenobi PAWS games. |
| **The Quill** versions A and C, with **The Illustrator** pictures | Verified against UnQuill output on several archive.org releases. |
| Quill for CPC and C64 | Experimental. |
| **Graphic Adventure Creator** (Spectrum) | Verified with the manual's example database from the original GAC tape. |
| GAC for CPC | Implemented from the reGAC documentation; not yet verified. |

Every import produces a report. Anything that couldn't be converted exactly is listed in the game's Notes.

### Standalone games
*File › Export Standalone Game…* offers four outputs:

1. **Game package** (`.adventure`): opens in any Adventure Player.
2. **Native app** (Android APK, iOS, macOS, Windows): runs `dotnet publish` on the Player project with the game embedded. This needs the .NET SDK and the MAUI workload.
3. **Desktop app from a template**: copies a prebuilt Player and places the game inside it. The macOS app is re-signed ad hoc. No SDK is needed.
4. **Console executable**: a single self-contained file with the game appended.

To create the templates for options 3 and 4, run `build/build-templates.sh`. It writes them to `artifacts/templates/`.

## Building

```bash
dotnet test tests/AdventureCreator.Tests                                  # 129 tests
dotnet build src/AdventureCreator.Studio -f net10.0-maccatalyst           # Studio for macOS
dotnet build src/AdventureCreator.Player -f net10.0-android               # Player APK
dotnet run --project src/AdventureCreator.ConsolePlayer -- --example      # play Genesis in the terminal
dotnet run --project src/AdventureCreator.ConsolePlayer -- --parse example "quietly open the door"
```

Windows targets are added automatically when you build on Windows.

## Code signing (team TEAMID)

| App | Configuration | Identity | Profile | Bundle id |
|---|---|---|---|---|
| Player, iOS | Debug | Apple Development | `player-dev-profile` | `com.adventurecreator.player` |
| Player, iOS | Release | Apple Distribution | `player-release-profile` | `com.adventurecreator.player` |
| Player, Mac | Debug | Apple Development | `player-dev-mac-profile` | `maccatalyst.com.adventurecreator.player` |
| Studio, Mac | Release (Mac App Store, sandboxed) | Apple Distribution | `studio-release-mac-profile` | `maccatalyst.com.adventurecreator.studio` |

The profiles must be installed in `~/Library/MobileDevice/Provisioning Profiles`.

Games exported with their own bundle id (`-p:GameId=…`) are not signed with these profiles.

Still to do:
* There is no `studio-dev-profile` profile yet, so Debug builds of the Studio are unsigned.
* The Player has no Mac *release* profile.
* The Studio has no iPad profile.
* Uploading the Mac App Store Studio also needs a *Mac Installer Distribution* certificate, which is not in the keychain.
* The sandboxed App Store Studio cannot run the .NET SDK. Export options 2 to 4 are therefore disabled there. They work in the Debug or Developer-ID builds.

## Example games

* **Genesis** is the main test adventure: a fan game inspired by *Doctor Who: Genesis of the Daleks* (1975). The Time Lords send you to Skaro to stop the creation of the Daleks. It includes:
  * poison gas and a gas mask
  * a guard who wants papers
  * a sentry you must pass *quietly*
  * a keycard, and a friend to rescue
  * a scientist who gives you explosives
  * Davros's tapes
  * a two-step moral choice in the incubator room
  * a deadline

  All text is original. *Doctor Who*, the Daleks, Davros and related names belong to the BBC. This game is for personal and testing use only. The Studio and Player therefore offer it only in Debug builds; Release (store) builds show only The Lighthouse.
* **The Lighthouse** is an original, freely distributable example. It covers light and darkness, an adverb puzzle on a rusted stair, custom commands (POLISH, SIGNAL), and hints.
