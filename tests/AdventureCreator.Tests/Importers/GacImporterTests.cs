using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Gac;

namespace AdventureCreator.Tests.Importers;

public class GacImporterTests
{
    // Verb numbers
    private const int North = 1, South = 2, Get = 3, Drop = 4, Open = 5, Pull = 6, Push = 7, Look = 8;

    private static GacTestDatabaseBuilder Sample()
    {
        var b = new GacTestDatabaseBuilder { StartRoom = 1 }
            .Verb(North, "NORTH", "N").Verb(South, "SOUTH", "S").Verb(Get, "GET", "TAKE").Verb(Drop, "DROP")
            .Verb(Open, "OPEN").Verb(Pull, "PULL").Verb(Push, "PUSH").Verb(Look, "LOOK", "L")
            .Noun(1, "LAMP").Noun(2, "KEY").Noun(3, "DOOR").Noun(4, "LEVER").Noun(255, "IT")
            .Adverb(1, "QUICKLY")
            .Room(1, 1, "You are in a small hall. A door leads north.", (North, 2))
            .Room(2, 2, "You are in the garden.", (South, 1))
            .Object(1, 5, 1, "a brass lamp")
            .Object(2, 1, 255, "the key")
            .Object(3, 100, 0, "lever")
            .Message(1, "The door creaks open.")
            .Message(2, "Click!")
            .Message(3, "HELLO  world, It's OK.")
            .Message(240, "What now?")
            .Message(241, "You can't do that.")
            .Message(254, "Okay.")
            // High priority: IF ( RES? 5 ) SET 5 3 CSET 7 END
            .High(5, "RES?", "IF", 5, "SET", 3, 7, "CSET", "END")
            // Local, room 1: IF ( VERB 5 AND NOUN 3 ) MESS 1 SET 10 WAIT END
            .Local(1, Open, "VERB", 3, "NOUN", "AND", "IF", 1, "MESS", 10, "SET", "WAIT", "END")
            // Low: IF ( VERB 6 OR VERB 7 ) MESS 2 END
            .Low(Pull, "VERB", Push, "VERB", "OR", "IF", 2, "MESS", "END")
            // Low: IF ( VERB 3 AND NOUN 1 AND HERE 1 ) GET 1 OKAY END
            .Low(Get, "VERB", 1, "NOUN", "AND", 1, "HERE", "AND", "IF", 1, "GET", "OKAY", "END")
            // Low: IF ( NOT CARR 2 AND 3 < CTR 7 ) INCR 0 END
            .Low(2, "CARR", "NOT", 3, 7, "CTR", "<", "AND", "IF", 0, "INCR", "END")
            // Low: IF ( VERB 1 ) MESS 2 WAIT END   (north is an exit of room 1)
            .Low(North, "VERB", "IF", 2, "MESS", "WAIT", "END")
            // Low: IF ( NO1 < 3 AND VERB 4 ) DROP NO1 OKAY END   (range of nouns → one trigger per noun)
            .Low(1, "NO1", 3, "<", Drop, "VERB", "AND", "IF", "NO1", "DROP", "OKAY", "END")
            // Low: IF ( CONN 1 ) ... is only convertible in local tables; this one uses an unsupported comparison
            .Low(7, "CTR", 8, "CTR", "<", "IF", 1, "MESS", "END")
            .Picture(1,
                new object[] { "INK", 2 },
                new object[] { "LINE", 0, 175, 255, 48 },
                new object[] { "ELLIPSE", 128, 111, 148, 101 },
                new object[] { "RECT", 10, 60, 30, 50 },
                new object[] { "FILL", 10, 170 },
                new object[] { "SHADE", 20, 60 },
                new object[] { "PLOT", 1, 174 },
                new object[] { "CALL", 3 })
            .Picture(2, new object[] { "PAPER", 1 }, new object[] { "LINE", 0, 100, 10, 100 })
            .Picture(3, new object[] { "PLOT", 5, 170 });
        return b;
    }

    private static Adventure ImportSample(out AdventureCreator.Importers.Common.ImportResult result)
    {
        var sna = Sample().BuildSna();
        var importer = new GacImporter();
        Assert.True(importer.CanImport(sna, "test.sna"));
        result = importer.Import(sna, "test.sna");
        return result.Adventure;
    }

