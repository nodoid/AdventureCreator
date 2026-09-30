using AdventureCreator.Core.Engine;
using AdventureCreator.Core.ZMachine;

namespace AdventureCreator.Tests.ZCode;

public class ZMachineTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zcode", name));

    private static string RunToEnd(ZMachine z, params string[] inputs)
    {
        var all = new System.Text.StringBuilder();
        var queue = new Queue<string>(inputs);
        var stop = z.Run(null);
        all.Append(z.TakeOutput());
        while (stop != ZStop.Quit && queue.Count > 0)
        {
            stop = z.Run(queue.Dequeue());
            all.Append(z.TakeOutput());
        }
        return all.ToString();
    }

    [Fact]
    public void CzechPassesEveryTestAndMatchesTheReferenceOutput()
    {
        var output = RunToEnd(new ZMachine(Fixture("czech.z5"), 1));
        Assert.Contains("Passed: 406, Failed: 0, Print tests: 19", output);
        // Apart from the interpreter's own header details, the output matches the reference exactly.
        static string Normalise(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
        var reference = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zcode", "czech.out5"));
        var ours = Normalise(output);
        var theirs = Normalise(reference);
        int cut(string s) => s.IndexOf("Header(", StringComparison.Ordinal) is var i and >= 0 ? i : s.Length;
        Assert.Equal(theirs[..cut(theirs)], ours[..cut(ours)]);
        int after(string s) => s.IndexOf("Performed", StringComparison.Ordinal);
        Assert.Equal(theirs[after(theirs)..], ours[after(ours)..]);
    }

    [Fact]
    public void PraxixPassesAllTests()
    {
        var output = RunToEnd(new ZMachine(Fixture("praxix.z5"), 1), "all");
        Assert.Contains("All tests passed.", output);
        Assert.DoesNotContain("FAIL", output);
    }

    [Fact]
    public void RecognisesStoryFiles()
    {
        Assert.True(ZStory.IsStory(Fixture("praxix.z5")));
        Assert.False(ZStory.IsStory(new byte[4096]));
        Assert.False(ZStory.IsStory(System.Text.Encoding.ASCII.GetBytes("This is not a story file at all.".PadRight(200))));
    }

    [Fact]
    public void EnginePlaysAStoryWithSaveAndRestore()
    {
        var game = ZStory.CreateAdventure(Fixture("praxix.z5"), "praxix.z5");
        Assert.True(game.IsStory);
        Assert.Equal("Praxix", game.Title);
        var engine = new GameEngine(game, randomSeed: 1);
        var start = engine.Start();
        Assert.Contains("test chamber", start.Text);
        Assert.False(start.Text.TrimEnd().EndsWith('>'));   // the host shows its own prompt

        engine.SaveToSlot("before");
        var inc = engine.Submit("inc");
        Assert.Contains("Passed.", inc.Text);

        var restored = engine.Submit("restore \"before\"");
        Assert.Contains("restored", restored.Text);
        Assert.Contains("Passed.", engine.Submit("shift").Text);

        var quit = engine.Submit("quit");
        Assert.True(engine.IsGameOver);
        Assert.Contains(quit.Events, e => e.Kind == OutputKind.Quit);
    }

    [Fact]
    public void BlorbPicturesAndCoverArtBecomePictures()
    {
        // A Blorb with the story, a 2×3 PNG as picture 1, and picture 1 as the frontispiece.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAAEElEQVR4nGP4z8DAwMDAAAAr/wL+2vT1YQAAAABJRU5ErkJggg==");
        var story = Fixture("praxix.z5");
        var chunks = new List<(string Type, byte[] Data)> { ("ZCOD", story), ("PNG ", png) };
        var blorb = BuildBlorb(chunks, frontispiece: 1);
        Assert.True(ZStory.IsStory(blorb));
        var game = ZStory.CreateAdventure(blorb, "picture_test.zblorb");
        var picture = Assert.Single(game.Pictures);
        Assert.Equal(("zpic1", 2, 3), (picture.Id, picture.Width, picture.Height));
        Assert.Equal("zpic1", game.IntroPictureId);
        Assert.Equal(story, game.Assets[game.StoryFile!]);
    }

    private static byte[] BuildBlorb(List<(string Type, byte[] Data)> chunks, int frontispiece)
    {
        static void BE(List<byte> b, int v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }
        static void Str(List<byte> b, string s) => b.AddRange(System.Text.Encoding.ASCII.GetBytes(s));
        int ridxLength = 4 + chunks.Count * 12;
        int offset = 12 + 8 + ridxLength + 8 + 4;   // after FORM header, RIdx chunk and Fspc chunk
        var index = new List<byte>();
        var body = new List<byte>();
        BE(index, chunks.Count);
        int number = 0, pictureNumber = 1;
        foreach (var (type, data) in chunks)
        {
            Str(index, type == "ZCOD" ? "Exec" : "Pict");
            BE(index, type == "ZCOD" ? number : pictureNumber++);
            BE(index, offset + body.Count);
            Str(body, type);
            BE(body, data.Length);
            body.AddRange(data);
            if ((data.Length & 1) != 0) body.Add(0);
        }
        var file = new List<byte>();
        Str(file, "FORM");
        BE(file, 4 + 8 + ridxLength + 12 + body.Count);
        Str(file, "IFRS");
        Str(file, "RIdx"); BE(file, ridxLength); file.AddRange(index);
        Str(file, "Fspc"); BE(file, 4); BE(file, frontispiece);
        file.AddRange(body);
        return file.ToArray();
    }

    [Fact]
    public void StoryTextLosesTheStoryPrompt()
    {
        Assert.Equal("You are in a room.", GameEngine.CleanStoryText("\nYou are in a room.\n\n>"));
        Assert.Equal("One\n\nTwo", GameEngine.CleanStoryText("One\n\n\n\nTwo\n> "));
    }
}
