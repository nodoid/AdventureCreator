# 8. Testing, exporting and publishing

## Testing

### Test Play (⌘R)

Test Play runs a copy of the game inside the Studio, so edits don't disturb a session in progress. **↻ Restart with latest edits** reloads your changes.

The **Watch** panel updates after every move. It shows:
* current room, score, turns and darkness
* carried items
* all variables
* solved puzzles
* every trigger that has fired

**Save position… / Load position…** keep named test saves (stored separately from players' saves), so you can jump back to a tricky point.

**Run commands…** takes a semicolon-separated list (`n; take lamp; light it; e`) and plays it. This is handy for replaying a walkthrough after each change.

**Previous / Next Command** (⌥⌘↑ / ⌥⌘↓) recall earlier commands.

### Validate (⌘K)

Validate checks the whole game and lists problems in three levels:

| Level | Examples |
|---|---|
| ⛔ Errors | Missing start room; duplicate ids; an id used for both a room and an item; exits, doors, keys, pictures, sounds or items that don't exist; conditions and actions naming missing things; broken grammar lines; missing image or audio files |
| ⚠️ Warnings | Rooms without descriptions; locked items with no key; triggers with no actions; puzzles that can never be solved; undeclared variables; duplicate exit directions |
| ℹ️ Info | Rooms that no exit or GoTo leads to |

Export warns you if there are errors.

### Automated tests (for developers)

`AdventureCreator.Core` can run a game without any UI:

```csharp
var engine = new GameEngine(AdventurePackage.Load("mygame.adventure"), randomSeed: 1);
engine.Start();
var result = engine.Submit("take the lamp and go north");
Assert.Contains("Taken.", result.Text);
Assert.Equal("cellar", engine.State.CurrentRoomId);
Assert.True(engine.Submit("give the locket to the ghost").Won);   // TurnResult.Won / .GameOver
```

The test project includes complete walkthrough tests for both example games and for the tutorial game from chapter 2. Use them as templates.

## Saving

Games are saved as **`.adventure` packages**: a single file containing the game and all its pictures and sounds (see [chapter 9](09-file-format.md)). The Studio can also open plain `.json` game files.

## Exporting the game file — File › Export Game File… (⇧⌘X)

This saves the game as a single file, in one of these formats:

* **The format it was imported from.** This is listed first, marked *original format*. A PAWS game goes back to a `.sna` or `.z80` snapshot, a Quest game to `.aslx`, and so on.
* **Adventure Creator** (`.adventure`). This is the same as *File › Save As…*.
* **Another system's format**, for any system that can hold the game (Quest, Twine, Scott Adams, Z-code…).

The same choices are at the top of the *Export Standalone Game…* window, under **1. Game file**.

After exporting, a report gives a summary and lists anything the format couldn't hold, with a count. Things the target system has no place for are always reported, never dropped silently. Examples are a sound in a Scott Adams game, or a timer in PAWS.

### Back to the original format

| Imported from | Exported as | How |
|---|---|---|
| **PAWS** (`.sna`, `.z80`, 48K and 128K) | the same snapshot format | Only what you changed is rebuilt (see below). An unchanged game comes out byte-for-byte identical. |
| **The Quill** (`.sna`, `.z80`) | the same snapshot format | As for PAWS. Illustrator pictures are kept. |
| **GAC** (`.sna`, `.z80`, `.tap`, `.tzx`) | the same file type | As for PAWS. Tape blocks get correct lengths and checksums. |
| **Scott Adams** (`.dat`) | a ScottFree / ScottKit `.dat` | The game is compiled into Scott Adams' tables. |
| **Quest 5** (`.aslx`, `.quest`) | `.aslx`, or `.quest` when it has pictures or sounds | Rooms, objects, exits, verbs and commands; triggers become Quest scripts. Gamebooks stay gamebooks. |
| **Twine** (`.html`, `.twee`) | Twine 2 archive, or Twee 3 if it came from Twee | Keeps the story format (Harlowe or SugarCube). |
| **Z-code** (`.z1`–`.z8`, `.zblorb`) | the story file, or a Blorb with its pictures | The story is stored unchanged. |

**How the 8-bit exports work.** The Studio keeps a copy of the original file inside the game package, so this works after saving and reopening too. When you export, the original is imported again and compared with your game:

* **Unchanged** locations, objects, texts, words and table entries keep their original bytes. This includes table entries with condacts the importer couldn't translate.
* **Edited and new** things are compiled into the system's own form: PAWS condacts, Quill table entries or GAC condition lines, messages, vocabulary and connections. New commands get new words, and new variables get free flags.
* **Deleted** locations and objects keep their numbers but are left empty, because other entries refer to them by number.

The database is then laid out in the memory it came from. For PAWS it is laid out exactly as the PAWS editor does it, including 128K RAM pages. If your changes no longer fit, the export stops with *"The game is N bytes too big…"*.

If a Quill game doesn't fit, its longest texts may be moved into memory the original left empty. The report says so, so check that game in an emulator. Edited or new pictures can't be written back to the 8-bit formats; the original pictures are kept and the report says so.

Exported PAWS, Quill and GAC games have been checked in the Fuse emulator. An edited PAWS game and an edited Quill game both showed their new descriptions, renamed objects and new commands when played in the original interpreters.

**Things that don't translate.** A few things have no place in some systems:

* Room names: PAWS, Quill and GAC take the name from the description.
* Game-start and room-entry triggers: PAWS runs these in Process 1, after the location is described.
* Winning: PAWS and The Quill only have END.

## Exporting — File › Export Standalone Game… (⇧⌘E)

| Option | Produces | Needs | Platforms |
|---|---|---|---|
| **1. Game file** | `MyGame.adventure`, or the game in its original or another system's format | nothing | The `.adventure` package opens in the Adventure Player on any platform (*Open a game…*), and in the console player |
| **2. Native app** | An app with the game built in | .NET SDK + MAUI workload (+ Xcode and signing for Apple) | Android `.apk`, iOS/iPadOS, macOS `.app`, Windows `.exe` |
| **3. Desktop app from a template** | A copy of the Player app with the game inside, named after the game | a prebuilt template | macOS, Windows |
| **4. Console executable** | One self-contained text-only program | a prebuilt template | macOS, Windows, Linux |

Options 2–4 need the desktop Studio running outside the Mac App Store sandbox. In the App Store version (and on iPad) only option 1 is available.

### Option 2: native apps with the .NET SDK

The launch screen of an exported game shows the game's own picture, title and author on its background colour. The exporter generates it. Android 12 and later only show the app icon while launching, but the Player's title screen still shows the name.

1. **Player project**: the path to `src/AdventureCreator.Player/AdventureCreator.Player.csproj`. It is found automatically when the Studio runs from a source checkout.
2. Choose the **Target** and click **Build with .NET SDK…**, then choose an output folder. The targets are:
   * **Android**: an `.apk`
   * **iOS**: a signed `.ipa` for devices
   * **iOSSimulator**: an `.app` for the iOS Simulator; no signing needed
   * **MacCatalyst**: a macOS `.app`
   * **Windows**: a folder with an `.exe`
3. The log shows the build output. The finished app is copied to `<folder>/<Game Title>-<Target>/<Game Title>.apk|.ipa|.app`.

### Trying an export in the iOS Simulator

Build with the **iOSSimulator** target. The Studio can then install and launch the game in a simulator:
* it boots an iPhone simulator
* it opens the simulator window: **DeviceHub** in Xcode 27 (which replaced the Simulator app), or **Simulator** in older Xcode
* it installs and starts the game

To do the same by hand:

```bash
xcrun simctl boot "iPhone 16 Pro"
open /Applications/Xcode.app/Contents/Applications/DeviceHub.app     # Xcode 27; older Xcode: open -a Simulator
xcrun simctl install booted "My Game.app"
xcrun simctl launch booted com.adventurecreator.game.mygame
```

### Trying an export on Android

Start an emulator (Android Studio › Device Manager, or `emulator -avd <name>`), then:

```bash
adb install -r "My Game.apk"
adb shell monkey -p com.adventurecreator.game.mygame -c android.intent.category.LAUNCHER 1
```

If the install fails with `INSTALL_FAILED_INSUFFICIENT_STORAGE`, the emulator is full. Create a separate test emulator with a bigger data partition rather than deleting apps.

Behind the scenes the Studio:
* saves the game as `game.adventure`
* runs `dotnet publish -f <framework> -c Release -p:EmbeddedGame=<package> -p:GameTitle="<title>" -p:GameId=com.adventurecreator.game.<name>`

The Player then includes the game as an app asset and opens straight into it, with the game's title as the app name.

You can run the same command yourself in a terminal or a CI pipeline:

```bash
dotnet publish src/AdventureCreator.Player -f net10.0-android -c Release \
  -p:EmbeddedGame=$PWD/MyGame.adventure -p:GameTitle="My Game" -p:GameId=com.example.mygame \
  -p:AndroidPackageFormat=apk -o out/android
```

Platform notes:
* **Android**: produces an `.apk`, signed with the debug key by default. For the Play Store, add your keystore (`-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=…`) and build an `.aab` (`-p:AndroidPackageFormat=aab`).
* **iOS/iPadOS**: needs a Mac with Xcode, and a signing identity plus provisioning profile for the game's bundle id. Pass `-p:CodesignKey=… -p:CodesignProvision=…`.
* **macOS**: produces an `.app`. Sign it for distribution with your Developer ID, or the App Store profile for the game's bundle id.
* **Windows**: must be built on Windows. Produces a self-contained folder with an `.exe`.

### Options 3 and 4: templates

Build the templates once with:

```bash
build/build-templates.sh           # all console players + the graphical player for this OS
RIDS=osx-arm64 build/build-templates.sh   # only some console players
```

On Windows, use PowerShell:

```powershell
build\build-templates.ps1                         # Windows graphical player + all console players
build\build-templates.ps1 -Rids win-x64,win-arm64 # only some console players
```

This creates:
* `artifacts/templates/console/<rid>/adventure-player[.exe]`: console players for `osx-arm64`, `osx-x64`, `win-x64` and `linux-x64`
* `artifacts/templates/Adventure Player.app` on macOS, or `artifacts/templates/windows/` on Windows

In the export window, choose the template once with **Browse…**; the Studio remembers it.

* **Desktop app from a template** copies the Player and puts `game.adventure` inside it.
  * On macOS it goes in `Contents/Resources`; the bundle's display name is set to the game's title and the app is re-signed ad hoc.
  * On Windows it goes next to the `.exe`, which is also copied under the game's name (with its `.pri` resource index, which WinUI needs).
* **Console executable** appends the game package to the player program. The result (about 35 MB, self-contained) runs with no installation: `./MyGame`. Saved positions go to the user's application-data folder.

Ad-hoc-signed Mac apps run on your own Mac. To give them to other people, sign them with a Developer ID and notarise them, or macOS Gatekeeper will block them.

## Code signing of the Studio and Player themselves

Signing details are kept out of the repository, in a local file.
* Without it, the apps use the ids `com.adventurecreator.player` / `com.adventurecreator.studio` and build unsigned. That is fine for simulators, emulators, Android, Windows and local Mac builds.
* To sign, copy **`signing.local.props.example`** to **`signing.local.props`** in the repository root and fill in, for each app and build type:
  * your App ID (bundle id)
  * your signing identity (e.g. `Apple Development: Your Name (TEAMID)`)
  * your provisioning profile name
* `signing.local.props` is ignored by git, and the Studio and Player projects import it automatically.

Notes:
* Provisioning profiles must be installed in `~/Library/MobileDevice/Provisioning Profiles`.
* Mac Catalyst apps that share an iOS App ID use the `maccatalyst.` prefix, as Xcode does.
* Games exported with their own bundle id need profiles for that id. Enter them in the export window's signing fields.
* Uploading to the Mac App Store also needs a *Mac Installer Distribution* certificate.

## Content and licences

Exported games contain everything in the package. Make sure you have the rights to all the text, pictures and audio you ship. This matters especially for imported commercial 8-bit games and fan games based on other people's characters. The *Genesis* example (Doctor Who) is for personal testing only, so it is offered only in development builds.