    [Fact]
    public void DetectsSnapshotAndFormat()
    {
        ImportSample(out var result);
        Assert.Contains("GAC", result.DetectedFormat);
        Assert.Contains("Spectrum", result.DetectedFormat);
    }

    [Fact]
    public void DoesNotClaimOtherSnapshots()
    {
        var importer = new GacImporter();
        Assert.False(importer.CanImport(new byte[49179], "empty.sna"));
        var random = new byte[49179];
        new Random(1).NextBytes(random);
        Assert.False(importer.CanImport(random, "random.sna"));
        Assert.False(importer.CanImport(new byte[100], "short.tap"));
    }

    [Fact]
    public void ImportsSettings()
    {
        var a = ImportSample(out _);
        Assert.True(a.Settings.LegacyTableSemantics);
        Assert.False(a.Settings.AutoListExits);
        Assert.Equal("r1", a.StartRoomId);
        Assert.Equal("What now? ", a.Settings.Prompt);
        Assert.Equal(250, a.Settings.MaxCarriedWeight);
    }

    [Fact]
    public void ImportsRoomsAndExits()
    {
        var a = ImportSample(out _);
        Assert.Equal(new[] { "r1", "r2" }, a.Rooms.Select(r => r.Id));
        var hall = a.FindRoom("r1")!;
        Assert.Equal("You are in a small hall. A door leads north.", hall.Description);
        Assert.Equal("You are in a small hall", hall.Name);
        Assert.Equal("p1", hall.PictureId);
        var exit = Assert.Single(hall.Exits);
        Assert.Equal("north", exit.Direction);
        Assert.Equal("r2", exit.TargetRoomId);
        Assert.Equal("south", Assert.Single(a.FindRoom("r2")!.Exits).Direction);
        Assert.Equal("north", a.Vocabulary.Directions["n"]);
        Assert.Equal("south", a.Vocabulary.Directions["s"]);
    }

    [Fact]
    public void ImportsObjects()
    {
        var a = ImportSample(out _);
        var lamp = a.FindItem("o1")!;
        Assert.Equal("a", lamp.Article);
        Assert.Equal("brass lamp", lamp.Name);
        Assert.Equal("r1", lamp.Location);
        Assert.Equal(5, lamp.Weight);
        Assert.Contains("lamp", lamp.Nouns);
        var key = a.FindItem("o2")!;
        Assert.Equal("the", key.Article);
        Assert.Equal(Locations.Carried, key.Location);
        var lever = a.FindItem("o3")!;
        Assert.Equal("", lever.Article);
        Assert.Equal(Locations.Nowhere, lever.Location);
        Assert.Contains("lever", lever.Nouns);
    }

    [Fact]
    public void ImportsVocabulary()
    {
        var a = ImportSample(out _);
        var get = a.Vocabulary.Verbs.Single(v => v.Id == "get");
        Assert.Equal(new[] { "get", "take" }, get.Words);
        Assert.Empty(get.Grammar);
        Assert.Equal(3, a.Vocabulary.LegacyWordNumbers["take"]);
        Assert.Equal(3, a.Vocabulary.LegacyWordNumbers["get"]);
        Assert.Contains("door", a.Vocabulary.Nouns);
        Assert.DoesNotContain("it", a.Vocabulary.Nouns);
        Assert.Contains("quickly", a.Vocabulary.Adverbs);
    }

    [Fact]
    public void DecodesCompressedText()
    {
        var a = ImportSample(out _);
        var t = a.Triggers.Where(x => x.Actions.Any(ac => ac.Type == ActionType.Message)).SelectMany(x => x.Actions).Select(ac => ac.Text).ToList();
        Assert.Contains("The door creaks open.", t);
        Assert.Equal("You can't do that.", a.Messages[Msg.CantDoThat]);
        Assert.Equal("Okay.", a.Messages[Msg.Ok]);
    }

    [Fact]
    public void TextEncodingRoundTripsCaseAndPunctuationRuns()
    {
        var b = new GacTestDatabaseBuilder()
            .Room(1, 0, "HELLO  world, It's OK. 3 keys!")
            .Verb(1, "WAIT");
        var r = new GacImporter().Import(b.BuildSna(), "t.sna");
        Assert.Equal("HELLO  world, It's OK. 3 keys!", r.Adventure.Rooms[0].Description);
    }

