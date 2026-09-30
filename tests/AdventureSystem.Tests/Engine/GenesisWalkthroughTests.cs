using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Packaging;
using AdventureSystem.Core.Samples;

namespace AdventureSystem.Tests.Engine;

/// <summary>Plays the main test adventure, "Genesis", from start to finish, and checks its puzzles individually.</summary>
public class GenesisWalkthroughTests
{
    public static readonly string[] Walkthrough =
    {
        "examine the ring", "n", "search the dead soldier", "take the pass and the mask", "wear mask", "e",
        "show the pass to the guard", "n", "quietly go west", "open the drawer", "take keycard", "read memo", "e",
        "d", "unlock the cell door with the keycard", "open cell door", "n", "s", "u",
        "e", "ask ronson about the incubator", "w", "n", "ask davros about the virus",
        "unlock door with keycard", "open door", "n", "plant the charges among the tanks", "connect the wires", "connect the wires",
        "destroy the tapes", "s", "s", "w", "s", "twist the ring",
    };

    private static (GameEngine Engine, string Log, TurnResult Last) Play(IEnumerable<string> commands, Adventure? game = null)
    {
        var e = new GameEngine(game ?? ExampleAdventures.Genesis(), randomSeed: 7);
        var log = new System.Text.StringBuilder(e.Start().Text);
        TurnResult last = new();
        foreach (var c in commands)
        {
            last = e.Submit(c);
            log.AppendLine("> " + c).AppendLine(last.Text);
        }
        return (e, log.ToString(), last);
    }

    [Fact]
    public void CompleteWalkthroughWinsWithFullScore()
    {
        var (e, log, last) = Play(Walkthrough);
        Assert.True(last.Won, log);
        Assert.Equal(e.Adventure.ComputeMaxScore(), e.State.Score);
    }

    [Fact]
    public void GasKillsWithoutMask()
    {
        var (_, log, last) = Play(new[] { "n", "e" });
        Assert.True(last.GameOver);
        Assert.False(last.Won);
        Assert.Contains("yellow-green mist closes over you", log);
    }

    [Fact]
    public void UndoAfterDeath()
    {
        var e = new GameEngine(ExampleAdventures.Genesis(), randomSeed: 1);
        e.Start();
        e.Submit("n");
        Assert.True(e.Submit("e").GameOver);
        e.Submit("undo");
        Assert.False(e.IsGameOver);
        Assert.Equal("trench", e.State.CurrentRoomId);
    }

    [Fact]
    public void GuardNeedsThePass()
    {
        var (e, log, _) = Play(new[] { "n", "take mask", "wear it", "e", "n" });
        Assert.Contains("Show me your pass", log);
        Assert.Equal("entrance", e.State.CurrentRoomId);
    }

    [Fact]
    public void SentryIsAnAdverbPuzzle()
    {
        var prefix = new[] { "n", "search soldier", "take pass and mask", "wear mask", "e", "show pass to guard", "n" };
        var (e1, log1, _) = Play(prefix.Append("w"));
        Assert.Contains("Where do you think you're going", log1);
        Assert.Equal("corridor", e1.State.CurrentRoomId); // caught and sent back

        var (e2, _, _) = Play(prefix.Append("go west stealthily"));
        Assert.Equal("office", e2.State.CurrentRoomId);

        var (e3, log3, _) = Play(prefix.Append("casually walk west"));
        Assert.Contains("isn't *that* asleep", log3);
        Assert.Equal("corridor", e3.State.CurrentRoomId);
    }

    [Fact]
    public void TheChoiceNeedsTwoAttempts()
    {
        var steps = Walkthrough.TakeWhile(c => c != "connect the wires").Append("connect the wires").ToList();
        var (e, log, _) = Play(steps);
        Assert.Contains("wires tremble", log);
        Assert.Equal(0, e.GetVar("boom"));
    }

    [Fact]
    public void RingWontWorkEarly()
    {
        var (_, log, last) = Play(new[] { "twist ring" });
        Assert.False(last.GameOver);
        Assert.Contains("The ring stays cold", log);
    }

    [Fact]
    public void ConversationGivesCharges()
    {
        var prefix = new[] { "n", "search soldier", "take pass and mask", "wear mask", "e", "show pass to guard", "n", "e", "ask ronson about explosives" };
        var (e, _, _) = Play(prefix);
        Assert.True(e.IsCarried(e.Adventure.FindItem("charges")!));
        Assert.Contains("That's all I have", Play(prefix.Append("ask ronson about the charges")).Log);
    }

