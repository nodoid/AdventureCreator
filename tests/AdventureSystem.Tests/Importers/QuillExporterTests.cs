using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Packaging;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.Quill;

namespace AdventureSystem.Tests.Importers;

public class QuillExporterTests
{
    // Word numbers
    private const int N = 1, S = 2, U = 3, D = 4, GET = 20, OPEN = 22, LAMP = 50, COIN = 51, DOOR = 53;
    // Late (version C) numbering
    private const byte OK = 5, DONE = 4, MESSAGE = 22, GETA = 24, SET = 31, PLUS = 33, AUTOG = 13, PAUSE = 17, LET = 35;
    private const byte AT = 0, PRESENT = 4, ZERO = 11;

    private static readonly string[] SystemMessages =
    {
        "It's too dark to see anything.", "I can also see:", "What now?", "What now?", "What now?", "What now?",
        "I don't understand.", "I can't go in that direction.", "I can't do that.", "I have with me:", " (worn)",
        "Nothing at all.", "Quit?", "END OF GAME", "Bye.", "OK.", "Press any key.", "You have taken ", " turn", "s", ".",
        "You have scored ", "%.", "I'm not wearing it.", "My hands are full.", "I already have it.", "It's not here.",
        "I can't carry any more.", "I don't have it.", "I'm already wearing it.", "Y", "N",
    };

    /// <summary>A later-format ("C") Quill game; <paramref name="fillAbove"/> leaves no empty memory above the database.</summary>
    private static byte[] BuildLateGame(bool fillAbove = false)
    {
        var b = new QuillSnapshotBuilder();
        const int sig = 0x6B85;
        b.Poke(sig, 0x10, 6, 0x11, 1, 0x12, 0, 0x13, 0, 0x14, 0, 0x15, 0, 1);
        const int header = sig + 13;
        b.Poke(header, 4, 2, 3, 2, (byte)SystemMessages.Length);
        const int h = header + 1;
        b.Poke(h + 29, 0x80, (byte)'t', (byte)'h', (byte)'e', (byte)(' ' | 0x80));

        int locs = b.TextTable(new[] { "You are in a dark cellar. Stairs lead up.", "I'm in the kitchen.", "¥" + "garden." });
        int objs = b.TextTable(new[] { "a brass lamp", "some gold coins" });
        int msgs = b.TextTable(new[] { "The door creaks open.", "Clink." });
        int sys = b.TextTable(SystemMessages);
        int vocab = b.Vocabulary(("N", N), ("NORT", N), ("S", S), ("U", U), ("UP", U), ("D", D), ("DOWN", D),
            ("GET", GET), ("TAKE", GET), ("OPEN", OPEN), ("LAMP", LAMP), ("COIN", COIN), ("DOOR", DOOR));
        int conn = b.Connections(new[] { (U, 1) }, new[] { (D, 0), (N, 2) }, new[] { (S, 1) });
        int pos = b.Put(0, 1);
        int objmap = b.Put(LAMP, COIN, 0xFF);
        int resp = b.Table(
            (GET, LAMP, new byte[] { PRESENT, 0 }, new byte[] { GETA, 0, OK }),
            (GET, COIN, Array.Empty<byte>(), new byte[] { AUTOG, MESSAGE, 1, DONE }),
            (OPEN, DOOR, new byte[] { AT, 1, ZERO, 11 }, new byte[] { MESSAGE, 0, SET, 11, PLUS, 30, 5, DONE }),
            // PAUSE with flag 28 = 17 is a special function the importer can't model: it must survive the export.
            (OPEN, 255, new byte[] { ZERO, 12 }, new byte[] { LET, 28, 17, PAUSE, 3 }));
        int proc = b.Table();
        b.PokeWord(h + 4, resp);
        b.PokeWord(h + 6, proc);
        b.PokeWord(h + 8, objs);
        b.PokeWord(h + 10, locs);
        b.PokeWord(h + 12, msgs);
        b.PokeWord(h + 14, sys);
        b.PokeWord(h + 16, conn);
        b.PokeWord(h + 18, vocab);
        b.PokeWord(h + 20, pos);
        b.PokeWord(h + 22, objmap);
        int end = b.Put();
        b.PokeWord(h + 24, end);
        if (fillAbove) Array.Fill<byte>(b.Mem, 0xAA, end, 0xFF00 - end);
        return b.ToSna();
    }

