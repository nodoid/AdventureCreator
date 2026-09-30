using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Samples;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.ScottAdams;

namespace AdventureSystem.Tests.Importers;

public class ScottAdamsExporterTests
{
    private static Adventure Import(byte[] data) => ImporterRegistry.Run(new ScottAdamsImporter(), data, "tiny.dat").Adventure;

    private static Adventure RoundTrip(Adventure a, out ExportResult result)
    {
        var exporter = Assert.IsType<ScottAdamsExporter>(ExporterRegistry.Original(a));
        Assert.Null(exporter.CannotExport(a));
        Assert.Equal("dat", exporter.Extension(a));
        result = exporter.Export(a);
        return Import(result.Data);
    }

    [Fact]
    public void TheWorldSurvivesExport()
    {
        var a = RoundTrip(Import(ScottAdamsImporterTests.Game()), out var result);
        Assert.Contains("2 rooms", result.Summary);
        Assert.Equal(2, a.Rooms.Count);
        Assert.Equal("I'm in a dusty hall", a.FindRoom(a.StartRoomId)!.Description);
        Assert.Contains(a.Items, i => i.LightSource && i.Location == Locations.Carried);
        Assert.Contains(a.Items, i => i.Nouns.Contains("key"));
        Assert.Equal(3, a.Settings.SignificantLetters);
    }

    [Fact]
    public void ThePuzzleCanStillBeSolvedAfterExport()
    {
        var a = RoundTrip(Import(ScottAdamsImporterTests.Game()), out _);
        var e = new GameEngine(a, randomSeed: 1);
        e.Start();
        Assert.Contains("O.K.", e.Submit("get key").Text);
        Assert.Contains("The door creaks open.", e.Submit("open door").Text);
        var enter = e.Submit("climb door").Text;
        Assert.Contains("I'm in the vault", enter);
        Assert.Contains("The vault is cold.", enter);
        var shout = e.Submit("shout").Text;
        Assert.Contains("HELLO!", shout);
        Assert.Contains("...echo.", shout);
        e.Submit("get gold");
        e.Submit("w");
        e.Submit("drop gold");
        var score = e.Submit("score");
        Assert.Contains("stored 1 treasures out of 1", score.Text);
        Assert.True(score.Won);
    }

    [Fact]
    public void OtherGamesCompileToAPlayableDat()
    {
        var game = ExampleAdventures.Lighthouse();
        var exporter = new ScottAdamsExporter();
        Assert.Null(exporter.CannotExport(game));
        var back = Import(exporter.Export(game).Data);
        Assert.Equal(game.Rooms.Count, back.Rooms.Count);
        new GameEngine(back, randomSeed: 1).Start();
    }
}
