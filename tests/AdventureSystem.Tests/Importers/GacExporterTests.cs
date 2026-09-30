using System.Reflection;
using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.Gac;

namespace AdventureSystem.Tests.Importers;

public class GacExporterTests
{
    private const int North = 1, South = 2, Get = 3, Open = 5, Pull = 6;

    private static GacTestDatabaseBuilder Sample() =>
        new GacTestDatabaseBuilder { StartRoom = 1 }
            .Verb(North, "NORTH", "N").Verb(South, "SOUTH", "S").Verb(Get, "GET", "TAKE").Verb(Open, "OPEN").Verb(Pull, "PULL")
            .Noun(1, "LAMP").Noun(2, "KEY").Noun(3, "DOOR")
            .Room(1, 0, "You are in a small hall. A door leads north.", (North, 2))
            .Room(2, 0, "You are in the garden.", (South, 1))
            .Object(1, 5, 1, "a brass lamp")
            .Object(2, 1, 255, "the key")
            .Message(1, "The door creaks open.")
            .Message(2, "Click!")
            .Message(240, "What now?")
            // Local, room 1: IF ( VERB 5 AND NOUN 3 ) MESS 1 WAIT END
            .Local(1, Open, "VERB", 3, "NOUN", "AND", "IF", 1, "MESS", "WAIT", "END")
            // Low: IF ( VERB 6 ) MESS 2 WAIT END
            .Low(Pull, "VERB", "IF", 2, "MESS", "WAIT", "END")
            // Low: IF ( VERB 3 AND NOUN 1 AND HERE 1 ) GET 1 OKAY END
            .Low(Get, "VERB", 1, "NOUN", "AND", 1, "HERE", "AND", "IF", 1, "GET", "OKAY", "END")
            // Low: IF ( RAND 5 < CTR 7 ) MESS 1 END   (not convertible: must survive untouched)
            .Low(5, "RAND", 7, "CTR", "<", "IF", 1, "MESS", "END");

    private static Adventure Import(byte[] data, string name) =>
        ImporterRegistry.Run(new GacImporter(), data, name).Adventure;

    private static string Play(Adventure a, params string[] commands)
    {
        var e = new GameEngine(a, 1);
        var text = e.Start().Text;
        foreach (var c in commands) text += "> " + c + "\n" + e.Submit(c).Text;
        return text;
    }