    private static Adventure Import(byte[] data, string name = "cellar.sna") =>
        ImporterRegistry.Run(new QuillImporter(), data, name).Adventure;

    private static string Play(Adventure a, params string[] commands)
    {
        var e = new GameEngine(a, 1);
        var sb = new System.Text.StringBuilder(e.Start().Text);
        foreach (var c in commands) sb.Append("> " + c + "\n" + e.Submit(c).Text);
        return sb.ToString();
    }

    [Fact]
    public void Unchanged_game_exports_the_original_snapshot()
    {
        var data = BuildLateGame();
        var game = Import(data);
        var exporter = new QuillExporter();
        Assert.Null(exporter.CannotExport(game));
        Assert.Equal("sna", exporter.Extension(game));
        var result = exporter.Export(game);
        Assert.Equal(data, result.Data);
        Assert.Contains("unchanged", result.Summary);
    }

    [Fact]
    public void Needs_the_original_snapshot()
    {
        var game = Import(BuildLateGame());
        game.Origin = null;
        Assert.Contains("original Quill snapshot", new QuillExporter().CannotExport(game));
        Assert.NotNull(new QuillExporter().CannotExport(new Adventure()));
    }

    [Fact]
    public void Rebuilt_database_keeps_everything_that_was_not_edited()
    {
        var data = BuildLateGame();
        var game = Import(data);
        game.Rooms[2].Description = "A sunny garden, full of the scent of roses.";
        var again = Import(new QuillExporter().Export(game).Data);

        Assert.Equal("A sunny garden, full of the scent of roses.", again.Rooms[2].Description);
        var before = Import(data);
        string Json<T>(T x) => System.Text.Json.JsonSerializer.Serialize(x, AdventurePackage.JsonOptions);
        Assert.Equal(Json(before.Rooms.Take(2)), Json(again.Rooms.Take(2)));
        Assert.Equal(Json(before.Items), Json(again.Items));
        Assert.Equal(Json(before.Triggers), Json(again.Triggers));
        Assert.Equal(Json(before.Vocabulary), Json(again.Vocabulary));
        Assert.Equal(Json(before.Messages), Json(again.Messages));
        Assert.Equal(Play(before, "get lamp", "u", "open door", "open door"), Play(again, "get lamp", "u", "open door", "open door"));
    }

