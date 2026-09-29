using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;

namespace AdventureCreator.Tests.Engine;

public class NpcAndEventTests
{
    /// <summary>
    ///            tower
    ///              | up (troll blocks)
    ///   garden — pond        vault (no exits at all)
    ///     | north
    ///   hall — kitchen (east)
    ///     | down (locked cellar door)
    ///   cellar
    /// </summary>
    private static Adventure World()
    {
        var a = new Adventure { StartRoomId = "hall" };
        a.Settings.PlayerHealth = 5;
        a.Settings.AutoListExits = false;
        Room R(string id, params (string Dir, string To, string? Door)[] exits)
        {
            var r = new Room { Id = id, Name = char.ToUpper(id[0]) + id[1..], Description = $"The {id}." };
            foreach (var (d, t, door) in exits) r.Exits.Add(new Exit { Direction = d, TargetRoomId = t, DoorItemId = door });
            a.Rooms.Add(r);
            return r;
        }
        R("hall", ("east", "kitchen", null), ("north", "garden", null), ("down", "cellar", "cellardoor"));
        R("kitchen", ("west", "hall", null));
        R("garden", ("south", "hall", null), ("up", "tower", null), ("east", "pond", null));
        R("tower", ("down", "garden", null));
        R("pond", ("west", "garden", null));
        R("cellar", ("up", "hall", "cellardoor"));
        R("vault");

        a.Items.Add(new Item { Id = "cellardoor", Name = "cellar door", Nouns = { "door" }, Location = "hall", Portable = false, Scenery = true, Openable = true, IsOpen = false, Lockable = true, IsLocked = true, KeyItemId = "key" });
        a.Items.Add(new Item { Id = "key", Name = "key", Location = "hall" });
        a.Items.Add(new Item { Id = "bone", Name = "bone", Location = "hall" });
        a.Items.Add(new Item { Id = "gold", Name = "gold coin", Nouns = { "coin", "gold" }, Location = "hall" });
        a.Items.Add(new Item { Id = "ring", Name = "ring", Location = "hall" });
        a.Items.Add(new Item { Id = "sword", Name = "sword", Location = "hall", Damage = 2 });
        a.Items.Add(new Item { Id = "boat", Name = "boat", Nouns = { "boat", "dinghy" }, Location = "kitchen", AllowsWater = true });
        a.Items.Add(new Item { Id = "cup", Name = "cup", Location = "pond" });

        Item Npc(string id, string room, NpcBehaviour b, params string[] nouns)
        {
            var i = new Item { Id = id, Name = id, Article = "the", Nouns = nouns.Length > 0 ? nouns.ToList() : new List<string> { id }, Location = room, IsCharacter = true, Portable = false, Npc = b };
            a.Items.Add(i);
            return i;
        }
        Npc("dog", "vault", new NpcBehaviour { Movement = NpcMovement.Wander, MoveChance = 100, Wants = { "bone" }, FollowsWhenGiven = true, AcceptMessage = "The dog wolfs down {the noun1} and wags its tail." });
        Npc("troll", "garden", new NpcBehaviour { BlocksExits = { "up" }, BlockMessage = "The troll growls and bars the stairs.", Wants = { "gold" }, PacifiedWhenGiven = true });
        Npc("goblin", "kitchen", new NpcBehaviour { Hostile = true, AttackChance = 100, Damage = 2, Health = 3, AttackMessage = "The goblin claws at you!", KillMessage = "The goblin finishes you off.", DefeatMessage = "The goblin flees, howling." });
        Npc("robot", "vault", new NpcBehaviour { ObeysOrders = true });
        Npc("cat", "cellar", new NpcBehaviour { OpensDoors = true });
        Npc("magpie", "vault", new NpcBehaviour { StealChance = 100, StealsItems = { "ring" }, Health = 1, DefeatMessage = "The magpie squawks off, dropping its loot." }, "magpie", "bird");
        return a;
    }

    private static GameEngine Start(Adventure? a = null, int seed = 3)
    {
        var e = new GameEngine(a ?? World(), randomSeed: seed);
        e.Start();
        return e;
    }

    private static void Put(GameEngine e, string id, string loc) => e.SetLoc(e.Adventure.FindItem(id)!, loc);

