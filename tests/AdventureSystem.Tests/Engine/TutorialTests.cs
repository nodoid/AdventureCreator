using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Tests.Engine;

/// <summary>Builds "The Quiet House" exactly as docs/02-creating-an-adventure.md describes, and plays it.</summary>
public class TutorialTests
{
    public static Adventure QuietHouse()
    {
        var a = new Adventure { Title = "The Quiet House", StartRoomId = "hall",
            Introduction = "Your great-aunt's house has stood empty for years. Tonight, for some reason, you have come back." };
        a.Rooms.Add(new Room { Id = "hall", Name = "Hall", Description = "A dusty hall. A staircase climbs into darkness, a heavy door leads north and the garden door stands open to the east.",
            Exits = { new Exit { Direction = "north", TargetRoomId = "study", DoorItemId = "studydoor" }, new Exit { Direction = "east", TargetRoomId = "garden" }, new Exit { Direction = "up", TargetRoomId = "attic" } } });
        a.Rooms.Add(new Room { Id = "garden", Name = "Garden", Description = "An overgrown garden, silver with frost. A cracked plant pot sits by the wall." });
        a.Rooms.Add(new Room { Id = "study", Name = "Study", Description = "Book-lined walls, a cold fireplace and a writing desk." });
        a.Rooms.Add(new Room { Id = "attic", Name = "Attic", Description = "Rafters, cobwebs and the smell of old paper.", IsDark = true });
        // "Add return exits from destination rooms"
        a.Rooms[1].Exits.Add(new Exit { Direction = "west", TargetRoomId = "hall" });
        a.Rooms[2].Exits.Add(new Exit { Direction = "south", TargetRoomId = "hall", DoorItemId = "studydoor" });
        a.Rooms[3].Exits.Add(new Exit { Direction = "down", TargetRoomId = "hall" });

        a.Items.Add(new Item { Id = "pot", Name = "plant pot", Nouns = { "pot", "plant pot" }, Adjectives = { "cracked", "clay" }, Location = "garden", Portable = false, Scenery = true, Container = true, IsOpen = true, Description = "A cracked clay pot full of frozen soil." });
        a.Items.Add(new Item { Id = "key", Name = "iron key", Nouns = { "key" }, Adjectives = { "iron", "small" }, Location = "pot" });
        a.Items.Add(new Item { Id = "studydoor", Name = "study door", Nouns = { "door" }, Adjectives = { "study", "heavy", "oak" }, Location = "hall", Portable = false, Scenery = true, Openable = true, IsOpen = false, Lockable = true, IsLocked = true, KeyItemId = "key" });
        a.Items.Add(new Item { Id = "candle", Name = "candle", Nouns = { "candle" }, Adjectives = { "wax", "white" }, Location = "study", LightSource = true, RoomDescription = "A single white candle stands on the desk." });
        a.Items.Add(new Item { Id = "locket", Name = "silver locket", Nouns = { "locket" }, Adjectives = { "silver", "tarnished" }, Location = "attic", ScoreOnTake = 5, Description = "Inside is a faded photograph of a young woman, and a lock of hair." });
        a.Items.Add(new Item { Id = "ghost", Name = "ghost", Article = "the", Nouns = { "ghost", "woman", "spirit", "aunt" }, Adjectives = { "pale", "grey" }, Location = "study", IsCharacter = true, Portable = false,
            Description = "A pale, grey woman, drifting a little above the floor. She looks terribly sad.", RoomDescription = "A pale ghost hovers by the fireplace.",
            Topics =
            {
                new Topic { Keywords = { "hello", "talk" }, Response = "\"You came back,\" she whispers. \"I lost something, long ago. Up there, in the dark.\"" },
                new Topic { Keywords = { "locket", "photograph", "lost" }, Response = "\"It was mine. Please... bring it to me.\"" },
                new Topic { Keywords = { "attic", "dark" }, Response = "\"The attic. Take a light.\"" },
            } });

        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "comfort", Words = { "comfort", "console", "soothe", "reassure" }, Grammar = { "* {person}", "* {person} with {held2}" },
            DefaultResponse = "You murmur a few kind words. Nothing seems to change." });
        a.Triggers.Add(new Trigger { Id = "t1", Name = "Comfort gently", Verb = "comfort", Noun1 = "ghost", Adverb = "gently|softly|kindly|tenderly",
            Actions = { GameAction.Say("The ghost's face softens. \"Thank you,\" she breathes. \"You were always kind.\"") } });

        a.Variables.Add(new Variable { Name = "rested" });
        a.Triggers.Add(new Trigger { Id = "t2", Name = "Give the locket", Verb = "give|show", Noun1 = "locket", Noun2 = "ghost",
            Conditions = { new Condition(ConditionType.ItemCarried, "locket") },
            Actions =
            {
                GameAction.Say("The ghost takes the locket in hands that are almost not there. For a moment she is young again, smiling. Then she is simply gone."),
                new GameAction(ActionType.DestroyItem, "ghost"), new GameAction(ActionType.DestroyItem, "locket"), new GameAction(ActionType.SetVar, "rested", 1),
            } });
        a.Puzzles.Add(new Puzzle { Id = "pz_rest", Name = "Lay the ghost to rest", Points = 20, SolvedWhen = { new Condition(ConditionType.VarEquals, "rested", 1) },
            HintRoomIds = { "study", "attic" }, Hints = { "The ghost lost something.", "It's somewhere dark.", "Light the candle, go up to the attic, and give the locket to the ghost." } });
        a.Triggers.Add(new Trigger { Id = "t3", Name = "The house is quiet", Event = TriggerEvent.PuzzleSolved, Subject = "pz_rest",
            Actions = { new GameAction(ActionType.Win, text: "The house sighs, and settles, and is quiet at last.") } });
        return a;
    }

    [Fact]
    public void TutorialWalkthroughWins()
    {
        var e = new GameEngine(QuietHouse(), randomSeed: 1);
        var log = new System.Text.StringBuilder(e.Start().Text);
        TurnResult last = new();
        foreach (var line in new[]
        {
            "e. look in pot. take key. w", "unlock the door with the key then open it",
            "n. talk to the ghost. ask her about the locket", "take the candle and light it",
            "s. u. take the locket. d. n", "gently comfort the ghost", "give the locket to the ghost",
        })
        {
            last = e.Submit(line);
            log.AppendLine("> " + line).AppendLine(last.Text);
        }
        var text = log.ToString();
        Assert.True(last.Won, text);
        Assert.Contains("I lost something", text);
        Assert.True(text.Contains("It was mine"), text);
        Assert.Contains("You were always kind", text);
        Assert.Equal(25, e.State.Score);
        Assert.Equal(25, e.Adventure.ComputeMaxScore());
        Assert.DoesNotContain(AdventureValidator.Validate(e.Adventure), i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void DefaultResponseWithoutAdverbAndParserLabOutput()
    {
        var e = new GameEngine(QuietHouse());
        e.Start();
        e.Submit("e. look in pot. take key. w. unlock door with key. open door. n");
        Assert.Contains("Nothing seems to change", e.Submit("comfort the ghost").Text);
        Assert.Equal("comfort [gently] obj1=the pale ghost", e.Parser.Parse("gently comfort the pale ghost").Commands[0].ToString());
    }

    [Fact]
    public void AtticIsDarkWithoutCandle()
    {
        var e = new GameEngine(QuietHouse());
        e.Start();
        Assert.Contains("pitch dark", e.Submit("u").Text);
    }
}