    [Fact]
    public void HighPriorityBecomesEveryTurnAndGameStart()
    {
        var a = ImportSample(out _);
        var every = a.Triggers.Single(t => t.Id == "hp1");
        Assert.Equal(TriggerEvent.EveryTurn, every.Event);
        var cond = Assert.Single(every.Conditions);
        Assert.Equal(ConditionType.VarEquals, cond.Type);
        Assert.Equal("m5", cond.A);
        Assert.Equal(0, cond.N);
        Assert.Collection(every.Actions,
            x => { Assert.Equal(ActionType.SetVar, x.Type); Assert.Equal("m5", x.A); Assert.Equal(1, x.N); },
            x => { Assert.Equal(ActionType.SetVar, x.Type); Assert.Equal("c7", x.A); Assert.Equal(3, x.N); });
        Assert.Contains(a.Triggers, t => t.Event == TriggerEvent.GameStart && t.Id == "hp1s");
        Assert.True(every.Priority > a.Triggers.Where(t => t.Event == TriggerEvent.BeforeCommand).Max(t => t.Priority));
        Assert.Contains(a.Variables, v => v.Name == "m5");
        Assert.Contains(a.Variables, v => v.Name == "c7");
    }

    [Fact]
    public void LocalConditionBecomesRoomTrigger()
    {
        var a = ImportSample(out _);
        var t = a.Triggers.Single(x => x.RoomId == "r1");
        Assert.Equal(TriggerEvent.BeforeCommand, t.Event);
        Assert.Equal("open", t.Verb);
        Assert.Equal("door", t.Noun1);
        Assert.False(t.StopsCommand);
        Assert.Empty(t.Conditions);
        Assert.Collection(t.Actions,
            x => { Assert.Equal(ActionType.Message, x.Type); Assert.Equal("The door creaks open.", x.Text); },
            x => { Assert.Equal(ActionType.SetVar, x.Type); Assert.Equal("m10", x.A); Assert.Equal(1, x.N); },
            x => Assert.Equal(ActionType.Done, x.Type));
        var lows = a.Triggers.Where(x => x.Event == TriggerEvent.BeforeCommand && x.RoomId == null);
        Assert.True(t.Priority > lows.Max(x => x.Priority));
    }

    [Fact]
    public void OrExpressionIsSplitIntoTwoTriggers()
    {
        var a = ImportSample(out _);
        var split = a.Triggers.Where(t => t.Actions.Count == 1 && t.Actions[0].Text == "Click!" && t.Verb is "pull" or "push").ToList();
        Assert.Equal(2, split.Count);
        Assert.Equal(new[] { "pull", "push" }, split.Select(t => t.Verb).OrderBy(v => v));
        Assert.All(split, t => Assert.Empty(t.Conditions));
        Assert.All(split, t => Assert.Contains("OR", t.Notes));
    }

    [Fact]
    public void ConvertsConditionsAndActions()
    {
        var a = ImportSample(out _);
        var get = a.Triggers.Single(t => t.Verb == "get");
        Assert.Equal("lamp", get.Noun1);
        var here = Assert.Single(get.Conditions);
        Assert.Equal(ConditionType.ItemIn, here.Type);
        Assert.Equal("o1", here.A);
        Assert.Equal(Locations.Here, here.B);
        Assert.Equal(new[] { ActionType.TakeItem, ActionType.Ok }, get.Actions.Select(x => x.Type));
        Assert.Equal("o1", get.Actions[0].A);

        // IF ( NOT CARR 2 AND 3 < CTR 7 ) INCR 0 END
        var score = a.Triggers.Single(t => t.Actions.Any(x => x.Type == ActionType.AwardScore));
        Assert.Equal("*", score.Verb);
        Assert.Contains(score.Conditions, c => c.Type == ConditionType.ItemCarried && c.A == "o2" && c.Negate);
        Assert.Contains(score.Conditions, c => c.Type == ConditionType.VarGreater && c.A == "c7" && c.N == 3 && !c.Negate);
        Assert.Equal(1, score.Actions.Single().N);
    }

    [Fact]
    public void DirectionVerbTriggersDontFireWhereTheExitExists()
    {
        var a = ImportSample(out _);
        var t = a.Triggers.Single(x => x.Verb == "north");
        Assert.Contains(t.Conditions, c => c.Type == ConditionType.PlayerIn && c.A == "r1" && c.Negate);
    }

