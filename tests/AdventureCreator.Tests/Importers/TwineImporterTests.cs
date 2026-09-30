using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;
using AdventureCreator.Importers.Twine;

namespace AdventureCreator.Tests.Importers;

public class TwineImporterTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAAEElEQVR4nGP4z8DAwMDAAAAr/wL+2vT1YQAAAABJRU5ErkJggg==";

    /// <summary>A published Twine 2 story in Harlowe (passage text is HTML-escaped, as Twine writes it).</summary>
    private static byte[] Harlowe()
    {
        static string P(int pid, string name, string text) =>
            $"<tw-passagedata pid=\"{pid}\" name=\"{name}\" tags=\"\" position=\"0,0\" size=\"100,100\">{System.Net.WebUtility.HtmlEncode(text)}</tw-passagedata>";
        var html = "<html><body><tw-storydata name=\"The Cave\" startnode=\"1\" creator=\"Twine\" format=\"Harlowe\" format-version=\"3.3.8\">"
            + P(1, "Start", "(set: $torch to 0)You are in a cave. ''Cold'' air blows.\n[[Light the torch->Lit]] or [[Leave]]")
            + P(2, "Lit", $"(set: $torch to it + 1)<img src=\"data:image/png;base64,{Png}\">The torch flares.\n(if: $torch is 1)[You can see a [[ladder->Top]].](else:)[It is dark.]")
            + P(3, "Leave", "You leave the cave. THE END")
            + P(4, "Top", "(goto: \"Leave\")")
            + "</tw-storydata></body></html>";
        return System.Text.Encoding.UTF8.GetBytes(html);
    }

    private const string SugarCube = """
:: StoryTitle
Gold Rush

:: StoryData
{"ifid": "D674C58C-DEFA-4F70-B7A2-27742230C0FC", "format": "SugarCube", "start": "Begin"}

:: StoryInit
<<set $gold to 5>>

:: Begin
<<if $gold gt 3>>You are rich. [[Visit the shop->Shop]]<<else>>You are poor.<</if>>
[[Leave|End]]

:: Shop
<<set $gold -= 5>>You spend it all. Gold left: $gold
[[Go home->Begin]]

:: End
Goodbye.
""";

    [Fact]
    public void ReadsHarloweStories()
    {
        var data = Harlowe();
        var importer = ImporterRegistry.Detect(data, "cave.html");
        Assert.IsType<TwineImporter>(importer);
        var a = importer!.Import(data, "cave.html").Adventure;
        Assert.Equal("The Cave", a.Title);
        Assert.Equal("start", a.StartRoomId);
        var start = a.FindRoom("start")!;
        Assert.Contains("You are in a cave. Cold air blows.", start.Description);   // markup removed
        Assert.Contains("1. Light the torch", start.Description);
        Assert.Contains("2. Leave", start.Description);
        Assert.NotNull(a.FindPicture(a.FindRoom("lit")!.PictureId));                   // the embedded image
    }

    [Fact]
    public void HarloweStoriesPlay()
    {
        var a = new TwineImporter().Import(Harlowe(), "cave.html").Adventure;
        var e = new GameEngine(a, randomSeed: 1);
        e.Start();
        var lit = e.Submit("1").Text;
        Assert.Equal("lit", e.State.CurrentRoomId);
        Assert.Contains("The torch flares.", lit);
        Assert.Contains("You can see a ladder.", lit);                                  // (if:) true branch
        Assert.DoesNotContain("It is dark.", lit);
        Assert.Contains("1. ladder", lit);
        e.Submit("1");                                                                   // the ladder: Top jumps to Leave
        Assert.Equal("leave", e.State.CurrentRoomId);
    }

    [Fact]
    public void SugarCubeTweeStoriesPlay()
    {
        var data = System.Text.Encoding.UTF8.GetBytes(SugarCube);
        var importer = ImporterRegistry.Detect(data, "gold.twee");
        Assert.IsType<TwineImporter>(importer);
        var a = importer!.Import(data, "gold.twee").Adventure;
        Assert.Equal("Gold Rush", a.Title);
        Assert.Equal("begin", a.StartRoomId);

        var e = new GameEngine(a, randomSeed: 1);
        var start = e.Start().Text;
        Assert.Contains("You are rich.", start);                                         // StoryInit set $gold to 5
        Assert.DoesNotContain("You are poor.", start);
        var shop = e.Submit("2").Text;                                                   // 1 is Leave; the conditional link comes after
        Assert.Contains("Gold left: 0", shop);                                           // <<set $gold -= 5>> and $gold printed
        var home = e.Submit("1").Text;
        Assert.Contains("You are poor.", home);
        e.Submit("1");
        Assert.Equal("end", e.State.CurrentRoomId);
    }
}
