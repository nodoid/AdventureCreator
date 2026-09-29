using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Samples;

namespace AdventureCreator.Tests.Engine;

/// <summary>Plays the bundled example game from start to finish.</summary>
public class LighthouseWalkthroughTests
{
    public static readonly string[] Walkthrough =
    {
        "look under the boat", "take shell", "n", "e", "talk to tom", "ask tom about the key",
        "take lantern", "open cupboard", "take matches and cloth", "take the oil can", "light the lantern", "w",
        "lift mat", "take key", "unlock door with key", "open door", "n", "up",
        "up", "carefully go up", "pour the oil into the great lamp", "polish the lens with the chamois",
        "light the great lamp", "e",
    };

    [Fact]
    public void CompleteWalkthroughWins()
    {
        var e = new GameEngine(ExampleAdventures.Lighthouse(), randomSeed: 42);
        var log = new System.Text.StringBuilder(e.Start().Text);
        TurnResult? last = null;
        foreach (var cmd in Walkthrough)
        {
            last = e.Submit(cmd);
            log.AppendLine("> " + cmd).AppendLine(last.Text);
            Assert.False(last.GameOver && !last.Won, "Lost unexpectedly:\n" + log);
        }
        Assert.True(last!.Won, log.ToString());
        Assert.Equal(e.Adventure.ComputeMaxScore() - 2, e.State.Score); // befriending Skipper (2 points) is optional
    }

    [Fact]
    public void StairsNeedCareAndLight()
    {
        var e = new GameEngine(ExampleAdventures.Lighthouse(), randomSeed: 1);
        e.Start();
        foreach (var c in new[] { "n", "lift mat", "take key", "unlock door with key", "open door", "n", "up" }) e.Submit(c);
        Assert.Contains("can't see a thing", e.Submit("up").Text);
    }

    [Fact]
    public void CarelessClimbSlips()
    {
        var e = new GameEngine(ExampleAdventures.Lighthouse(), randomSeed: 1);
        e.Start();
        foreach (var c in new[] { "n", "e", "take lantern", "open cupboard", "take matches", "light lantern", "w", "lift mat", "take key", "unlock door with key", "open door", "n", "u" })
            e.Submit(c);
        var r = e.Submit("u");
        Assert.Contains("gives way", r.Text);
        Assert.Equal("stairs", e.State.CurrentRoomId);
        Assert.Contains(r.Events, ev => ev.Kind == OutputKind.PlaySound && ev.Id == "snd_creak");
    }

    [Fact]
    public void ExampleSurvivesPackageRoundTrip()
    {
        var game = ExampleAdventures.Lighthouse();
        var bytes = AdventurePackage.SaveToBytes(game);
        var loaded = AdventurePackage.Load(bytes);
        Assert.Equal(game.Rooms.Count, loaded.Rooms.Count);
        Assert.Equal(game.Triggers.Count, loaded.Triggers.Count);
        Assert.Equal(game.Assets.Count, loaded.Assets.Count);
        var e = new GameEngine(loaded, randomSeed: 42);
        e.Start();
        TurnResult? last = null;
        foreach (var c in Walkthrough) last = e.Submit(c);
        Assert.True(last!.Won);
    }

    [Fact]
    public void HintsProgress()
    {
        var e = new GameEngine(ExampleAdventures.Lighthouse(), randomSeed: 1);
        e.Start();
        e.Submit("n");
        var first = e.Submit("hint").Text;
        var second = e.Submit("hint").Text;
        Assert.NotEqual(first, second);
        Assert.Contains("1/3", first);
    }

    [Fact]
    public void SkipperFollowsAfterABiscuit()
    {
        var e = new GameEngine(ExampleAdventures.Lighthouse(), randomSeed: 5);
        e.Start();
        e.Submit("n. e");
        var dog = e.Adventure.FindItem("dog")!;
        e.SetLoc(dog, "cottage");
        e.Submit("take biscuit");
        Assert.Contains("adoringly", e.Submit("give the biscuit to skipper").Text);
        Assert.Contains("Skipper trots after you", e.Submit("w").Text);
        Assert.Equal("path", e.Loc(dog));
    }

    [Fact]
    public void GullCanBeShooedAway()
    {
        var e = new GameEngine(ExampleAdventures.Lighthouse(), randomSeed: 5);
        e.Start();
        var r = e.Submit("shoo the gull");
        Assert.Contains("flies off", r.Text);
        Assert.Equal("", e.Loc(e.Adventure.FindItem("gull")!));
    }
}
