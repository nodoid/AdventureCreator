# 9. The `.adventure` file format

An `.adventure` file is a standard **zip archive**:

```text
MyGame.adventure
├── adventure.json          the game (UTF-8 JSON)
└── assets/
    ├── images/cellar.png   imported bitmaps (PNG)
    └── sounds/wind.wav     audio (WAV / MP3 / M4A)
```

You can unzip it, edit `adventure.json` by hand or with scripts, and zip it again. The Studio and Player also open a bare `.json` file, which is useful for games without assets and for keeping games under version control.

## `adventure.json`

The JSON mirrors the classes in `src/AdventureCreator.Core/Model`:
* property names are PascalCase
* enums are written as strings
* null values are omitted

```json
{
  "FormatVersion": 1,
  "Title": "The Quiet House",
  "Author": "You",
  "Introduction": "Your great-aunt's house has stood empty for years…",
  "StartRoomId": "hall",
  "Settings": { "MaxCarriedItems": 10, "ShowPictures": true, "Verbose": true, "…": "…" },
  "Rooms": [
    { "Id": "hall", "Name": "Hall", "Description": "A dusty hall…",
      "Exits": [ { "Direction": "north", "TargetRoomId": "study", "DoorItemId": "studydoor" } ] }
  ],
  "Items": [
    { "Id": "key", "Name": "iron key", "Nouns": ["key"], "Adjectives": ["iron"], "Location": "pot" }
  ],
  "Triggers": [
    { "Id": "t2", "Name": "Give the locket", "Event": "BeforeCommand", "Verb": "give|show",
      "Noun1": "locket", "Noun2": "ghost",
      "Conditions": [ { "Type": "ItemCarried", "A": "locket" } ],
      "Actions": [ { "Type": "Message", "Text": "The ghost takes the locket…" },
                   { "Type": "SetVar", "A": "rested", "N": 1 } ] }
  ],
  "Puzzles": [ { "Id": "pz_rest", "Points": 20, "SolvedWhen": [ { "Type": "VarEquals", "A": "rested", "N": 1 } ] } ],
  "Variables": [ { "Name": "rested", "InitialValue": 0 } ],
  "Pictures": [ { "Id": "pic_study", "Width": 256, "Height": 176, "RenderMode": "FullColour",
                  "Commands": [ { "Op": "Clear", "Color": 16 }, { "Op": "FilledRectangle", "X": 40, "Y": 110, "X2": 200, "Y2": 120 } ] } ],
  "Sounds": [ { "Id": "snd_sigh", "Name": "Sigh", "AssetName": "sounds/sigh.wav", "Volume": 1 } ],
  "Vocabulary": { "Verbs": [ { "Id": "comfort", "Words": ["comfort", "console"], "Grammar": ["* {person}"] } ] },
  "Messages": { "CantGo": "The house won't let you go that way." }
}
```

Location values for items:
* a room id or item id
* `@carried` or `@worn`
* `""` (nowhere)

Actions can also use `@here` (the player's room).

### Picture commands

Each drawing command has an `Op`:

`Clear`, `SetInk`, `SetPaper`, `SetBright`, `Plot`, `Line`, `Rectangle`, `FilledRectangle`, `Ellipse`, `FilledEllipse`, `Polygon`, `FilledPolygon`, `Fill`, `Shade`, `AttributeBlock`, `Text`, `Call`, `Image`, `Freehand`

Their fields:

| Field | Meaning |
|---|---|
| `X`, `Y`, `X2`, `Y2` | Coordinates (top-left origin). |
| `Color` / `Color2` | Palette indices. |
| `Points` | A flat `[x1, y1, x2, y2, …]` list. |
| `Pattern` | 8 bytes (base64). Bit 7 is the leftmost pixel. |
| `Text` | The text, or the asset name for `Image`. |
| `SubPictureId` | The picture a `Call` draws. |
| `Scale` | Eighths for `Call` (8 = 1:1), letter size for `Text`, brush size for `Freehand`. |
| `Xor` / `Inverse` | Spectrum OVER / INVERSE. |

## Save games

Each save is one file, `<name>.sav`. The autosave is `autosave.sav`. Each file contains a small JSON object with:
* the save's name and the game's title
* the date saved
* the room name, score, maximum score and turns (shown in the Load list)
* `State`: a JSON snapshot of the game state

Saves from earlier versions, which contain only the state, are still accepted.

The game state includes:
* item locations and item states
* variables and score
* visited rooms and solved puzzles
* fired and enabled triggers
* changed descriptions and exits
* NPC states (health, following, hostility, destinations)
* player health, traps, flooded rooms and room flags
* how often each random event has happened

The Player stores them in its application-data folder (`Saves/<game title>/`). The Studio's Test Play uses `TestSaves/<game title>/`. The console player stores them under the user's application-data folder (`AdventureCreator/Saves/<game title>/`).