    [Fact]
    public void GameIsValidAndSurvivesPackaging()
    {
        var game = ExampleAdventures.Genesis();
        Assert.DoesNotContain(AdventureValidator.Validate(game), i => i.Severity == IssueSeverity.Error);
        var loaded = AdventurePackage.Load(AdventurePackage.SaveToBytes(game));
        Assert.True(Play(Walkthrough, loaded).Last.Won);
    }

    [Fact]
    public void PicturesRenderAndSoundsExist()
    {
        var game = ExampleAdventures.Genesis();
        foreach (var r in game.Rooms) Assert.NotNull(game.FindPicture(r.PictureId));
        foreach (var p in game.Pictures)
            Assert.True(AdventureSystem.Core.Graphics.PictureRenderer.Render(p, game).Pixels.Distinct().Count() > 3, p.Id);
        foreach (var s in game.Sounds) Assert.True(game.Assets[s.AssetName].Length > 1000);
    }

    [Fact]
    public void WalkthroughWinsWhateverTheDiceDo()
    {
        // Random events (shells, mines, power cuts) and the Dalek differ with every seed; the walkthrough must always win.
        for (int seed = 0; seed < 100; seed++)
        {
            var e = new GameEngine(ExampleAdventures.Genesis(), randomSeed: seed);
            e.Start();
            TurnResult last = new();
            foreach (var c in Walkthrough) last = e.Submit(c);
            Assert.True(last.Won, $"seed {seed}");
            Assert.Equal(1, e.GetVar("reunited"));
        }
    }

    [Fact]
    public void HarryJoinsAndFollowsThroughTheGas()
    {
        var (e, log, _) = Play(new[] { "n", "talk to harry", "search soldier", "take pass and mask", "wear mask", "e" });
        Assert.Contains("I'll stick with you", log);
        Assert.Equal("entrance", e.Loc(e.Adventure.FindItem("harry")!));
        Assert.Contains("respirator of his own", log);
        var r = e.Submit("harry, wait");
        Assert.Contains("Right-ho", r.Text);
        e.Submit("show pass to guard. n");
        Assert.Equal("entrance", e.Loc(e.Adventure.FindItem("harry")!));
    }

    [Fact]
    public void SarahWalksBackThroughTheExits()
    {
        var steps = Walkthrough.TakeWhile(c => c != "s").Append("s").ToList(); // free Sarah, step back into the cell block
        var e = new GameEngine(ExampleAdventures.Genesis(), randomSeed: 3);
        e.Start();
        foreach (var c in steps) e.Submit(c);
        var sarah = e.Adventure.FindItem("sarah")!;
        var path = new List<string> { e.Loc(sarah) };
        for (int i = 0; i < 8; i++)
        {
            e.Submit("wait");
            var now = e.Loc(sarah);
            if (now != path[^1])
            {
                Assert.Contains(e.ExitsOf(path[^1]), x => x.TargetRoomId == now); // only ever through an exit
                path.Add(now);
            }
        }
        Assert.Equal("wasteland", path[^1]);
        Assert.Equal(new[] { "corridor", "entrance", "trench", "wasteland" }, path.SkipWhile(r => r != "corridor").ToArray());
    }

    [Fact]
    public void DalekWakesAndCannotUseStairs()
    {
        var upToTapes = Walkthrough.TakeWhile(c => c != "destroy the tapes").Append("destroy the tapes").ToList();
        var (e, log, _) = Play(upToTapes);
        Assert.Contains("EX-TER-MIN-ATE", log);
        var dalek = e.Adventure.FindItem("dalek")!;
        Assert.Equal("davroslab", e.Loc(dalek));
        e.Submit("s");
        e.Submit("d");                               // escape down to the cells
        for (int i = 0; i < 10; i++) e.Submit("wait");
        Assert.NotEqual("cellblock", e.Loc(dalek));  // it can't follow down the stairs
        Assert.False(e.IsGameOver);
    }

    [Fact]
    public void RonsonIsArrestedOnlyAfterHelping()
    {
        var e = new GameEngine(ExampleAdventures.Genesis(), randomSeed: 11);
        e.Start();
        for (int i = 0; i < 40; i++) e.Submit("wait");
        Assert.Equal("lab", e.Loc(e.Adventure.FindItem("ronson")!));
        Assert.False(e.State.EventCounts.ContainsKey("gev_arrest"));
    }
}
