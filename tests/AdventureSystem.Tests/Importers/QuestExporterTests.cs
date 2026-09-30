using System.IO.Compression;
using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Samples;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.Quest;

namespace AdventureSystem.Tests.Importers;

public class QuestExporterTests
{
    private static Adventure Import(byte[] data, string name) => ImporterRegistry.Run(new QuestImporter(), data, name).Adventure;

    private static Adventure RoundTrip(Adventure a, string extension, out ExportResult result)
    {
        var exporter = Assert.IsType<QuestExporter>(ExporterRegistry.Original(a));
        Assert.Null(exporter.CannotExport(a));
        Assert.Equal(extension, exporter.Extension(a));
        result = exporter.Export(a);
        return Import(result.Data, "house." + extension);
    }

    [Fact]
    public void PackagesAreWrittenBackAsPackages()
    {
        var a = RoundTrip(Import(QuestImporterTests.Package(), "house.quest"), "quest", out var result);
        using (var zip = new ZipArchive(new MemoryStream(result.Data)))
            Assert.NotNull(zip.GetEntry("game.aslx"));

        Assert.Equal("The Test House", a.Title);
        Assert.Equal("Tester", a.Author);
        Assert.Equal("Great Hall", a.FindRoom(a.StartRoomId)!.Name);
        var lamp = a.Items.Single(i => i.Nouns.Contains("lantern"));
        Assert.True(lamp.Portable);
        Assert.Equal("A brass lamp.", lamp.Description);
        var picture = Assert.Single(a.Pictures, p => p.BitmapAsset != null);
        Assert.Equal((2, 3), (picture.Width, picture.Height));
    }

    [Fact]
    public void ScriptsStillWorkAfterExport()
    {
        var a = RoundTrip(Import(QuestImporterTests.Aslx(), "house.aslx"), "aslx", out _);
        var e = new GameEngine(a, randomSeed: 1);
        Assert.Contains("Welcome!", e.Start().Text);

        Assert.Contains("You need to be holding it.", e.Submit("rub lamp").Text);
        Assert.Contains("The vault door is locked.", e.Submit("n").Text);
        Assert.Contains("Nothing happens.", e.Submit("say open sesame").Text);
        Assert.Contains("Still nothing.", e.Submit("say open sesame").Text);

        e.Submit("take lamp");
        var rub = e.Submit("polish lantern").Text;
        Assert.Contains("A genie appears!", rub);
        Assert.DoesNotContain("You need to be holding it.", rub);
        Assert.Equal(5, e.State.Score);

        Assert.Contains("The vault door swings open.", e.Submit("say open sesame").Text);
        var vault = e.Submit("n").Text;
        Assert.Contains("The vault is cold. Gold glitters everywhere.", vault.Replace("\n", ""));
    }

    [Fact]
    public void OtherGamesConvertToQuest()
    {
        var game = ExampleAdventures.Lighthouse();
        var exporter = new QuestExporter();
        Assert.Null(exporter.CannotExport(game));
        var result = exporter.Export(game);
        var back = Import(result.Data, "lighthouse." + exporter.Extension(game));
        Assert.Equal(game.Rooms.Count, back.Rooms.Count);
        Assert.Equal(game.FindRoom(game.StartRoomId)!.Name, back.FindRoom(back.StartRoomId)!.Name);
    }
}
