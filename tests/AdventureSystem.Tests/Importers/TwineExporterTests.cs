using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Samples;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.Twine;

namespace AdventureSystem.Tests.Importers;

public class TwineExporterTests
{
    private static Adventure Import(byte[] data, string name) => ImporterRegistry.Run(new TwineImporter(), data, name).Adventure;

    private static Adventure RoundTrip(Adventure a, string extension, out ExportResult result)
    {
        var exporter = Assert.IsType<TwineExporter>(ExporterRegistry.Original(a));
        Assert.Null(exporter.CannotExport(a));
        Assert.Equal(extension, exporter.Extension(a));
        result = exporter.Export(a);
        return Import(result.Data, "story." + extension);
    }

    [Fact]
    public void HarloweStoriesStillPlayAfterExport()
    {
        var a = RoundTrip(Import(TwineImporterTests.Harlowe(), "cave.html"), "html", out var result);
        Assert.Contains("Harlowe", result.Summary);
        Assert.Equal("The Cave", a.Title);

        var e = new GameEngine(a, randomSeed: 1);
        e.Start();
        var lit = e.Submit("1").Text;
        Assert.Contains("The torch flares.", lit);
        Assert.Contains("You can see a ladder.", lit);
        Assert.DoesNotContain("It is dark.", lit);
        e.Submit("1");
        Assert.Equal("Leave", a.FindRoom(e.State.CurrentRoomId)!.Name);
    }

    [Fact]
    public void SugarCubeTweeStoriesStillPlayAfterExport()
    {
        var a = RoundTrip(Import(System.Text.Encoding.UTF8.GetBytes(TwineImporterTests.SugarCube), "gold.twee"), "twee", out var result);
        Assert.Contains("SugarCube", result.Summary);
        Assert.Equal("Gold Rush", a.Title);

        var e = new GameEngine(a, randomSeed: 1);
        var start = e.Start().Text;
        Assert.Contains("You are rich.", start);
        Assert.DoesNotContain("You are poor.", start);
        Assert.Contains("Gold left: 0", e.Submit("2").Text);
        Assert.Contains("You are poor.", e.Submit("1").Text);
    }

    [Fact]
    public void ParserGamesExportWithWarnings()
    {
        var game = ExampleAdventures.Lighthouse();
        var exporter = new TwineExporter();
        Assert.Null(exporter.CannotExport(game));
        Assert.Equal("html", exporter.Extension(game));
        var result = exporter.Export(game);
        Assert.Contains(result.Warnings, w => w.Contains("Items can't be carried"));

        var back = Import(result.Data, "lighthouse.html");
        Assert.Equal(game.Rooms.Count, back.Rooms.Count);
        Assert.Equal(game.FindRoom(game.StartRoomId)!.Name, back.FindRoom(back.StartRoomId)!.Name);
    }
}
