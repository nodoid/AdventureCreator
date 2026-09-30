using AdventureSystem.Core.Model;
using AdventureSystem.Core.Samples;
using AdventureSystem.Core.ZMachine;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.ZCode;

namespace AdventureSystem.Tests.Importers;

public class ZCodeExporterTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAAEElEQVR4nGP4z8DAwMDAAAAr/wL+2vT1YQAAAABJRU5ErkJggg==";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zcode", name));

    private static Adventure Import(byte[] data, string name) => ImporterRegistry.Run(new ZCodeImporter(), data, name).Adventure;

    [Fact]
    public void StoriesAreWrittenBackByteForByte()
    {
        var story = Fixture("praxix.z5");
        var a = Import(story, "praxix.z5");
        var exporter = Assert.IsType<ZCodeExporter>(ExporterRegistry.Original(a));
        Assert.Null(exporter.CannotExport(a));
        Assert.Equal("z5", exporter.Extension(a));
        var result = exporter.Export(a);
        Assert.Equal(story, result.Data);
        Assert.Contains("version 5", result.Summary);
    }

    [Fact]
    public void StoriesWithPicturesBecomeABlorb()
    {
        var story = Fixture("praxix.z5");
        var a = Import(story, "praxix.z5");
        a.Title = "Praxix & Co";
        a.Assets["images/zpic3.png"] = Convert.FromBase64String(Png);
        a.Pictures.Add(new Picture { Id = "zpic3", Width = 2, Height = 3, BitmapAsset = "images/zpic3.png" });
        a.IntroPictureId = "zpic3";

        var exporter = new ZCodeExporter();
        Assert.Equal("zblorb", exporter.Extension(a));
        var blorb = exporter.Export(a).Data;
        Assert.True(Blorb.IsBlorb(blorb));

        var back = Import(blorb, "praxix.zblorb");
        Assert.Equal(story, back.Assets[back.StoryFile!]);
        var picture = Assert.Single(back.Pictures);
        Assert.Equal(("zpic3", 2, 3), (picture.Id, picture.Width, picture.Height));
        Assert.Equal("zpic3", back.IntroPictureId);   // the frontispiece
    }

    [Fact]
    public void OnlyStoriesCanBeExportedAsZCode()
    {
        var game = ExampleAdventures.Lighthouse();
        Assert.NotNull(new ZCodeExporter().CannotExport(game));

        // ...and a story can't be converted to anything else.
        var story = Import(Fixture("praxix.z5"), "praxix.z5");
        foreach (var exporter in ExporterRegistry.All.Where(e => e is not ZCodeExporter))
            Assert.NotNull(exporter.CannotExport(story));
        Assert.IsType<ZCodeExporter>(ExporterRegistry.For(story).First());
    }
}
