using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Samples;

namespace AdventureCreator.Tests.Engine;

/// <summary>Plays the main test adventure, "Genesis", from start to finish, and checks its puzzles individually.</summary>
public class GenesisWalkthroughTests
{
    public static readonly string[] Walkthrough =
    {
        "examine the ring", "n", "search the dead soldier", "take the pass and the mask", "wear mask", "e",
        "show the pass to the guard", "n", "quietly go west", "open the drawer", "take keycard", "read memo", "e",
        "d", "unlock the cell door with the keycard", "open cell door", "n", "s", "u",
        "e", "ask ronson about the incubator", "w", "n", "ask davros about the virus", "destroy the tapes",
        "unlock door with keycard", "open door", "n", "plant the charges among the tanks", "connect the wires", "connect the wires",
        "s", "s", "w", "s", "twist the ring",
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
            Assert.True(AdventureCreator.Core.Graphics.PictureRenderer.Render(p, game).Pixels.Distinct().Count() > 3, p.Id);
        foreach (var s in game.Sounds) Assert.True(game.Assets[s.AssetName].Length > 1000);
    }
}
