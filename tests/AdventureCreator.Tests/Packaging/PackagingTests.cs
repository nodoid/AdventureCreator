using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Samples;
using AdventureCreator.Core.Snapshots;

namespace AdventureCreator.Tests.Packaging;

public class PackagingTests
{
    [Fact]
    public void AppendedPayloadCanBeReadBack()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var fakeExe = Path.Combine(dir.FullName, "player");
            File.WriteAllBytes(fakeExe, new byte[] { 1, 2, 3, 4, 5 });
            var game = ExampleAdventures.Lighthouse();
            var output = Path.Combine(dir.FullName, "game");
            StandaloneExporter.AppendPayload(fakeExe, game, output);
            var loaded = StandaloneExporter.TryReadPayload(output);
            Assert.NotNull(loaded);
            Assert.Equal(game.Title, loaded!.Title);
            Assert.Equal(game.Assets.Count, loaded.Assets.Count);

            // Re-exporting from an exported game replaces the payload rather than stacking another one.
            var again = Path.Combine(dir.FullName, "game2");
            loaded.Title = "Renamed";
            StandaloneExporter.AppendPayload(output, loaded, again);
            Assert.Equal("Renamed", StandaloneExporter.TryReadPayload(again)!.Title);
            Assert.True(new FileInfo(again).Length < new FileInfo(output).Length * 1.5);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void TemplateFolderExport()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var template = Path.Combine(dir.FullName, "template");
            Directory.CreateDirectory(template);
            File.WriteAllText(Path.Combine(template, "AdventureCreator.Player.exe"), "exe");
            var outDir = Path.Combine(dir.FullName, "out");
            var game = ExampleAdventures.Lighthouse();
            var app = StandaloneExporter.ExportFromTemplate(template, game, outDir);
            Assert.True(File.Exists(Path.Combine(app, StandaloneExporter.GameFileName)));
            Assert.True(File.Exists(Path.Combine(app, "The Lighthouse.exe")));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void BuildArgumentsEmbedGame()
    {
        var args = StandaloneExporter.BuildArguments("Player.csproj", "/tmp/game.adventure", "/tmp/out", StandaloneExporter.BuildTarget.Android, ExampleAdventures.Lighthouse());
        Assert.Contains("net10.0-android", args);
        Assert.Contains("EmbeddedGame=", args);
        Assert.Contains("GameId=com.adventurecreator.game.thelighthouse", args);
        Assert.Contains("--artifacts-path", args);

        var sim = StandaloneExporter.BuildArguments("Player.csproj", "g", "/tmp/out", StandaloneExporter.BuildTarget.iOSSimulator, ExampleAdventures.Lighthouse());
        Assert.StartsWith("build ", sim);
        Assert.Contains("--no-incremental", sim);
        Assert.Contains("-r iossimulator-", sim);

        var device = StandaloneExporter.BuildArguments("Player.csproj", "g", "/tmp/out", StandaloneExporter.BuildTarget.iOS, ExampleAdventures.Lighthouse(),
            new StandaloneExporter.BuildOptions { BundleId = "uk.co.example.game", CodesignKey = "Apple Development: X", CodesignProvision = "devel-x" });
        Assert.Contains("-r ios-arm64", device);
        Assert.DoesNotContain("--artifacts-path", device);        // the Apple SDK mis-places the executable with it
        var mac = StandaloneExporter.BuildArguments("Player.csproj", "g", "/tmp/out", StandaloneExporter.BuildTarget.MacCatalyst, ExampleAdventures.Lighthouse());
        Assert.DoesNotContain("--artifacts-path", mac);
        Assert.Contains("GameId=uk.co.example.game", device);
        Assert.Contains("-p:CodesignProvision=\"devel-x\"", device);
    }

    [Fact]
    public void SnaSnapshotLoads()
    {
        var sna = new byte[49179];
        sna[27 + 0x1000] = 0xAB; // address 0x5000
        var s = SpectrumSnapshot.Load(sna, ".sna");
        Assert.Equal(0xAB, s[0x5000]);
    }

    [Fact]
    public void Z80CompressedSnapshotLoads()
    {
        // v1 header (30 bytes) with PC != 0 and compression flag, then an RLE block and end marker.
        var header = new byte[30];
        header[6] = 0x00; header[7] = 0x80; // PC
        header[12] = 0x20; // compressed
        var body = new List<byte> { 0xED, 0xED, 0x10, 0x42, 0x07 };
        body.AddRange(Enumerable.Repeat((byte)0, 49152 - 17));
        body.AddRange(new byte[] { 0x00, 0xED, 0xED, 0x00 });
        var s = SpectrumSnapshot.Load(header.Concat(body).ToArray(), ".z80");
        Assert.Equal(0x42, s[0x4000]);
        Assert.Equal(0x42, s[0x400F]);
        Assert.Equal(0x07, s[0x4010]);
    }
}
