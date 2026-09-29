using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;

namespace AdventureCreator.Tests.Engine;

public class EngineTests
{
    private static Adventure World()
    {
        var a = new Adventure { Title = "Test", StartRoomId = "hall" };
        a.Settings.MaxCarriedItems = 3;
        a.Rooms.Add(new Room { Id = "hall", Name = "Hall", Description = "A grand hall.", Exits = { new Exit { Direction = "north", TargetRoomId = "study", DoorItemId = "door" }, new Exit { Direction = "down", TargetRoomId = "cellar" } } });
        a.Rooms.Add(new Room { Id = "study", Name = "Study", Description = "Books everywhere.", Exits = { new Exit { Direction = "south", TargetRoomId = "hall" } } });
        a.Rooms.Add(new Room { Id = "cellar", Name = "Cellar", Description = "Damp and cold.", IsDark = true, Exits = { new Exit { Direction = "up", TargetRoomId = "hall" } } });
        a.Items.Add(new Item { Id = "door", Name = "study door", Nouns = { "door" }, Adjectives = { "study" }, Location = "hall", Portable = false, Scenery = true, Openable = true, IsOpen = false, Lockable = true, IsLocked = true, KeyItemId = "brasskey" });
        a.Items.Add(new Item { Id = "brasskey", Name = "brass key", Nouns = { "key" }, Adjectives = { "brass" }, Location = "hall" });
        a.Items.Add(new Item { Id = "ironkey", Name = "iron key", Nouns = { "key" }, Adjectives = { "iron" }, Location = "hall" });
        a.Items.Add(new Item { Id = "torch", Name = "torch", Nouns = { "torch", "flashlight" }, LightSource = true, Switchable = true, Location = "hall" });
        a.Items.Add(new Item { Id = "chest", Name = "chest", Nouns = { "chest" }, Container = true, Openable = true, IsOpen = false, Portable = false, Location = "hall" });
        a.Items.Add(new Item { Id = "gem", Name = "gem", Nouns = { "gem", "jewel" }, Adjectives = { "red" }, Location = "chest", ScoreOnTake = 10 });
        a.Items.Add(new Item { Id = "hat", Name = "hat", Nouns = { "hat" }, Wearable = true, Location = "study" });
        a.Items.Add(new Item { Id = "wine", Name = "bottle of wine", Nouns = { "bottle", "wine" }, Location = "cellar" });
        a.Items.Add(new Item { Id = "butler", Name = "butler", Article = "the", Nouns = { "butler", "jeeves" }, IsCharacter = true, Portable = false, Location = "hall",
            Topics = { new Topic { Keywords = { "key", "keys" }, Response = "\"The brass one opens the study, sir.\"" } } });
        a.Variables.Add(new Variable { Name = "bells", InitialValue = 0 });
        return a;
    }

    private static GameEngine Start(Adventure? a = null)
    {
        var e = new GameEngine(a ?? World(), randomSeed: 1);
        e.Start();
        return e;
    }

    [Fact]
    public void StartDescribesRoom()
    {
        var e = new GameEngine(World());
        var r = e.Start();
        Assert.Contains("A grand hall.", r.Text);
        Assert.Contains("Hall", r.Text);
    }

    [Fact]
    public void TakeAndDisambiguate()
    {
        var e = Start();
        var r = e.Submit("take key");
        Assert.Contains("Which do you mean", r.Text);
        r = e.Submit("brass");
        Assert.Contains("Taken", r.Text);
        Assert.Equal(Locations.Carried, e.Loc(e.Adventure.FindItem("brasskey")!));
        // Now only one key is not carried, so "take key" picks the iron key without asking.
        r = e.Submit("take key");
        Assert.Contains("Taken", r.Text);
        Assert.True(e.IsCarried(e.Adventure.FindItem("ironkey")!));
    }