    /// <summary>Edits made in the Studio: a room's text, an object's name and a new command.</summary>
    private static void Edit(Adventure a)
    {
        a.FindRoom("r2")!.Description = "You are in the rose garden, which smells wonderful.";
        a.FindItem("o1")!.Name = "golden lamp";
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "dance", Words = { "dance", "jig" } });
        a.Triggers.Add(new Trigger
        {
            Id = "new_dance", Event = TriggerEvent.BeforeCommand, Verb = "dance", Noun1 = "*",
            Actions = { GameAction.Say("You dance a little jig.") },
        });
    }

    [Fact]
    public void Needs_the_original_file()
    {
        var exporter = new GacExporter();
        Assert.NotNull(exporter.CannotExport(new Adventure()));
        var a = Import(Sample().BuildSna(), "game.sna");
        Assert.Null(exporter.CannotExport(a));
        Assert.Equal("sna", exporter.Extension(a));
        Assert.IsType<GacExporter>(ExporterRegistry.Original(a));
    }

    [Fact]
    public void Unchanged_game_is_the_original_file()
    {
        var sna = Sample().BuildSna();
        var result = new GacExporter().Export(Import(sna, "game.sna"));
        Assert.Equal(sna, result.Data);
    }

    [Fact]
    public void Rebuilding_an_unchanged_database_gives_the_same_bytes()
    {
        var sna = Sample().BuildSna();
        var export = typeof(GacExporter).GetMethod("Export", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (ExportResult)export.Invoke(new GacExporter(), new object[] { Import(sna, "game.sna"), true })!;
        Assert.Equal(sna, result.Data);
        var tap = Sample().BuildDataFileTap();
        result = (ExportResult)export.Invoke(new GacExporter(), new object[] { Import(tap, "game.tap"), true })!;
        Assert.Equal(tap, result.Data);
    }

    [Theory]
    [InlineData("sna")]
    [InlineData("tap")]
    [InlineData("z80")]
    public void Edits_are_written_back(string format)
    {
        var sna = Sample().BuildSna();
        var file = format switch
        {
            "tap" => Sample().BuildDataFileTap(),
            "z80" => Z80(sna),
            _ => sna,
        };
        var a = Import(file, "game." + format);
        var before = Play(a, "open door", "pull", "take lamp", "i");
        Edit(a);

        var result = new GacExporter().Export(a);
        var again = Import(result.Data, "game." + format);

        Assert.Equal("You are in the rose garden, which smells wonderful.", again.FindRoom("r2")!.Description);
        Assert.Equal("golden lamp", again.FindItem("o1")!.Name);
        Assert.Contains("You dance a little jig.", Play(again, "jig"));
        // The rest of the game is as it was, including the line the importer couldn't convert.
        Assert.Equal(before.Replace("brass lamp", "golden lamp"), Play(again, "open door", "pull", "take lamp", "i"));
        Assert.Equal(4, Assert.IsType<List<byte[]>>(Lines(result.Data, format)).Count);
    }

    [Fact]
    public void Edited_trigger_is_recompiled_in_place()
    {
        var a = Import(Sample().BuildSna(), "game.sna");
        var pull = a.Triggers.Single(t => t.Verb == "pull");
        pull.Actions[0] = GameAction.Say("The lever won't budge.");
        var again = Import(new GacExporter().Export(a).Data, "game.sna");
        Assert.Contains("The lever won't budge.", Play(again, "pull"));
        Assert.Contains("The door creaks open.", Play(again, "open door"));
        Assert.Equal("lp1", again.Triggers.Single(t => t.Verb == "pull").Id);
    }

    [Fact]
    public void Edited_trigger_comparing_ROOM_is_compiled_back_to_ROOM()
    {
        // Low: IF ( VERB 3 AND ROOM < 2 ) MESS 2 WAIT END   (GET does something special in room 1 only)
        var sna = Sample().Low(Get, "VERB", "ROOM", 2, "<", "AND", "IF", 2, "MESS", "WAIT", "END").BuildSna();
        var a = Import(sna, "game.sna");
        var get = a.Triggers.Single(t => t.Conditions.Any(c => c.A == "gac_room"));
        get.Actions[0] = GameAction.Say("Only in the hall.");
        var result = new GacExporter().Export(a);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("gac_room") || w.Contains("BeforeEnterRoom"));

        var again = Import(result.Data, "game.sna");
        Assert.Equal(2, again.Rooms.Count);
        Assert.Equal(2, again.Triggers.Count(t => t.Id.StartsWith("gac_room_")));
        Assert.Contains("Only in the hall.", Play(again, "get"));
        Assert.DoesNotContain("Only in the hall.", Play(again, "n", "get"));
    }

    private static ExportResult ForcedExport(Adventure a) =>
        (ExportResult)typeof(GacExporter).GetMethod("Export", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new GacExporter(), new object[] { a, true })!;

    private static int SnaOffset(int address) => 27 + address - 0x4000;

    [Fact]
    public void A_database_followed_by_other_bytes_can_still_be_rebuilt_and_edited()
    {
        // As in real games, the byte after the dictionary isn't a zero (the builder writes one).
        var db = Sample().BuildDatabase();
        var sna = Sample().BuildSna();
        sna[SnaOffset(GacTestDatabaseBuilder.DatabaseAddress + db.Length - 1)] = 0x9A;
        Assert.Equal(sna, ForcedExport(Import(sna, "game.sna")).Data);

        var a = Import(sna, "game.sna");
        a.FindRoom("r2")!.Description = "A garden.";                     // shorter: fits
        var again = Import(new GacExporter().Export(a).Data, "game.sna");
        Assert.Equal("A garden.", again.FindRoom("r2")!.Description);

        a.FindRoom("r2")!.Description = "You are in the rose garden, which smells wonderful.";   // longer: no room
        var error = Assert.Throws<InvalidOperationException>(() => new GacExporter().Export(a));
        Assert.Contains("too big", error.Message);
    }

    [Fact]
    public void A_database_that_reaches_into_the_UDGs_may_use_memory_up_to_the_top()
    {
        // Messages fill memory until the database ends just past $FF58, where the UDGs normally start.
        var b = Sample();
        int id = 10;
        while (GacTestDatabaseBuilder.DatabaseAddress + b.BuildDatabase().Length <= 0xFF58)
            b.Message(id++, string.Join(" ", Enumerable.Repeat("ZIGZAG", 100)));
        int end = GacTestDatabaseBuilder.DatabaseAddress + b.BuildDatabase().Length - 1;   // without the builder's zero
        Assert.InRange(end, 0xFF59, 0xFFE0);
        var sna = b.BuildSna();
        for (int x = end; x < 0x10000; x++) sna[SnaOffset(x)] = 0x42;       // what is left of the UDGs
        Assert.Equal(sna, ForcedExport(Import(sna, "game.sna")).Data);

        var a = Import(sna, "game.sna");
        a.FindRoom("r2")!.Description = "You are in the garden. ZIGZAG.";
        var again = Import(new GacExporter().Export(a).Data, "game.sna");
        Assert.Equal("You are in the garden. ZIGZAG.", again.FindRoom("r2")!.Description);
    }

    [Fact]
    public void Deleted_rooms_objects_and_triggers_are_removed()
    {
        var a = Import(Sample().BuildSna(), "game.sna");
        a.Items.RemoveAll(i => i.Id == "o2");
        a.Triggers.RemoveAll(t => t.Verb == "pull");
        var again = Import(new GacExporter().Export(a).Data, "game.sna");
        Assert.Null(again.FindItem("o2"));
        Assert.DoesNotContain(again.Triggers, t => t.Verb == "pull");
        Assert.Contains("The door creaks open.", Play(again, "open door"));
    }

    [Fact]
    public void Things_GAC_cannot_hold_are_reported()
    {
        var a = Import(Sample().BuildSna(), "game.sna");
        a.Triggers.Add(new Trigger
        {
            Id = "t_enter", Event = TriggerEvent.EnterRoom, RoomId = "r2",
            Actions = { GameAction.Say("Birds sing.") },
        });
        a.FindItem("o1")!.Wearable = true;
        var result = new GacExporter().Export(a);
        Assert.Contains(result.Warnings, w => w.Contains("EnterRoom"));
        Assert.Contains(result.Warnings, w => w.Contains("object settings"));
    }

    [Fact]
    public void New_variables_become_counters_with_their_starting_values()
    {
        var a = Import(Sample().BuildSna(), "game.sna");
        a.Variables.Add(new Variable { Name = "gold", InitialValue = 5 });
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "count", Words = { "count" } });
        a.Triggers.Add(new Trigger
        {
            Id = "rich", Event = TriggerEvent.BeforeCommand, Verb = "count", Noun1 = "*",
            Conditions = { new Condition(ConditionType.VarGreater, "gold", 3) },
            Actions = { new GameAction(ActionType.Message, n: 1, text: "You have "), new GameAction(ActionType.Message, text: "{var:gold} coins."), new GameAction(ActionType.AddVar, "gold", -1) },
        });
        var again = Import(new GacExporter().Export(a).Data, "game.sna");
        var text = Play(again, "count", "count", "count");
        Assert.Contains("You have 5 coins.", text);
        Assert.Contains("You have 4 coins.", text);
        Assert.DoesNotContain("You have 3 coins.", text);
    }

    [Fact]
    public void Tape_blocks_have_valid_lengths_and_checksums()
    {
        var a = Import(Sample().BuildDataFileTap(), "game.tap");
        Edit(a);
        var tap = new GacExporter().Export(a).Data;
        int p = 0, blocks = 0;
        while (p < tap.Length)
        {
            int len = tap[p] | (tap[p + 1] << 8);
            byte x = 0;
            for (int i = 0; i < len; i++) x ^= tap[p + 2 + i];
            Assert.Equal(0, x);
            p += 2 + len;
            blocks++;
        }
        Assert.Equal(tap.Length, p);
        Assert.Equal(2, blocks);
        int declared = tap[2 + 12] | (tap[2 + 13] << 8);
        Assert.Equal(tap.Length - 21 - 4, declared);
    }

    /// <summary>A version 1 .z80 file (uncompressed) of the snapshot's RAM.</summary>
    private static byte[] Z80(byte[] sna)
    {
        var z80 = new byte[30 + 49152];
        z80[6] = 0x00; z80[7] = 0x80;   // pc ≠ 0: version 1
        Array.Copy(sna, 27, z80, 30, 49152);
        return z80;
    }

    /// <summary>The low priority lines of the exported database.</summary>
    private static List<byte[]> Lines(byte[] file, string format)
    {
        var asm = typeof(GacExporter).Assembly;
        var located = asm.GetType("AdventureSystem.Importers.Gac.GacImporter")!
            .GetMethod("Locate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { file, "game." + format })!;
        var t = located.GetType();
        var raw = Activator.CreateInstance(asm.GetType("AdventureSystem.Importers.Gac.GacRawDatabase")!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { t.GetProperty("Memory")!.GetValue(located), t.GetProperty("Layout")!.GetValue(located), t.GetProperty("Punctuation")!.GetValue(located), 0x10000 }, null)!;
        return (List<byte[]>)raw.GetType().GetProperty("Low")!.GetValue(raw)!;
    }
}