    [Fact]
    public void Edits_are_compiled_into_the_database()
    {
        var game = Import(BuildLateGame());
        game.Items[1].Name = "shiny golden widget";
        game.Rooms.Add(new Room { Id = "vault", Name = "Vault", Description = "A secret vault." });
        game.Rooms[0].Exits.Add(new Exit { Direction = "down", TargetRoomId = "vault" });
        game.Rooms[^1].Exits.Add(new Exit { Direction = "up", TargetRoomId = "r0" });
        game.Vocabulary.Verbs.Add(new VerbDefinition { Id = "xyzzy", Words = { "xyzzy", "plugh" } });
        var magic = new Trigger { Id = "magic", Event = TriggerEvent.BeforeCommand, Verb = "xyzzy", Noun1 = "*" };
        magic.Conditions.Add(new Condition(ConditionType.VarEquals, "tingled", 0));
        magic.Actions.Add(GameAction.Say("The air tingles."));
        magic.Actions.Add(new GameAction(ActionType.SetVar, "tingled", 1));
        magic.Actions.Add(new GameAction(ActionType.Done));
        game.Triggers.Insert(0, magic);
        game.Variables.Add(new Variable { Name = "tingled" });

        var result = new QuillExporter().Export(game);
        var again = Import(result.Data);

        Assert.Equal("shiny golden widget", again.Items[1].Name);
        Assert.Equal("A secret vault.", again.Rooms[3].Description);
        Assert.Contains(again.Rooms[0].Exits, e => e.Direction == "down" && e.TargetRoomId == "r3");
        var transcript = Play(again, "xyzzy", "plugh", "d", "u", "u");
        Assert.Contains("The air tingles.", transcript);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(transcript, "tingles"));   // a free flag remembers it
        Assert.True(transcript.Contains("A secret vault."), transcript);
        Assert.Contains("I'm in the kitchen.", transcript);
        // The PAUSE special function the importer can't model is still in the response table, unchanged.
        string Json<T>(T x) => System.Text.Json.JsonSerializer.Serialize(x, AdventurePackage.JsonOptions);
        var original = Import(BuildLateGame()).FindTrigger("resp3")!;
        var kept = again.Triggers.Single(t => t.Name == "OPEN _");
        Assert.Equal(Json(original.Conditions), Json(kept.Conditions));
        Assert.Equal(Json(original.Actions), Json(kept.Actions));
    }

    [Fact]
    public void Deleted_rooms_keep_their_numbers()
    {
        var game = Import(BuildLateGame());
        game.Rooms.RemoveAt(2);
        game.Rooms[1].Exits.RemoveAll(e => e.TargetRoomId == "r2");
        var result = new QuillExporter().Export(game);
        var again = Import(result.Data);
        Assert.Equal(3, again.Rooms.Count);
        Assert.Equal("", again.Rooms[2].Description);
        Assert.Contains(result.Warnings, w => w.Contains("keep their number"));
    }

    [Fact]
    public void Unrepresentable_changes_are_reported()
    {
        var game = Import(BuildLateGame());
        var t = new Trigger { Id = "enter", Event = TriggerEvent.EnterRoom };
        t.Actions.Add(GameAction.Say("Hello."));
        game.Triggers.Add(t);
        game.Rooms[0].Exits[0].Conditions.Add(new Condition(ConditionType.ItemCarried, "o0"));
        var warnings = new QuillExporter().Export(game).Warnings;
        Assert.Contains(warnings, w => w.Contains("EnterRoom triggers have no Quill equivalent"));
        Assert.Contains(warnings, w => w.Contains("connections are always open"));
    }

    [Fact]
    public void Text_that_no_longer_fits_moves_to_spare_memory_or_fails()
    {
        var game = Import(BuildLateGame());
        game.Rooms[1].Description = string.Join(" ", Enumerable.Repeat("A very long and winding description.", 40));
        var result = new QuillExporter().Export(game);
        Assert.Contains(result.Warnings, w => w.Contains("unused memory"));
        Assert.Equal(game.Rooms[1].Description, Import(result.Data).Rooms[1].Description);

        var full = Import(BuildLateGame(fillAbove: true));
        full.Rooms[1].Description = game.Rooms[1].Description;
        var error = Assert.Throws<InvalidOperationException>(() => new QuillExporter().Export(full));
        Assert.Contains("too big for the Spectrum's memory", error.Message);
    }

    [Fact]
    public void Exports_back_into_z80_snapshots()
    {
        // A version 1 .z80 of the same memory: the export must be a .z80 that decodes to the edited game.
        var sna = BuildLateGame();
        var z80 = new byte[30 + 49152];
        z80[6] = 0x00; z80[7] = 0x80;   // pc: version 1
        Array.Copy(sna, 27, z80, 30, 49152);
        var game = Import(z80, "cellar.z80");
        game.Rooms[1].Description = "I'm in the scullery.";
        var exporter = new QuillExporter();
        Assert.Equal("z80", exporter.Extension(game));
        var data = exporter.Export(game).Data;
        Assert.True(data.Length < z80.Length);   // re-compressed
        Assert.Equal("I'm in the scullery.", Import(data, "cellar.z80").Rooms[1].Description);
    }
}
