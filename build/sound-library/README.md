# How the sound library was made

`assets/sounds` holds 88 CC0 recordings chosen from these packs (licences checked on each download page):

* Kenney: RPG Audio, Impact Sounds, Sci-fi Sounds, Interface Sounds, Digital Audio (kenney.nl, each with a CC0 `License.txt`)
* rubberduck on OpenGameArt.org: 100 CC0 SFX, 100 CC0 SFX #2, 30 CC0 SFX loops, 40 CC0 water / splash / slime SFX, 80 CC0 RPG SFX, 25 CC0 bang / firework SFX
* OpenGameArt.org single sounds, all marked CC0: Rain + Long Thunder (WuxiaScrub), Beach Ocean Waves (jasinski / qubodup), Classic fanfare lick (fvcalderan), Foghorn Kinda Gross and Low Rumbling (Musheran), wind whoosh loop (SketchMan3), Loopable Dungeon Ambience (JaggedStone), Air whoosh (pyranostudios)

To rebuild:

1. Download and unzip the packs.
2. Convert each file listed in `manifest.tsv` with macOS `afconvert -f WAVE -d LEI16@22050 -c 1 <source> conv/<id>.wav`.
3. Run `dotnet run -- manifest.tsv conv ../../assets/sounds`. It trims long sounds (the manifest's `maxSeconds` and optional start), adds short fades to loops, levels them (one-shots peak at 90 %, loops at 75 %), and writes the WAVs and `library.json`.

`assets/sounds/CREDITS.md` lists every sound with its author, original file and source page. Add a row there, and the matching pack to `Program.cs`, for any new sound.