    // ------------------------------------------------------------ movement only through exits

    [Fact]
    public void NpcInRoomWithoutExitsNeverLeaves()
    {
        var e = Start();
        for (int i = 0; i < 40; i++) e.Submit("wait");
        Assert.Equal("vault", e.Loc(e.Adventure.FindItem("dog")!));
    }

    [Fact]
    public void WanderingNpcOnlyMovesToConnectedRooms()
    {
        var e = Start();
        var dog = e.Adventure.FindItem("dog")!;
        Put(e, "dog", "hall");
        var previous = "hall";
        int moves = 0;
        for (int i = 0; i < 60; i++)
        {
            e.Submit("wait");
            var now = e.Loc(dog);
            if (now != previous)
            {
                moves++;
                Assert.Contains(e.ExitsOf(previous), x => x.TargetRoomId == now);
                previous = now;
            }
        }
        Assert.True(moves > 10);
    }

    [Fact]
    public void LockedDoorStopsNpcThenOpenDoorLetsItThrough()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "call", Verb = "xyzzy", Actions = { new GameAction(ActionType.NpcGoTo, "cat", b: "hall") } });
        var e = Start(a);
        e.Submit("xyzzy");
        for (int i = 0; i < 5; i++) e.Submit("wait");
        Assert.Equal("cellar", e.Loc(a.FindItem("cat")!)); // locked door: no route

        e.Submit("take key. unlock door with key");
        var r = e.Submit("xyzzy");        // the cat sets off at the end of this turn
        Assert.Equal("hall", e.Loc(a.FindItem("cat")!));
        Assert.Contains("opens the cellar door", r.Text);   // closed but unlocked: it may open doors
        Assert.Contains("The cat arrives from below", r.Text);
    }

    [Fact]
    public void OrdersToGoOnlyWorkThroughRealExits()
    {
        var e = Start();
        Put(e, "robot", "hall");
        Assert.Contains("can't go that way", e.Submit("robot, go west").Text);
        Assert.Equal("hall", e.Loc(e.Adventure.FindItem("robot")!));
        var r = e.Submit("tell the robot to go east");
        Assert.Contains("leaves, heading east", r.Text);
        Assert.Equal("kitchen", e.Loc(e.Adventure.FindItem("robot")!));
    }

    [Fact]
    public void PatrolFollowsRouteThroughExits()
    {
        var a = World();
        a.Items.Add(new Item { Id = "guard", Name = "guard", Location = "kitchen", IsCharacter = true, Portable = false, Npc = new NpcBehaviour { Movement = NpcMovement.Patrol, Route = { "kitchen", "pond" } } });
        var e = Start(a);
        var visited = new List<string>();
        for (int i = 0; i < 12; i++) { e.Submit("wait"); visited.Add(e.Loc(a.FindItem("guard")!)); }
        // kitchen -> hall -> garden -> pond -> garden -> hall -> kitchen ...
        Assert.Equal(new[] { "hall", "garden", "pond", "garden", "hall", "kitchen" }, visited.Take(6));
    }

    [Fact]
    public void SeekerHuntsThePlayer()
    {
        var a = World();
        a.Items.Add(new Item { Id = "hound", Name = "hound", Location = "pond", IsCharacter = true, Portable = false, Npc = new NpcBehaviour { Movement = NpcMovement.Seek, MoveChance = 100 } });
        var e = Start(a);
        e.Submit("wait");                 // pond -> garden
        var r = e.Submit("wait");         // garden -> hall
        Assert.Equal("hall", e.Loc(a.FindItem("hound")!));
        Assert.Contains("arrives from the north", r.Text);
    }

    // ------------------------------------------------------------ interaction

    [Fact]
    public void GivingWantedItemMakesNpcFollow()
    {
        var e = Start();
        Put(e, "dog", "hall");
        e.Adventure.FindItem("dog")!.Npc!.Movement = NpcMovement.Stationary;
        e.Submit("take bone");
        Assert.Contains("wolfs down the bone", e.Submit("give bone to dog").Text);
        var r = e.Submit("n");
        Assert.Contains("follows you", r.Text);
        Assert.Equal("garden", e.Loc(e.Adventure.FindItem("dog")!));
        Assert.Contains("doesn't want", e.Submit("s. take ring. give ring to dog").Text); // the dog follows back to the hall
    }

    [Fact]
    public void BlockingNpcIsPacifiedByGift()
    {
        var e = Start();
        e.Submit("take coin. n");
        Assert.Contains("bars the stairs", e.Submit("up").Text);
        Assert.Equal("garden", e.State.CurrentRoomId);
        e.Submit("give coin to troll");
        e.Submit("up");
        Assert.Equal("tower", e.State.CurrentRoomId);
    }

    [Fact]
    public void HostileNpcHurtsAndCanBeDefeated()
    {
        var e = Start();
        e.Submit("take sword");
        var r = e.Submit("e");
        Assert.Contains("claws at you", r.Text);
        Assert.Equal(3, e.State.Health);
        r = e.Submit("attack the goblin with the sword");
        Assert.Contains("goblin flees", r.Text);
        Assert.True(e.Adventure.FindItem("goblin") is { } g && e.NpcStateOf(g).Defeated);
        Assert.Contains("3 out of 5", e.Submit("diagnose").Text);
    }

    [Fact]
    public void NpcCanKillThePlayer()
    {
        var a = World();
        a.Settings.PlayerHealth = 2;
        var e = Start(a);
        var r = e.Submit("e");
        Assert.True(r.GameOver);
        Assert.Contains("The goblin finishes you off", r.Text);
    }

    [Fact]
    public void ThiefStealsAndWontGiveBack()
    {
        var e = Start();
        e.Submit("take ring");
        Put(e, "magpie", "hall");
        var r = e.Submit("wait");
        Assert.Contains("snatches the ring", r.Text);
        Assert.Equal("magpie", e.Loc(e.Adventure.FindItem("ring")!));
        Assert.Contains("carrying a ring", e.Submit("look").Text);
        Assert.Contains("won't let you have", e.Submit("take ring").Text);
        Assert.Contains("refuses", e.Submit("ask magpie for ring").Text);
        e.Submit("attack bird");
        Assert.Equal("hall", e.Loc(e.Adventure.FindItem("ring")!));
    }

    [Fact]
    public void PlayerCanFollowAnNpcThatLeft()
    {
        var e = Start();
        Put(e, "robot", "hall");
        e.Submit("robot, go north");
        e.Submit("follow robot");
        Assert.Equal("garden", e.State.CurrentRoomId);
    }

    [Fact]
    public void NpcGreetsOnce()
    {
        var a = World();
        a.FindItem("troll")!.Npc!.GreetingMessage = "\"Who goes there?\" rumbles the troll.";
        var e = Start(a);
        Assert.Contains("Who goes there", e.Submit("n").Text);
        e.Submit("s");
        Assert.DoesNotContain("Who goes there", e.Submit("n").Text);
    }

    [Fact]
    public void NpcTriggersFire()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Event = TriggerEvent.NpcArrives, Subject = "robot", Actions = { GameAction.Say("Beep boop.") } });
        a.Triggers.Add(new Trigger { Id = "call", Verb = "xyzzy", Actions = { new GameAction(ActionType.NpcGoTo, "robot", b: "hall") } });
        var e = Start(a);
        Put(e, "robot", "garden");
        Assert.Contains("Beep boop", e.Submit("xyzzy").Text);
    }

    // ------------------------------------------------------------ random events, flooding, traps, light

    [Fact]
    public void RandomEventFloodsAwayFromPlayer()
    {
        var a = World();
        a.RandomEvents.Add(new RandomEvent
        {
            Id = "flood", Chance = 100, Where = EventLocation.AwayFromPlayer, Rooms = { "pond" }, MaxTimes = 1,
            DistantMessage = "Somewhere, water gushes.", Actions = { new GameAction(ActionType.Flood, Locations.EventRoom, 1) },
        });
        var e = Start(a);
        Assert.Contains("water gushes", e.Submit("n").Text);
        Assert.Contains("flooded", e.Submit("e").Text);
        Assert.Equal("garden", e.State.CurrentRoomId);
    }

    [Fact]
    public void BoatLetsPlayerIntoFloodedRoom()
    {
        var a = World();
        a.Items.Remove(a.FindItem("goblin")!);
        a.Triggers.Add(new Trigger { Id = "t", Verb = "xyzzy", Actions = { new GameAction(ActionType.Flood, "pond", 1) } });
        var e = Start(a);
        e.Submit("xyzzy. e. take boat. w. n. e");
        Assert.Equal("pond", e.State.CurrentRoomId);
        Assert.Contains("Water fills this place", e.Submit("look").Text);
    }

    [Fact]
    public void FloodingPlayersRoomSweepsThemOut()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Verb = "xyzzy", Actions = { new GameAction(ActionType.Flood, Locations.Here, 1) } });
        var e = Start(a);
        e.Submit("n. e");
        var r = e.Submit("xyzzy");
        Assert.Contains("sweeps you away to Garden", r.Text);
        Assert.Equal("garden", e.State.CurrentRoomId);
        // A room with no exits: drowned.
        var b = World();
        b.Rooms.First(x => x.Id == "vault").Exits.Clear();
        b.StartRoomId = "vault";
        b.Triggers.Add(new Trigger { Id = "t", Verb = "xyzzy", Actions = { new GameAction(ActionType.Flood, Locations.Here, 1) } });
        Assert.True(Start(b).Submit("xyzzy").GameOver);
    }

    [Fact]
    public void RandomEventCanMoveAndCreateItemsAndSwitchLights()
    {
        var a = World();
        a.Items.Add(new Item { Id = "rock", Name = "rock", Location = "" });
        a.RandomEvents.Add(new RandomEvent
        {
            Id = "rockfall", Chance = 100, Where = EventLocation.PlayerRoom, MaxTimes = 1, WitnessMessage = "Rocks tumble from the ceiling!",
            Actions = { new GameAction(ActionType.CreateItem, "rock"), new GameAction(ActionType.MoveItem, "$randomitem", b: "tower"), new GameAction(ActionType.SetDark, Locations.EventRoom, 1) },
        });
        var e = Start(a);
        var r = e.Submit("wait");
        Assert.Contains("Rocks tumble", r.Text);
        Assert.Contains("pitch dark", r.Text);
        Assert.True(e.IsDark());
        Assert.Contains(e.Adventure.Items, i => e.Loc(i) == "tower");
        Assert.Equal(1, e.State.EventCounts["rockfall"]);
    }

    [Fact]
    public void EventRespectsConditionsCooldownAndLimits()
    {
        var a = World();
        a.Variables.Add(new Variable { Name = "storm" });
        a.RandomEvents.Add(new RandomEvent { Id = "thunder", Chance = 100, Where = EventLocation.Global, Cooldown = 3, MaxTimes = 2,
            Conditions = { new Condition(ConditionType.VarEquals, "storm", 1) }, WitnessMessage = "Thunder!" });
        a.Triggers.Add(new Trigger { Id = "s", Verb = "xyzzy", Actions = { new GameAction(ActionType.SetVar, "storm", 1) } });
        var e = Start(a);
        Assert.DoesNotContain("Thunder", e.Submit("wait").Text);
        var log = e.Submit("xyzzy").Text;
        for (int i = 0; i < 10; i++) log += e.Submit("wait").Text;
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(log, "Thunder!").Count);
    }

    [Fact]
    public void TrapsSenseSpringRevealAndDisarm()
    {
        var a = World();
        a.Triggers.Add(new Trigger { Id = "t", Event = TriggerEvent.GameStart, Actions = { new GameAction(ActionType.SetTrap, "kitchen", 2, b: "You spot a tripwire across the floor.", text: "A tripwire! Darts fly at you.") } });
        a.Items.Remove(a.FindItem("goblin")!);
        var e = Start(a);
        Assert.Contains("uneasy feeling", e.Submit("e").Text);
        var r = e.Submit("take boat");
        Assert.Contains("Darts fly", r.Text);
        Assert.Equal(3, e.State.Health);

        var e2 = Start(a);
        e2.Submit("e");
        Assert.Contains("tripwire across the floor", e2.Submit("search").Text);
        e2.Submit("take boat");
        Assert.Equal(5, e2.State.Health);
        Assert.Contains("disarm", e2.Submit("disarm trap").Text);
        Assert.False(e2.State.Traps.ContainsKey("kitchen"));
    }

    [Fact]
    public void DeadlyTrapKills()
    {
        var a = World();
        a.Items.Remove(a.FindItem("goblin")!);
        a.Triggers.Add(new Trigger { Id = "t", Event = TriggerEvent.GameStart, Actions = { new GameAction(ActionType.SetTrap, "kitchen", -1, text: "The floor gives way into a spiked pit.") } });
        var e = Start(a);
        e.Submit("e");
        Assert.True(e.Submit("jump").GameOver);
    }

    // ------------------------------------------------------------ saving and loading

    [Fact]
    public void NamedSaveSlots()
    {
        var e = Start();
        e.Submit("take ring");
        Assert.Contains("Game saved", e.Submit("save \"with ring\"").Text);
        e.Submit("n");
        e.Submit("save garden");
        var saves = e.ListSaves();
        Assert.Equal(2, saves.Count);
        Assert.Contains(saves, s => s.Name == "garden" && s.RoomName == "Garden");
        var r = e.Submit("restore \"with ring\"");
        Assert.Contains("Game restored", r.Text);
        Assert.Equal("hall", e.State.CurrentRoomId);
        Assert.Contains("Saved games", e.Submit("restore").Text);
        Assert.Contains("no saved game called", e.Submit("restore nothing").Text);
    }

    [Fact]
    public void HostDialogsAndAutosave()
    {
        var e = Start();
        e.HostHandlesSaveDialogs = true;
        Assert.Contains(e.Submit("save").Events, ev => ev.Kind == OutputKind.SaveRequested);
        Assert.Contains(e.Submit("load").Events, ev => ev.Kind == OutputKind.RestoreRequested);
        e.Submit("n");
        e.Autosave();
        Assert.True(e.HasSave(GameEngine.AutosaveSlot));
        e.Submit("s");
        e.Submit("restore autosave");
        Assert.Equal("garden", e.State.CurrentRoomId);
        Assert.DoesNotContain(e.ListSaves(includeAutosave: false), s => s.IsAutosave);
    }

    [Fact]
    public void FileStorageAndOldFormatSaves()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var e = Start();
            e.SaveStorage = new FileSaveStorage(dir.FullName);
            e.Submit("take bone. give bone to dog");
            Put(e, "dog", "hall");
            e.Submit("save my game");
            Assert.True(File.Exists(Path.Combine(dir.FullName, "my game.sav")));
            // An old-style save (just the state) is still readable.
            File.WriteAllText(Path.Combine(dir.FullName, "old.sav"), e.State.Serialize());
            var e2 = Start();
            e2.SaveStorage = new FileSaveStorage(dir.FullName);
            Assert.Contains("Game restored", e2.Submit("restore old").Text);
            e2.Submit("restore my game");
            Assert.Contains(e2.ListSaves(), s => s.Name == "my game");
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void NpcStateSurvivesSaveAndRestore()
    {
        var e = Start();
        Put(e, "dog", "hall");
        e.Adventure.FindItem("dog")!.Npc!.Movement = NpcMovement.Stationary;
        e.Submit("take bone. give bone to dog");
        e.Submit("save");
        e.Submit("n");
        e.Submit("restore");
        e.Submit("e");
        Assert.Equal("kitchen", e.Loc(e.Adventure.FindItem("dog")!));
    }

    [Fact]
    public void QuitAsksHostToConfirmOrEndsGame()
    {
        var e = Start();
        e.HostHandlesSaveDialogs = true;
        var r = e.Submit("quit");
        Assert.Contains(r.Events, ev => ev.Kind == OutputKind.Quit && ev.Text == GameEngine.QuitConfirm);
        Assert.False(e.IsGameOver);                       // nothing happens until the player confirms

        var console = Start();                            // hosts without dialogs (console) quit straight away
        var r2 = console.Submit("quit");
        Assert.Contains(r2.Events, ev => ev.Kind == OutputKind.Quit && ev.Text == null);
        Assert.True(console.IsGameOver);
    }
}