    [Fact]
    public void LockedDoorFlow()
    {
        var e = Start();
        Assert.Contains("locked", e.Submit("n").Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You have nothing to unlock", e.Submit("unlock door").Text);
        e.Submit("take brass key");
        Assert.Contains("You unlock", e.Submit("unlock the study door").Text); // key inferred
        Assert.Contains("You open", e.Submit("open door").Text);
        var r = e.Submit("go north");
        Assert.Contains("Books everywhere", r.Text);
    }

    [Fact]
    public void WrongKey()
    {
        var e = Start();
        e.Submit("take iron key");
        Assert.Contains("doesn't fit", e.Submit("unlock door with iron key").Text);
    }

    [Fact]
    public void ContainersAndScoring()
    {
        var e = Start();
        Assert.Contains("You can't see any such thing", e.Submit("take gem").Text);
        var r = e.Submit("open chest");
        Assert.Contains("gem", r.Text);
        r = e.Submit("take the red gem");
        Assert.Contains("Taken", r.Text);
        Assert.Equal(10, e.State.Score);
        e.Submit("put gem in chest");
        Assert.Equal("chest", e.Loc(e.Adventure.FindItem("gem")!));
        e.Submit("take gem from chest");
        Assert.Equal(10, e.State.Score); // score only once
    }

    [Fact]
    public void TakeAllAndCarryLimit()
    {
        var a = World();
        a.Settings.MaxCarriedItems = 2;
        var e = Start(a);
        var r = e.Submit("take all");
        Assert.Contains("carrying too many", r.Text);
        Assert.Equal(2, e.ItemsAt(Locations.Carried).Count());
    }

    [Fact]
    public void TakeAllExcept()
    {
        var e = Start();
        e.Submit("take all except the torch");
        Assert.False(e.IsCarried(e.Adventure.FindItem("torch")!));
        Assert.True(e.IsCarried(e.Adventure.FindItem("brasskey")!));
    }

    [Fact]
    public void DarknessAndLight()
    {
        var e = Start();
        var r = e.Submit("down");
        Assert.Contains("pitch dark", r.Text);
        Assert.Contains("too dark", e.Submit("take wine").Text);
        e.Submit("up");
        e.Submit("take torch");
        e.Submit("turn on the torch");
        r = e.Submit("d");
        Assert.Contains("Damp and cold", r.Text);
        Assert.Contains("wine", r.Text);
    }

    [Fact]
    public void WearingAndImplicitTake()
    {
        var e = Start();
        e.Submit("take brass key");
        e.Submit("unlock door with key");
        e.Submit("open door");
        e.Submit("n");
        var r = e.Submit("wear hat");
        Assert.Contains("first taking", r.Text);
        Assert.True(e.IsWorn(e.Adventure.FindItem("hat")!));
        Assert.Contains("wearing", e.Submit("i").Text);
    }

    [Fact]
    public void Pronouns()
    {
        var e = Start();
        e.Submit("take the torch");
        var r = e.Submit("drop it");
        Assert.Contains("Dropped", r.Text);
    }

    [Fact]
    public void Conversation()
    {
        var e = Start();
        var r = e.Submit("ask the butler about the keys");
        Assert.Contains("brass one", r.Text);
        r = e.Submit("ask jeeves about the weather");
        Assert.Contains("doesn't seem to know", r.Text);
    }

    [Fact]
    public void OrdersToCharactersGoToTriggers()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t1", Verb = "take", Subject = "butler", Actions = { GameAction.Say("\"Very good, sir.\"") } });
        var e = Start(a);
        Assert.Contains("Very good", e.Submit("butler, take the torch").Text);
        Assert.Contains("Very good", e.Submit("tell the butler to take the torch").Text);
        Assert.Contains("ignores you", e.Submit("butler, go north").Text);
    }

    [Fact]
    public void BeforeCommandTriggerBlocksDefault()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Verb = "take", Noun1 = "torch", Actions = { GameAction.Say("It's glued down!") } });
        var e = Start(a);
        var r = e.Submit("take torch");
        Assert.Contains("glued", r.Text);
        Assert.False(e.IsCarried(a.FindItem("torch")!));
    }

    [Fact]
    public void ContinueLetsDefaultRun()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Verb = "take", Noun1 = "torch", Actions = { GameAction.Say("It's heavier than it looks."), new GameAction(ActionType.Continue) } });
        var e = Start(a);
        var r = e.Submit("take torch");
        Assert.Contains("heavier", r.Text);
        Assert.Contains("Taken", r.Text);
    }

    [Fact]
    public void AfterCommandTrigger()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Event = TriggerEvent.AfterCommand, Verb = "take", Noun1 = "torch", Actions = { GameAction.Say("A bell rings.") } });
        var e = Start(a);
        var r = e.Submit("take torch");
        Assert.True(r.Text.IndexOf("Taken") < r.Text.IndexOf("bell"));
    }

    [Fact]
    public void AdverbTriggers()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "loud", Verb = "open", Noun1 = "chest", Adverb = "loudly|noisily|violently", Actions = { GameAction.Say("The butler winces.") } });
        a.Triggers.Add(new Trigger { Id = "cond", Verb = "open", Noun1 = "chest", Conditions = { new Condition(ConditionType.AdverbUsed, "carefully") }, Actions = { GameAction.Say("You ease it open."), new GameAction(ActionType.Continue) } });
        var e = Start(a);
        Assert.Contains("winces", e.Submit("violently open the chest").Text);
        var r = e.Submit("open the chest carefully");
        Assert.Contains("ease it open", r.Text);
        Assert.Contains("You open", r.Text);
    }

    [Fact]
    public void AdjectiveCondition()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Verb = "examine", Conditions = { new Condition(ConditionType.AdjectiveUsed, "iron") }, Actions = { GameAction.Say("Iron! Excellent.") } });
        var e = Start(a);
        Assert.Contains("Iron! Excellent", e.Submit("examine the iron key").Text);
    }

    [Fact]
    public void TimersAndEveryTurn()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "clock", Event = TriggerEvent.Timer, Interval = 2, Actions = { GameAction.Say("The clock chimes."), new GameAction(ActionType.AddVar, "bells", 1) } });
        a.Triggers.Add(new Trigger { Id = "once", Event = TriggerEvent.Timer, Turn = 3, Actions = { GameAction.Say("A door slams upstairs.") } });
        var e = Start(a);
        Assert.DoesNotContain("chimes", e.Submit("wait").Text);
        Assert.Contains("chimes", e.Submit("wait").Text);
        Assert.Contains("slams", e.Submit("wait").Text);
        e.Submit("z");
        Assert.Equal(2, e.GetVar("bells"));
    }

    [Fact]
    public void RoomEntryTriggerAndOnceOnly()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Event = TriggerEvent.EnterRoom, RoomId = "cellar", OnceOnly = true, Actions = { GameAction.Say("A rat scurries away.") } });
        var e = Start(a);
        Assert.Contains("rat", e.Submit("down").Text);
        e.Submit("up");
        Assert.DoesNotContain("rat", e.Submit("down").Text);
    }

    [Fact]
    public void PuzzlesSolveAndScore()
    {
        var a = World();
        a.Puzzles.Add(new Puzzle { Id = "p", Name = "Open the chest", Points = 5, SolvedWhen = { new Condition(ConditionType.ItemOpen, "chest") }, SolvedMessage = "Puzzle solved!", Hints = { "Try opening it." } });
        var e = Start(a);
        Assert.Contains("Try opening it", e.Submit("hint").Text);
        var r = e.Submit("open chest");
        Assert.Contains("Puzzle solved!", r.Text);
        Assert.Equal(5, e.State.Score);
        Assert.Contains("5 out of a possible", e.Submit("score").Text);
    }

    [Fact]
    public void WinEndsGame()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "w", Verb = "xyzzy", Actions = { new GameAction(ActionType.Win, text: "Magic!") } });
        var e = Start(a);
        var r = e.Submit("xyzzy");
        Assert.True(r.GameOver);
        Assert.True(r.Won);
        Assert.Contains("The game is over", e.Submit("look").Text);
    }

    [Fact]
    public void CustomVerbDefaultResponseAndTrigger()
    {
        var a = World();
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "juggle", Words = { "juggle", "toss about" }, Grammar = { "* {multi}" }, DefaultResponse = "You juggle {the noun1} {adverb}." });
        a.Triggers.Add(new Trigger { Id = "t", Verb = "juggle", Noun1 = "torch", Actions = { GameAction.Say("Not the torch, it's switched on!") } });
        var e = Start(a);
        Assert.Contains("You juggle the brass key expertly", e.Submit("expertly juggle the brass key").Text);
        Assert.Contains("Not the torch", e.Submit("toss about the torch").Text);
    }

    [Fact]
    public void UndoAndAgain()
    {
        var e = Start();
        e.Submit("take torch");
        Assert.True(e.IsCarried(e.Adventure.FindItem("torch")!));
        e.Submit("undo");
        Assert.False(e.IsCarried(e.Adventure.FindItem("torch")!));
        e.Submit("open chest");
        Assert.Contains("already open", e.Submit("g").Text);
    }

    [Fact]
    public void Oops()
    {
        var e = Start();
        var r = e.Submit("take the qqqtorch");
        Assert.Contains("don't know the word", r.Text);
        r = e.Submit("oops torch");
        Assert.Contains("Taken", r.Text);
    }

    [Fact]
    public void SaveAndRestore()
    {
        var e = Start();
        e.Submit("take torch");
        e.Submit("save");
        e.Submit("drop torch");
        e.Submit("restore");
        Assert.True(e.IsCarried(e.Adventure.FindItem("torch")!));
    }

    [Fact]
    public void MultipleCommandsOnOneLine()
    {
        var e = Start();
        var r = e.Submit("take the torch and the iron key then drop the torch. i");
        Assert.True(e.IsCarried(e.Adventure.FindItem("ironkey")!));
        Assert.False(e.IsCarried(e.Adventure.FindItem("torch")!));
        Assert.Contains("You are carrying an iron key", r.Text.Replace("a iron", "an iron"));
    }

    [Fact]
    public void ExitOverridesAndMessagesPlaceholders()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Verb = "push", Noun1 = "chest", Actions = { GameAction.Say("Behind the chest is a passage west."), new GameAction(ActionType.SetExit, "hall", 0, "west", "cellar") } });
        a.Messages[Msg.CantGo] = "No way {direction}!";
        var e = Start(a);
        Assert.Contains("No way west!", e.Submit("west").Text);
        e.Submit("push chest");
        Assert.Contains("pitch dark", e.Submit("w").Text);
    }

    [Fact]
    public void VariablesAndPseudoVariables()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Verb = "wait", Conditions = { new Condition(ConditionType.VarGreater, "@turns", 1) }, Actions = { GameAction.Say("Turns so far: {var:@turns}, room {var:@room}.") } });
        var e = Start(a);
        e.Submit("z");
        e.Submit("z");
        Assert.Contains("Turns so far: 2, room 0", e.Submit("z").Text);
    }

    [Fact]
    public void SoundsAndPicturesAreEmitted()
    {
        var a = World();
        a.Rooms[1].PictureId = "p1";
        a.Rooms[2].SoundId = "s1";
        a.Pictures.Add(new Picture { Id = "p1" });
        a.Sounds.Add(new SoundAsset { Id = "s1" });
        a.Items.First(i => i.Id == "door").IsLocked = false;
        a.Items.First(i => i.Id == "door").IsOpen = true;
        var e = Start(a);
        var r = e.Submit("n");
        Assert.Contains(r.Events, ev => ev.Kind == OutputKind.Picture && ev.Id == "p1");
        e.Submit("s");
        r = e.Submit("d");
        Assert.Contains(r.Events, ev => ev.Kind == OutputKind.PlaySound && ev.Id == "s1" && ev.Loop);
        r = e.Submit("u");
        Assert.Contains(r.Events, ev => ev.Kind == OutputKind.StopSound);
    }
}
