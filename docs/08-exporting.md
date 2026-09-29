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

## Exporting — File › Export Standalone Game… (⇧⌘E)

| Option | Produces | Needs | Platforms |
|---|---|---|---|
| **1. Game package** | `MyGame.adventure` | nothing | Opens in the Adventure Player on any platform (*Open a game…*), and in the console player |
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

This creates:
* `artifacts/templates/console/<rid>/adventure-player[.exe]`: console players for `osx-arm64`, `osx-x64`, `win-x64` and `linux-x64`
* `artifacts/templates/Adventure Player.app` on macOS, or `artifacts/templates/windows/` on Windows

In the export window, choose the template once with **Browse…**; the Studio remembers it.

* **Desktop app from a template** copies the Player and puts `game.adventure` inside it.
  * On macOS it goes in `Contents/Resources`; the bundle's display name is set to the game's title and the app is re-signed ad hoc.
  * On Windows it goes next to the `.exe`, which is also copied under the game's name.
* **Console executable** appends the game package to the player program. The result (about 35 MB, self-contained) runs with no installation: `./MyGame`. Saved positions go to the user's application-data folder.

Ad-hoc-signed Mac apps run on your own Mac. To give them to other people, sign them with a Developer ID and notarise them, or macOS Gatekeeper will block them.

## Code signing of the Studio and Player themselves

The projects are set up for team **TEAMID**:

| App | Build | Identity | Profile | Bundle id |
|---|---|---|---|---|
| Player, iOS | Debug | Apple Development | `player-dev-profile` | `com.adventurecreator.player` |
| Player, iOS | Release | Apple Distribution | `player-release-profile` | `com.adventurecreator.player` |
| Player, Mac | Debug | Apple Development | `player-dev-mac-profile` | `maccatalyst.com.adventurecreator.player` |
| Studio, Mac | Release (Mac App Store, sandboxed) | Apple Distribution | `studio-release-mac-profile` | `maccatalyst.com.adventurecreator.studio` |

Notes:
* Provisioning profiles must be installed in `~/Library/MobileDevice/Provisioning Profiles`.
* Mac Catalyst apps that share an iOS App ID use the `maccatalyst.` prefix, as Xcode does.
* Games exported with their own bundle id aren't signed with these profiles.
* Still missing: a `studio-dev-profile` profile (Studio Debug builds are unsigned), a Mac release profile for the Player, an iPad profile for the Studio, and the *Mac Installer Distribution* certificate needed to upload the Studio to the Mac App Store.

## Content and licences

Exported games contain everything in the package. Make sure you have the rights to all the text, pictures and audio you ship. This matters especially for imported commercial 8-bit games and fan games based on other people's characters. The *Genesis* example (Doctor Who) is for personal testing only, so it is offered only in development builds.
