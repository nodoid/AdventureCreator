using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Importers.Common;
using AdventureCreator.Importers.Paws;
using Xunit;

namespace AdventureCreator.Tests.Importers;

public class PawsExporterTests
{
    private static Adventure Import(byte[] sna) => ImporterRegistry.Run(new PawsImporter(), sna, "game.sna").Adventure;

    private static Adventure RoundTrip(Adventure a, out ExportResult result)
    {
        var exporter = ExporterRegistry.Original(a)!;
        Assert.IsType<PawsExporter>(exporter);
        Assert.Null(exporter.CannotExport(a));
        result = exporter.Export(a);
        Assert.Equal("sna", exporter.Extension(a));
        return Import(result.Data);
    }

    private static string Play(Adventure a, params string[] commands)
    {
        var e = new GameEngine(a, 1);
        var text = e.Start().Text;
        foreach (var c in commands) text += "> " + c + "\n" + e.Submit(c).Text;
        return text;
    }

    private static string Json(object o) => System.Text.Json.JsonSerializer.Serialize(o, AdventurePackage.JsonOptions);

    [Fact]
    public void Unchanged_game_exports_to_the_same_database()
    {
        var a = Import(PawsImporterTests.SampleGame().BuildSna());
        var again = RoundTrip(a, out _);
        Assert.Equal(Json(a.Rooms), Json(again.Rooms));
        Assert.Equal(Json(a.Items), Json(again.Items));
        Assert.Equal(Json(a.Triggers), Json(again.Triggers));
        Assert.Equal(Json(a.Vocabulary), Json(again.Vocabulary));
        Assert.Equal(Json(a.Messages), Json(again.Messages));
        Assert.Equal(Play(a, "n", "s", "get lamp", "open box", "i"), Play(again, "n", "s", "get lamp", "open box", "i"));
    }

    [Fact]
    public void Edits_and_new_triggers_are_compiled()
    {
        var a = Import(PawsImporterTests.SampleGame().BuildSna());
        a.FindRoom("r0")!.Description = "A grand entrance hall.";
        a.FindItem("o2")!.Name = "wooden chest";
        a.FindItem("o2")!.Description = "";
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "xyzzy", Words = { "xyzzy", "plugh" } });
        a.Triggers.Add(new Trigger { Id = "magic", Verb = "xyzzy", Actions = { GameAction.Say("A hollow voice says \"fool\".") } });
        a.Triggers.Add(new Trigger
        {
            Id = "garden", Verb = "look", RoomId = "r1", Conditions = { new Condition(ConditionType.ItemCarried, "o0") },
            Actions = { GameAction.Say("The lamp glints."), new GameAction(ActionType.SetVar, "f200", 7) },
        });

        var again = RoundTrip(a, out var result);
        Assert.Equal("A grand entrance hall.", again.FindRoom("r0")!.Description);
        Assert.Equal("wooden chest", again.FindItem("o2")!.Name);
        Assert.Contains("xyzzy", again.Vocabulary.Verbs.SelectMany(v => v.Words));
        var magic = Assert.Single(again.Triggers, t => t.Actions.Any(x => x.Text?.Contains("hollow voice") == true));
        Assert.Contains(magic.Actions, x => x.Type == ActionType.Done);
        var garden = Assert.Single(again.Triggers, t => t.Actions.Any(x => x.Text == "The lamp glints."));
        Assert.Contains(garden.Conditions, c => c.Type == ConditionType.PlayerIn && c.A == "r1");
        Assert.Contains(garden.Conditions, c => c.Type == ConditionType.ItemIn && c.A == "o0");
        Assert.Contains(garden.Actions, x => x.Type == ActionType.SetVar && x.A == "f200" && x.N == 7);

        Assert.Contains("hollow voice", Play(again, "plugh"));
        Assert.Contains("A grand entrance hall.", Play(again));
    }

    [Fact]
    public void Things_PAWS_cannot_store_are_reported()
    {
        var a = Import(PawsImporterTests.SampleGame().BuildSna());
        a.Triggers.Add(new Trigger { Id = "t", Event = TriggerEvent.Timer, Interval = 3, Actions = { GameAction.Say("tick") } });
        a.Triggers.Add(new Trigger { Id = "u", Verb = "look", Actions = { new GameAction(ActionType.PlaySound, "bang") } });
        RoundTrip(a, out var result);
        Assert.Contains(result.Warnings, w => w.Contains("Timer triggers have no PAWS equivalent"));
        Assert.Contains(result.Warnings, w => w.Contains("PlaySound actions have no PAWS equivalent"));
    }

    [Fact]
    public void A_game_that_no_longer_fits_is_refused()
    {
        var a = Import(PawsImporterTests.SampleGame().BuildSna());
        a.FindRoom("r1")!.Description = string.Concat(Enumerable.Repeat("An enormous garden with no end. ", 1200));
        var ex = Assert.Throws<InvalidOperationException>(() => RoundTrip(a, out _));
        Assert.Contains("too big", ex.Message);
    }

    [Fact]
    public void Games_not_imported_from_PAWS_cannot_use_it()
    {
        var a = new Adventure();
        Assert.NotNull(new PawsExporter().CannotExport(a));
    }
}