    [Fact]
    public void NounNumberRangeIsExpandedAndBound()
    {
        var a = ImportSample(out _);
        var drops = a.Triggers.Where(t => t.Verb == "drop").OrderBy(t => t.Noun1).ToList();
        Assert.Equal(new[] { "key", "lamp" }, drops.Select(t => t.Noun1));
        Assert.Equal("o2", drops[0].Actions[0].A);
        Assert.Equal("o1", drops[1].Actions[0].A);
        Assert.All(drops, t => Assert.Equal(ActionType.DropItem, t.Actions[0].Type));
    }

    [Fact]
    public void UnconvertibleConditionIsSkippedWithNote()
    {
        var a = ImportSample(out var result);
        Assert.Contains(a.Notes, n => n.Contains("CTR 7 < CTR 8"));
        Assert.Contains(result.Warnings, w => w.Contains("CTR 7 < CTR 8"));
    }

    [Fact]
    public void ConvertsPictures()
    {
        var a = ImportSample(out _);
        var p1 = a.FindPicture("p1")!;
        Assert.Equal(PictureRenderMode.SpectrumAttributes, p1.RenderMode);
        Assert.Equal(256, p1.Width);
        Assert.Equal(128, p1.Height);
        Assert.False(p1.IsSubroutine);
        var ops = p1.Commands;
        Assert.Equal(DrawOp.SetInk, ops[0].Op);
        Assert.Equal(2, ops[0].Color);
        Assert.Equal((DrawOp.Line, 0, 0, 255, 127), (ops[1].Op, ops[1].X, ops[1].Y, ops[1].X2, ops[1].Y2));
        // Centre (128, 111) → top-down (128, 64); radii 20 and 10.
        Assert.Equal((DrawOp.Ellipse, 108, 54, 148, 74), (ops[2].Op, ops[2].X, ops[2].Y, ops[2].X2, ops[2].Y2));
        Assert.Equal((DrawOp.Rectangle, 10, 115, 30, 125), (ops[3].Op, ops[3].X, ops[3].Y, ops[3].X2, ops[3].Y2));
        Assert.Equal((DrawOp.Fill, 10, 5), (ops[4].Op, ops[4].X, ops[4].Y));
        Assert.Equal(DrawOp.Shade, ops[5].Op);
        Assert.Equal(8, ops[5].Pattern!.Length);
        Assert.Equal((DrawOp.Plot, 1, 1), (ops[6].Op, ops[6].X, ops[6].Y));
        Assert.Equal(DrawOp.Call, ops[7].Op);
        Assert.Equal("p3", ops[7].SubPictureId);
        Assert.True(a.FindPicture("p3")!.IsSubroutine);
        Assert.False(a.FindPicture("p2")!.IsSubroutine);
        Assert.Equal("p2", a.FindRoom("r2")!.PictureId);
    }

    [Fact]
    public void ImportsDataFileSavedFromEditor()
    {
        var tap = Sample().BuildDataFileTap("MYADV");
        var importer = new GacImporter();
        Assert.True(importer.CanImport(tap, "myadv.tap"));
        var r = importer.Import(tap, "myadv.tap");
        Assert.Contains("MYADV", r.DetectedFormat);
        Assert.Equal(2, r.Adventure.Rooms.Count);
        Assert.Equal("You are in the garden.", r.Adventure.FindRoom("r2")!.Description);
    }

    [Fact]
    public void DarknessMarkersMapToDarknessVariable()
    {
        var b = new GacTestDatabaseBuilder()
            .Verb(1, "ENTER")
            .Room(1, 0, "Outside.").Room(2, 0, "A cave.")
            .Local(1, 1, "VERB", "IF", 1, "RESE", 2, "GOTO", "WAIT", "END");
        var a = new GacImporter().Import(b.BuildSna(), "dark.sna").Adventure;
        Assert.Equal("gac_dark", a.Settings.DarknessVariable);
        Assert.Equal(1, a.FindVariable("m1")!.InitialValue);
        var t = a.Triggers.Single(x => x.RoomId == "r1");
        Assert.Contains(t.Actions, x => x.Type == ActionType.RunTrigger && x.A == "gac_darkness");
        Assert.Equal(3, a.Triggers.Count(x => x.Event == TriggerEvent.Subroutine && x.Name == "gac_darkness"));
    }
}
