using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Samples;

/// <summary>
/// Built-in example games. "The Lighthouse" demonstrates rooms, items, containers, a locked door, a dark area and
/// light sources, characters with conversation topics, puzzles with hints and scoring, command/room/timer triggers,
/// adverb-sensitive puzzles, adjectives, custom verbs with their own grammar, vector pictures and audio.
/// </summary>
public static partial class ExampleAdventures
{
    public static Adventure Lighthouse()
    {
        var a = new Adventure
        {
            Title = "The Lighthouse",
            Author = "AdventureCreator examples",
            Version = "1.0",
            Description = "A storm is rising and the lighthouse has gone dark. A ship is heading for the rocks.",
            Introduction =
                "The storm came in faster than anyone expected. From the beach you can see the lights of a ship, pitching " +
                "in the swell and heading straight for the Devil's Teeth. Above you, the lighthouse is dark.\n\n" +
                "(Type HELP for instructions, HINT if you get stuck.)",
            StartRoomId = "beach",
            IntroPictureId = "pic_beach",
            IntroSoundId = "snd_foghorn",
        };
        a.Settings.MaxCarriedItems = 6;
        a.Settings.AutoListExits = true;
        a.Settings.BackgroundColor = "#F7F3E8";
        a.Settings.TextColor = "#2B2620";

        // ------------------------------------------------------------ rooms
        a.Rooms.Add(new Room
        {
            Id = "beach", Name = "Storm Beach", PictureId = "pic_beach", SoundId = "snd_waves",
            Description = "Waves hammer the shingle and spray stings your face. A narrow path climbs north towards the " +
                          "lighthouse, a black finger against the clouds. An upturned rowing boat lies above the tide line.",
            Exits = { new Exit { Direction = "north", TargetRoomId = "path" } },
        });
        a.Rooms.Add(new Room
        {
            Id = "path", Name = "Cliff Path", PictureId = "pic_path", SoundId = "snd_waves",
            Description = "The path ends at the foot of the lighthouse. Its heavy oak door faces you to the north. " +
                          "A small stone cottage huddles against the wind to the east, smoke torn from its chimney. " +
                          "The beach is back down to the south.",
            Exits =
            {
                new Exit { Direction = "north", TargetRoomId = "base", DoorItemId = "door" },
                new Exit { Direction = "in", TargetRoomId = "base", DoorItemId = "door", Hidden = true },
                new Exit { Direction = "east", TargetRoomId = "cottage" },
                new Exit { Direction = "south", TargetRoomId = "beach" },
            },
        });
        a.Rooms.Add(new Room
        {
            Id = "cottage", Name = "Keeper's Cottage", PictureId = "pic_cottage",
            Description = "A snug room smelling of pipe smoke and paraffin. A fire crackles in the grate. There is a " +
                          "scrubbed table, and an old cupboard stands in the corner. The door leads back out west.",
            Exits = { new Exit { Direction = "west", TargetRoomId = "path" }, new Exit { Direction = "out", TargetRoomId = "path", Hidden = true } },
        });
        a.Rooms.Add(new Room
        {
            Id = "base", Name = "Foot of the Tower", PictureId = "pic_base", ScoreOnFirstVisit = 5,
            Description = "Inside, the wind drops to a moan. Iron stairs spiral up into total darkness. " +
                          "A coil of rope and some old crates are piled against the curved wall. The door is to the south.",
            Exits =
            {
                new Exit { Direction = "up", TargetRoomId = "stairs" },
                new Exit { Direction = "south", TargetRoomId = "path", DoorItemId = "door" },
                new Exit { Direction = "out", TargetRoomId = "path", DoorItemId = "door", Hidden = true },
            },
        });
        a.Rooms.Add(new Room
        {
            Id = "stairs", Name = "Spiral Stairs", PictureId = "pic_stairs", IsDark = true, SoundId = "snd_creak", LoopSound = false,
            Description = "The stairs corkscrew upwards, slick with damp. One step, halfway up, is badly rusted through – " +
                          "it would be wise to go carefully.",
            Exits = { new Exit { Direction = "up", TargetRoomId = "lamproom" }, new Exit { Direction = "down", TargetRoomId = "base" } },
        });
        a.Rooms.Add(new Room
        {
            Id = "lamproom", Name = "Lamp Room", PictureId = "pic_lamproom", SoundId = "snd_waves", ScoreOnFirstVisit = 5,
            Description = "You are at the top of the tower, surrounded by rain-lashed glass. In the centre stands the great " +
                          "lamp inside its enormous lens. A narrow door leads out onto the gallery to the east. Stairs lead down.",
            Exits = { new Exit { Direction = "down", TargetRoomId = "stairs" }, new Exit { Direction = "east", TargetRoomId = "gallery" }, new Exit { Direction = "out", TargetRoomId = "gallery", Hidden = true } },
        });
        a.Rooms.Add(new Room
        {
            Id = "gallery", Name = "Gallery", PictureId = "pic_gallery", SoundId = "snd_waves",
            Description = "The wind tries to tear you off the narrow iron balcony. Far below, the sea boils white over the " +
                          "Devil's Teeth. Out in the darkness the ship's lights are drawing closer. The lamp room is west.",
            Exits = { new Exit { Direction = "west", TargetRoomId = "lamproom" }, new Exit { Direction = "in", TargetRoomId = "lamproom", Hidden = true } },
        });

        // ------------------------------------------------------------ items
        a.Items.AddRange(new[]
        {
            new Item { Id = "boat", Name = "rowing boat", Nouns = { "boat", "rowing boat", "dinghy" }, Adjectives = { "upturned", "rowing", "old" }, Location = "beach", Portable = false, Scenery = true,
                       Description = "It's been dragged well above the tide line and turned over. Something glints underneath it." },
            new Item { Id = "shell", Name = "conch shell", Nouns = { "shell", "conch" }, Adjectives = { "pink", "conch", "large" }, Location = "", Weight = 1,
                       Description = "A large pink conch. When you hold it to your ear you can hear... the sea. Obviously.", ScoreOnTake = 2 },
            new Item { Id = "mat", Name = "doormat", Nouns = { "mat", "doormat" }, Adjectives = { "door", "worn", "rope" }, Location = "path", Portable = false, Scenery = true,
                       Description = "A worn rope doormat that says WELCOME. One corner is raised slightly." },
            new Item { Id = "door", Name = "oak door", Nouns = { "door" }, Adjectives = { "oak", "heavy", "lighthouse", "iron-bound" }, Location = "path", Portable = false, Scenery = true,
                       Openable = true, IsOpen = false, Lockable = true, IsLocked = true, KeyItemId = "key",
                       Description = "A massive iron-bound oak door with a big old-fashioned keyhole." },
            new Item { Id = "key", Name = "iron key", Nouns = { "key" }, Adjectives = { "iron", "big", "old", "rusty" }, Location = "", Weight = 1,
                       Description = "A heavy iron key, big enough to open a church." },
            new Item { Id = "table", Name = "table", Nouns = { "table" }, Adjectives = { "scrubbed", "wooden", "kitchen" }, Location = "cottage", Portable = false, Scenery = true, Supporter = true,
                       Description = "A scrubbed pine kitchen table." },
            new Item { Id = "oilcan", Name = "oil can", Nouns = { "can", "oil can", "oil", "paraffin" }, Adjectives = { "oil", "paraffin", "tin", "heavy" }, Location = "table", Weight = 3,
                       Description = "A heavy tin can, full of paraffin for the lamp." },
            new Item { Id = "cupboard", Name = "cupboard", Nouns = { "cupboard", "cabinet" }, Adjectives = { "old", "wooden", "corner" }, Location = "cottage", Portable = false, Scenery = true,
                       Container = true, Openable = true, IsOpen = false, Description = "A tall old cupboard with a sticky door." },
            new Item { Id = "matches", Name = "box of matches", Article = "a", Nouns = { "matches", "box", "matchbox", "match" }, Adjectives = { "safety", "match" }, Location = "cupboard", Weight = 1,
                       Description = "A box of Swan Vestas. Plenty left." },
            new Item { Id = "rag", Name = "chamois cloth", Nouns = { "cloth", "chamois", "rag", "leather" }, Adjectives = { "chamois", "soft", "yellow" }, Location = "cupboard", Weight = 1,
                       Description = "A soft chamois leather – just the thing for polishing glass." },
            new Item { Id = "lantern", Name = "brass lantern", Nouns = { "lantern", "lamp" }, Adjectives = { "brass", "hurricane", "small" }, Location = "cottage", Weight = 2,
                       LightSource = true, IsLit = false, Switchable = false,
                       Description = "A brass hurricane lantern. It has paraffin in it but it isn't lit.",
                       RoomDescription = "A brass hurricane lantern hangs from a hook by the door." },
            new Item { Id = "keeper", Name = "old keeper", Article = "the", Nouns = { "keeper", "tom", "man", "old man", "lighthouse keeper" }, Adjectives = { "old", "grizzled" },
                       Location = "cottage", IsCharacter = true, Portable = false,
                       Description = "Old Tom, the lighthouse keeper, sits by the fire with his leg propped on a stool. He's clearly in no state to climb stairs.",
                       RoomDescription = "Old Tom, the keeper, sits by the fire nursing a bandaged leg.",
                       Topics =
                       {
                           new Topic { Keywords = { "hello", "hi", "greeting", "talk" }, Response = "\"Thank heaven you're here!\" says Tom. \"I've done my ankle and I can't get up those stairs. The lamp's out and there's a ship out there. Ask me about the lamp, the key or the stairs.\"" },
                           new Topic { Keywords = { "key", "door" }, Response = "\"Key to the tower? I always leave it under the mat,\" he says. \"Burglars never think to look there.\"" },
                           new Topic { Keywords = { "lamp", "light", "oil", "paraffin" }, Response = "\"She'll need filling with paraffin – there's a can on the table – and the lens'll want a polish or the beam won't carry. Then light her up.\"" },
                           new Topic { Keywords = { "stairs", "step", "steps" }, Response = "\"Mind the rusty step halfway up. Go carefully, or you'll end up like me!\"" },
                           new Topic { Keywords = { "lantern" }, Response = "\"Take the lantern, it's pitch black in the tower. Matches are in the cupboard.\"" },
                           new Topic { Keywords = { "ship", "storm" }, Response = "\"If that lamp isn't lit soon she'll be on the Teeth. Go on, hurry!\"" },
                           new Topic { Keywords = { "leg", "ankle" }, Response = "\"Slipped on that blessed step. Don't you do the same.\"" },
                       } },
            new Item { Id = "rope", Name = "coil of rope", Nouns = { "rope", "coil" }, Adjectives = { "coiled", "old" }, Location = "base", Weight = 2,
                       Description = "Thick, tarry rope. Heavier than it looks." },
            new Item { Id = "crates", Name = "crates", Article = "some", Plural = true, Nouns = { "crates", "crate", "boxes" }, Adjectives = { "old", "wooden" }, Location = "base", Portable = false, Scenery = true,
                       Description = "Empty crates, stencilled TRINITY HOUSE." },
            new Item { Id = "biglamp", Name = "great lamp", Nouns = { "lamp", "great lamp", "burner", "light", "reservoir" }, Adjectives = { "great", "big", "huge" }, Location = "lamproom", Portable = false, Scenery = true,
                       Description = "The great paraffin burner sits in the centre of the lens. Its reservoir is bone dry." },
            new Item { Id = "lens", Name = "lens", Nouns = { "lens", "glass", "prisms" }, Adjectives = { "enormous", "fresnel", "grimy", "dirty", "huge" }, Location = "lamproom", Portable = false, Scenery = true,
                       Description = "An enormous Fresnel lens of polished prisms – or it would be polished, if it weren't coated in soot." },
            new Item { Id = "ship", Name = "ship", Nouns = { "ship", "lights", "vessel" }, Adjectives = { "distant" }, Location = "gallery", Portable = false, Scenery = true,
                       Description = "A coaster, riding low in the water, running before the storm. She's heading straight for the rocks." },
        });

        a.Variables.Add(new Variable { Name = "oil", InitialValue = 0, Description = "1 when the great lamp has been filled." });
        a.Variables.Add(new Variable { Name = "polished", InitialValue = 0, Description = "1 when the lens has been cleaned." });
        a.Variables.Add(new Variable { Name = "lit", InitialValue = 0, Description = "1 when the great lamp is lit." });
        a.Variables.Add(new Variable { Name = "slips", InitialValue = 0, Description = "Times the player has slipped on the stairs." });

        // ------------------------------------------------------------ vocabulary (custom commands)
        a.Vocabulary.Verbs.Add(new VerbDefinition
        {
            Id = "polish", Words = { "polish", "clean", "shine", "buff", "wipe" },
            Grammar = { "* {noun}", "* {noun} with {held2}" },
            DefaultResponse = "You give {the noun1} a quick rub. It doesn't look much different.",
            Help = "Polish something, e.g. POLISH THE LENS WITH THE CLOTH.",
        });
        a.Vocabulary.Verbs.Add(new VerbDefinition
        {
            Id = "signal", Words = { "signal", "flash", "wave to", "warn" },
            Grammar = { "*", "* {noun}", "* to {noun}", "* {noun} with {held2}" },
            DefaultResponse = "Nobody out there could possibly see that.",
            Help = "Try to signal the ship.",
        });
        a.Vocabulary.Verbs.Add(new VerbDefinition
        {
            Id = "pour", Words = { "pour", "decant", "tip" },
            Grammar = { "* {held} in|into {noun2}", "* {held}", "* {noun2} with {held} " },
            DefaultResponse = "You'd better not waste it.",
        });
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "fill", Words = { "fill", "refill", "top up" }, Grammar = { "* {noun}", "* {noun} with|from {held2}" } });
        a.Vocabulary.Replacements["lift"] = "look under";
        a.Vocabulary.Replacements["raise"] = "look under";
        a.Vocabulary.IgnoredWords.Add("hurriedly");

        // ------------------------------------------------------------ triggers
        int n = 0;
        string Id() => $"t{++n}";

        // Look under / move the boat -> shell appears
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Find shell under boat", Verb = "lookunder|search|push|turn|move", Noun1 = "boat", OnceOnly = true,
            Actions = { GameAction.Say("You heave the boat up a few inches. Underneath, half buried in the shingle, is a large pink conch shell."), new GameAction(ActionType.CreateItem, "shell") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Find key under mat", Verb = "lookunder|search|pull|push|turn|take", Noun1 = "mat",
            Conditions = { new Condition(ConditionType.ItemIn, "key", b: "", negate: false), new Condition(ConditionType.TriggerFired, "t_keyfound", negate: true) },
            Actions =
            {
                GameAction.Say("You lift the corner of the doormat. Just as Tom said – a big iron key!"),
                new GameAction(ActionType.MoveItem, "key", b: Locations.Here),
                new GameAction(ActionType.AwardScore, "key", 5),
                new GameAction(ActionType.RunTrigger, "t_keyfound"),
            },
        });
        a.Triggers.Add(new Trigger { Id = "t_keyfound", Name = "(marker) key found", Event = TriggerEvent.Subroutine });

        // Lighting the lantern needs the matches
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Light lantern", Verb = "light|switchon|burn", Noun1 = "lantern",
            Conditions = { new Condition(ConditionType.ItemCarried, "matches") },
            Actions =
            {
                new GameAction(ActionType.SetLit, "lantern", 1),
                new GameAction(ActionType.SetItemDescription, "lantern", text: "A brass hurricane lantern, burning with a steady yellow flame."),
                GameAction.Say("You strike a match and touch it to the wick. The lantern flares into life."),
                new GameAction(ActionType.AwardScore, "lantern", 5),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Lantern needs matches", Verb = "light|switchon|burn", Noun1 = "lantern",
            Actions = { GameAction.Say("You have nothing to light it with.") },
        });

        // The rusty step: an adverb puzzle
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Stairs in the dark", Verb = "go|climb", RoomId = "stairs", Priority = 10,
            Conditions = { new Condition(ConditionType.IsDark), new Condition(ConditionType.WordUsed, "up|u|climb|ascend|upstairs") },
            Actions = { GameAction.Say("You can't see a thing. Climbing further in the dark would be madness.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Rusty step – careless", Verb = "go|climb", RoomId = "stairs",
            Conditions =
            {
                new Condition(ConditionType.AdverbUsed, "carefully|slowly|cautiously|gingerly|warily|gently", negate: true),
                new Condition(ConditionType.PlayerIn, "stairs"),
                new Condition(ConditionType.WordUsed, "up|u|climb|ascend|upstairs"),
            },
            Actions =
            {
                new GameAction(ActionType.PlaySound, "snd_creak"),
                GameAction.Say("You bound up the stairs – and the rusty step gives way beneath you with a shriek of metal! You grab the rail just in time and haul yourself back. Better take it more carefully."),
                new GameAction(ActionType.AddVar, "slips", 1),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Rusty step – careful", Verb = "go|climb", RoomId = "stairs",
            Conditions = { new Condition(ConditionType.AdverbUsed, "carefully|slowly|cautiously|gingerly|warily|gently"), new Condition(ConditionType.WordUsed, "up|u|climb|ascend|upstairs") },
            Actions =
            {
                GameAction.Say("Testing each step, you pick your way {adverb} past the rusted tread."),
                new GameAction(ActionType.AwardScore, "stairs", 5),
                new GameAction(ActionType.GoTo, "lamproom"),
            },
        });

        // Filling the great lamp
        var fillActions = new List<GameAction>
        {
            GameAction.Say("You carefully pour paraffin into the reservoir of the great lamp until it's brim full."),
            new GameAction(ActionType.SetVar, "oil", 1),
            new GameAction(ActionType.SetItemDescription, "biglamp", text: "The great paraffin burner, its reservoir full and ready."),
            new GameAction(ActionType.SetItemDescription, "oilcan", text: "A tin can. It's nearly empty now."),
        };
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Fill lamp (pour)", Verb = "pour|insert|empty|use", Noun1 = "oilcan", Noun2 = "biglamp",
            Conditions = { new Condition(ConditionType.VarEquals, "oil", 0), new Condition(ConditionType.ItemCarried, "oilcan") },
            Actions = fillActions.ToList(),
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Fill lamp (fill)", Verb = "fill", Noun1 = "biglamp",
            Conditions = { new Condition(ConditionType.VarEquals, "oil", 0), new Condition(ConditionType.ItemCarried, "oilcan") },
            Actions = fillActions.ToList(),
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Fill lamp – no oil", Verb = "fill", Noun1 = "biglamp",
            Conditions = { new Condition(ConditionType.VarEquals, "oil", 0) },
            Actions = { GameAction.Say("You have nothing to fill it with.") },
        });

        // Polishing the lens (custom verb)
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Polish lens", Verb = "polish|rub", Noun1 = "lens",
            Conditions = { new Condition(ConditionType.ItemCarried, "rag"), new Condition(ConditionType.VarEquals, "polished", 0) },
            Actions =
            {
                GameAction.Say("You work the chamois {adverb} over prism after prism until the soot is gone and the great lens gleams."),
                new GameAction(ActionType.SetVar, "polished", 1),
                new GameAction(ActionType.SetItemDescription, "lens", text: "The enormous Fresnel lens gleams, every prism spotless."),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Polish lens – no cloth", Verb = "polish|rub", Noun1 = "lens", Conditions = { new Condition(ConditionType.VarEquals, "polished", 0) },
            Actions = { GameAction.Say("Rubbing at the soot with your sleeve just smears it. You need a proper cloth.") },
        });

        // Lighting the great lamp: the finale
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Light great lamp – no oil", Verb = "light|switchon|burn", Noun1 = "biglamp", Conditions = { new Condition(ConditionType.VarEquals, "oil", 0) },
            Actions = { GameAction.Say("The reservoir is dry. There's nothing to burn.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Light great lamp – no matches", Verb = "light|switchon|burn", Noun1 = "biglamp", Conditions = { new Condition(ConditionType.ItemCarried, "matches", negate: true) },
            Actions = { GameAction.Say("You have nothing to light it with.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Light great lamp – dirty lens", Verb = "light|switchon|burn", Noun1 = "biglamp", Conditions = { new Condition(ConditionType.VarEquals, "polished", 0) },
            Actions =
            {
                GameAction.Say("The burner catches with a roar – but the beam barely makes it through the filthy lens. The ship will never see that. You turn the burner down again. The lens needs cleaning first."),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Light great lamp – success", Verb = "light|switchon|burn", Noun1 = "biglamp",
            Actions =
            {
                GameAction.Say("You strike a match and the burner catches with a roar. The great lens begins to turn, and a brilliant beam sweeps out across the storm!"),
                new GameAction(ActionType.SetVar, "lit", 1),
                new GameAction(ActionType.ShowPicture, "pic_lit"),
                new GameAction(ActionType.PlaySound, "snd_foghorn"),
                new GameAction(ActionType.SetRoomDescription, "lamproom", text: "The great lamp blazes inside its turning lens, throwing its beam far out to sea."),
            },
        });

        // Winning: watch the ship from the gallery
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Ship saved", Event = TriggerEvent.EnterRoom, RoomId = "gallery",
            Conditions = { new Condition(ConditionType.VarEquals, "lit", 1) },
            Actions =
            {
                new GameAction(ActionType.PlaySound, "snd_fanfare"),
                new GameAction(ActionType.AwardScore, "win", 10),
                new GameAction(ActionType.Win, text: "As the beam sweeps over the water you see the ship heel hard to port. Slowly, agonisingly, she swings clear of the Devil's Teeth and into the safety of the bay. Somewhere below, Old Tom is cheering."),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Signal ship with lantern", Verb = "signal|wave", RoomId = "gallery",
            Conditions = { new Condition(ConditionType.ItemLit, "lantern"), new Condition(ConditionType.VarEquals, "lit", 0) },
            Actions = { GameAction.Say("You swing the lantern back and forth. It's a feeble spark against the storm – nobody on that ship will see it. Only the great lamp will do.") },
        });

        // Conch shell: blowing it is a fun extra
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Blow conch", Verb = "blow|play", Noun1 = "shell",
            Actions = { GameAction.Say("You blow into the conch. It makes a mournful, booming note{adverb}. From the cottage you hear Tom shout \"Very funny!\""), new GameAction(ActionType.PlaySound, "snd_foghorn") },
        });

        // Atmosphere and time pressure
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Foghorn every 7 turns", Event = TriggerEvent.Timer, Interval = 7,
            Conditions = { new Condition(ConditionType.VarEquals, "lit", 0) },
            Actions = { GameAction.Say("Out at sea, the ship sounds its horn: a long, desperate blast."), new GameAction(ActionType.PlaySound, "snd_foghorn") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Warning at turn 70", Event = TriggerEvent.Timer, Turn = 70,
            Conditions = { new Condition(ConditionType.VarEquals, "lit", 0) },
            Actions = { GameAction.Say("The ship's lights are very close to the rocks now. There isn't much time!") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Too late", Event = TriggerEvent.Timer, Turn = 90,
            Conditions = { new Condition(ConditionType.VarEquals, "lit", 0) },
            Actions = { new GameAction(ActionType.Lose, text: "A terrible grinding crash carries over the wind. The ship has struck the Devil's Teeth. You were too late.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Tom greets you", Event = TriggerEvent.EnterRoom, RoomId = "cottage", OnceOnly = true,
            Actions = { GameAction.Say("\"Who's that? Oh, thank goodness,\" says the old keeper. \"Come in, come in! Talk to me!\"") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Quietly into the cottage", Verb = "go", Adverb = "quietly|silently|stealthily|sneakily", RoomId = "path",
            Conditions = { new Condition(ConditionType.WordUsed, "east|e") },
            Actions = { GameAction.Say("You creep {adverb} up to the cottage door... and Tom still hears you. \"I'm not deaf, you know!\""), new GameAction(ActionType.GoTo, "cottage") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Rope is heavy", Verb = "take", Noun1 = "rope", Event = TriggerEvent.AfterCommand,
            Actions = { GameAction.Say("(You're not sure what you'll want this for.)") },
        });

        // ------------------------------------------------------------ puzzles
        a.Puzzles.Add(new Puzzle
        {
            Id = "pz_door", Name = "Into the tower", Points = 10, HintRoomIds = { "path", "beach", "cottage" },
            Hints = { "The lighthouse door is locked. Perhaps the keeper knows where the key is.", "Try asking Tom about the key.", "LOOK UNDER THE MAT, then UNLOCK DOOR WITH KEY." },
            SolvedWhen = { new Condition(ConditionType.ItemOpen, "door") },
            SolvedMessage = "The door swings open with a groan.",
        });
        a.Puzzles.Add(new Puzzle
        {
            Id = "pz_dark", Name = "Let there be light", Points = 0, HintRoomIds = { "base", "stairs", "cottage" },
            Hints = { "It's too dark to climb the stairs safely.", "There's a lantern in the cottage, and matches in the cupboard.", "OPEN CUPBOARD, TAKE MATCHES AND LANTERN, LIGHT LANTERN." },
            SolvedWhen = { new Condition(ConditionType.ItemLit, "lantern") },
        });
        a.Puzzles.Add(new Puzzle
        {
            Id = "pz_step", Name = "The rusty step", Points = 5, HintRoomIds = { "stairs" },
            Hints = { "Rushing up the stairs isn't working.", "Tom said to go carefully.", "Type: CAREFULLY GO UP (or CLIMB SLOWLY)." },
            SolvedWhen = { new Condition(ConditionType.RoomVisited, "lamproom") },
        });
        a.Puzzles.Add(new Puzzle
        {
            Id = "pz_oil", Name = "Fuel for the lamp", Points = 10, HintRoomIds = { "lamproom" },
            Hints = { "The great lamp is dry.", "There's paraffin in the cottage.", "POUR OIL INTO LAMP (or FILL LAMP)." },
            SolvedWhen = { new Condition(ConditionType.VarEquals, "oil", 1) },
        });
        a.Puzzles.Add(new Puzzle
        {
            Id = "pz_lens", Name = "Clean the lens", Points = 10, HintRoomIds = { "lamproom" },
            Hints = { "The lens is filthy.", "Something soft from the cottage cupboard would help.", "POLISH LENS WITH CLOTH." },
            SolvedWhen = { new Condition(ConditionType.VarEquals, "polished", 1) },
        });
        a.Puzzles.Add(new Puzzle
        {
            Id = "pz_light", Name = "Light the lamp", Points = 20, HintRoomIds = { "lamproom", "gallery" },
            Hints = { "Everything is ready.", "LIGHT THE GREAT LAMP, then go out onto the gallery." },
            SolvedWhen = { new Condition(ConditionType.VarEquals, "lit", 1) },
        });

        a.Messages[Engine.Msg.Dark] = "It is pitch dark. You can hear water dripping somewhere, and the stairs creak.";

        AddPictures(a);
        AddSounds(a);
        return a;
    }

    // ------------------------------------------------------------ graphics
    private const int Black = 0, Blue = 1, Red = 2, White = 7, BrightBlue = 9, BrightRed = 10, BrightCyan = 13, Yellow = 14, BrightWhite = 15,
        DarkBrown = 16, Brown = 17, Tan = 18, DarkGreen = 19, Green = 20, Navy = 22, Royal = 23, Sky = 24, Slate = 25, Grey = 26, DarkGrey = 27, Orange = 28, Wheat = 31;

    private static DrawCommand Clear(int c) => new() { Op = DrawOp.Clear, Color = c };
    private static DrawCommand Ink(int c) => new() { Op = DrawOp.SetInk, Color = c };
    private static DrawCommand Box(int x1, int y1, int x2, int y2) => new() { Op = DrawOp.FilledRectangle, X = x1, Y = y1, X2 = x2, Y2 = y2 };
    private static DrawCommand Frame(int x1, int y1, int x2, int y2) => new() { Op = DrawOp.Rectangle, X = x1, Y = y1, X2 = x2, Y2 = y2 };
    private static DrawCommand Oval(int x1, int y1, int x2, int y2, bool filled = true) => new() { Op = filled ? DrawOp.FilledEllipse : DrawOp.Ellipse, X = x1, Y = y1, X2 = x2, Y2 = y2 };
    private static DrawCommand Line(int x1, int y1, int x2, int y2) => new() { Op = DrawOp.Line, X = x1, Y = y1, X2 = x2, Y2 = y2 };
    private static DrawCommand Poly(params int[] pts) => new() { Op = DrawOp.FilledPolygon, Points = pts.ToList() };
    private static DrawCommand Fill(int x, int y) => new() { Op = DrawOp.Fill, X = x, Y = y };
    private static DrawCommand Label(int x, int y, string t) => new() { Op = DrawOp.Text, X = x, Y = y, Text = t, Scale = 1 };

    private static Picture Pic(string id, string name, params DrawCommand[][] layers) => new()
    {
        Id = id, Name = name, Palette = Palettes.Extended.ToList(), InitialPaper = Black, InitialInk = BrightWhite,
        Commands = layers.SelectMany(l => l).ToList(),
    };

    private static DrawCommand[] Tower(int cx, int top, int bottom, bool lit)
    {
        int halfTop = 9, halfBottom = 15;
        var list = new List<DrawCommand>
        {
            Ink(BrightWhite), Poly(cx - halfTop, top, cx + halfTop, top, cx + halfBottom, bottom, cx - halfBottom, bottom),
            Ink(BrightRed),
        };
        int h = bottom - top;
        for (int band = 1; band <= 2; band++)
        {
            int y1 = top + h * band / 3 - 5, y2 = y1 + 8;
            double f1 = (double)(y1 - top) / h, f2 = (double)(y2 - top) / h;
            int w1 = (int)(halfTop + (halfBottom - halfTop) * f1), w2 = (int)(halfTop + (halfBottom - halfTop) * f2);
            list.Add(Poly(cx - w1, y1, cx + w1, y1, cx + w2, y2, cx - w2, y2));
        }
        list.Add(Ink(DarkGrey)); list.Add(Box(cx - 11, top - 4, cx + 11, top));
        list.Add(Ink(lit ? Yellow : Slate)); list.Add(Box(cx - 7, top - 14, cx + 7, top - 5));
        list.Add(Ink(DarkGrey)); list.Add(Poly(cx - 9, top - 14, cx + 9, top - 14, cx, top - 22));
        if (lit)
        {
            list.Add(Ink(Wheat));
            list.Add(Poly(cx - 6, top - 10, 0, top - 40, 0, top + 10));
            list.Add(Poly(cx + 6, top - 10, 255, top - 30, 255, top + 5));
        }
        return list.ToArray();
    }

    private static DrawCommand[] Rain(int seed, int color)
    {
        var rnd = new Random(seed);
        var list = new List<DrawCommand> { Ink(color) };
        for (int i = 0; i < 45; i++)
        {
            int x = rnd.Next(0, 256), y = rnd.Next(0, 110);
            list.Add(Line(x, y, x - 3, y + 7));
        }
        return list.ToArray();
    }

    private static DrawCommand[] Sea(int top)
    {
        var list = new List<DrawCommand> { Ink(Blue), Box(0, top, 255, 175), Ink(Royal) };
        for (int y = top + 4; y < 176; y += 7)
            for (int x = (y * 7) % 20; x < 256; x += 22)
                list.Add(Line(x, y, x + 8, y - 2));
        list.Add(Ink(BrightWhite));
        for (int x = 5; x < 256; x += 31) list.Add(Line(x, top + 1, x + 10, top));
        return list.ToArray();
    }

    private static void AddPictures(Adventure a)
    {
        a.Pictures.Add(Pic("pic_beach", "Storm Beach",
            new[] { Clear(Navy), Ink(DarkGrey), Oval(-20, -10, 120, 40), Oval(90, -15, 230, 30), Ink(Slate), Oval(150, 5, 280, 45), Oval(10, 20, 90, 50) },
            Sea(95),
            new[] { Ink(DarkGrey), Poly(150, 175, 170, 90, 205, 80, 256, 70, 256, 175) },
            Tower(222, 22, 76, lit: false),
            new[] { Ink(Tan), Poly(0, 175, 0, 140, 60, 128, 140, 132, 180, 150, 200, 175), Ink(Brown), Poly(40, 138, 95, 138, 88, 128, 47, 128), Ink(DarkBrown), Line(47, 128, 88, 128) },
            new[] { Ink(Yellow), Box(100, 108, 103, 110), Ink(BrightRed), Box(108, 108, 110, 110), Ink(DarkGrey), Poly(92, 112, 120, 112, 116, 116, 96, 116) },
            Rain(1, Sky)));

        a.Pictures.Add(Pic("pic_path", "Cliff Path",
            new[] { Clear(Slate), Ink(DarkGrey), Oval(-30, -20, 140, 40), Oval(120, -25, 290, 35) },
            new[] { Ink(Green), Box(0, 110, 255, 175), Ink(DarkGreen), Poly(0, 125, 80, 118, 160, 128, 255, 120, 255, 175, 0, 175) },
            new[] { Ink(BrightWhite), Poly(90, 8, 150, 8, 165, 120, 75, 120), Ink(BrightRed), Poly(84, 45, 156, 45, 158, 60, 82, 60), Poly(80, 85, 160, 85, 162, 100, 78, 100) },
            new[] { Ink(DarkBrown), Poly(105, 120, 105, 88, 120, 80, 135, 88, 135, 120), Ink(Black), Oval(126, 100, 130, 104), Ink(Grey), Line(105, 96, 135, 96), Line(105, 110, 135, 110) },
            new[] { Ink(Tan), Poly(100, 122, 140, 122, 175, 175, 60, 175) },
            new[] { Ink(Brown), Poly(104, 121, 136, 121, 139, 127, 101, 127), Ink(Wheat), Label(104, 122, "") },
            new[] { Ink(Grey), Box(195, 105, 250, 140), Ink(DarkBrown), Poly(190, 105, 255, 105, 222, 82), Ink(Yellow), Box(205, 115, 215, 125), Ink(DarkGrey), Box(240, 70, 246, 90) },
            Rain(2, Sky)));

        a.Pictures.Add(Pic("pic_cottage", "Keeper's Cottage",
            new[] { Clear(Brown), Ink(DarkBrown), Box(0, 130, 255, 175), Ink(Tan), Line(0, 130, 255, 130) },
            new[] { Ink(Grey), Box(160, 60, 240, 130), Ink(Black), Box(175, 85, 225, 130), Ink(Orange), Poly(180, 130, 190, 100, 200, 120, 210, 95, 220, 130), Ink(Yellow), Poly(190, 130, 197, 110, 205, 125, 212, 108, 216, 130), Ink(DarkGrey), Box(155, 55, 245, 62) },
            new[] { Ink(Navy), Box(40, 30, 100, 80), Ink(Wheat), Frame(40, 30, 100, 80), Line(70, 30, 70, 80), Line(40, 55, 100, 55), Ink(Sky), Line(50, 35, 46, 43), Line(85, 60, 81, 68) },
            new[] { Ink(Wheat), Box(30, 110, 120, 116), Ink(DarkBrown), Box(35, 116, 40, 150), Box(110, 116, 115, 150), Ink(Grey), Box(55, 98, 70, 110), Ink(Slate), Box(58, 94, 67, 98) },
            new[] { Ink(DarkBrown), Box(5, 50, 30, 150), Ink(Brown), Frame(5, 50, 30, 150), Line(17, 50, 17, 150) },
            new[] { Ink(Royal), Poly(135, 110, 160, 110, 162, 150, 133, 150), Ink(Wheat), Oval(138, 88, 156, 108), Ink(BrightWhite), Poly(138, 100, 156, 100, 150, 112, 144, 112) },
            new[] { Ink(Yellow), Oval(115, 60, 127, 76), Ink(DarkGrey), Line(121, 50, 121, 60) }));

        a.Pictures.Add(Pic("pic_base", "Foot of the Tower",
            new[] { Clear(DarkGrey), Ink(Black), Oval(60, -40, 196, 60), Ink(Slate), Line(0, 150, 255, 150) },
            new[] { Ink(Grey), Line(128, 175, 128, 20), Line(100, 150, 156, 135), Line(156, 135, 100, 118), Line(100, 118, 156, 100), Line(156, 100, 100, 82), Line(100, 82, 156, 64), Line(156, 64, 110, 48) },
            new[] { Ink(Brown), Box(10, 120, 50, 150), Box(20, 95, 55, 120), Ink(DarkBrown), Frame(10, 120, 50, 150), Frame(20, 95, 55, 120), Ink(Tan), Oval(190, 130, 230, 150), Oval(197, 134, 223, 146, false) },
            new[] { Ink(DarkBrown), Box(110, 150, 146, 175), Ink(Slate), Line(110, 150, 146, 150) }));

        a.Pictures.Add(Pic("pic_stairs", "Spiral Stairs",
            new[] { Clear(Black), Ink(DarkGrey) },
            Enumerable.Range(0, 9).Select(i => Line(80 + (i % 2) * 40, 170 - i * 18, 176 - (i % 2) * 40, 160 - i * 18)).ToArray(),
            new[] { Ink(Grey), Line(128, 175, 128, 0), Ink(Orange), Oval(120, 88, 136, 96), Ink(DarkBrown), Box(118, 90, 138, 94), Ink(Yellow), Oval(20, 20, 60, 60, false) }));

        var lampRoom = new[]
        {
            new[] { Clear(Navy), Ink(Slate), Line(0, 40, 255, 40), Line(0, 140, 255, 140), Line(40, 0, 40, 140), Line(90, 0, 90, 140), Line(166, 0, 166, 140), Line(216, 0, 216, 140) },
            Rain(3, Sky),
            new[] { Ink(DarkGrey), Box(0, 140, 255, 175), Ink(Grey), Box(100, 120, 156, 145) },
            new[] { Ink(BrightCyan), Oval(90, 20, 166, 125, false), Oval(96, 30, 160, 115, false), Line(128, 20, 128, 125), Line(90, 72, 166, 72) },
            new[] { Ink(Slate), Box(118, 60, 138, 85) },
        };
        a.Pictures.Add(Pic("pic_lamproom", "Lamp Room", lampRoom));
        a.Pictures.Add(Pic("pic_lit", "The Lamp Lit", lampRoom.Append(new[] { Ink(Yellow), Oval(108, 50, 148, 95), Ink(BrightWhite), Oval(118, 60, 138, 85), Ink(Wheat), Poly(128, 70, 0, 20, 0, 110), Poly(128, 70, 255, 30, 255, 100) }).ToArray()));

        a.Pictures.Add(Pic("pic_gallery", "Gallery",
            new[] { Clear(Navy), Ink(DarkGrey), Oval(-40, -20, 150, 40), Oval(100, -10, 300, 30) },
            Sea(80),
            new[] { Ink(BrightWhite), Poly(30, 130, 45, 118, 60, 132, 75, 120, 90, 134, 40, 140), Ink(DarkGrey), Poly(150, 78, 215, 78, 205, 88, 158, 88), Box(170, 64, 185, 78), Ink(Yellow), Box(173, 68, 175, 70), Box(160, 81, 162, 83), Box(195, 81, 197, 83) },
            new[] { Ink(Grey), Line(0, 150, 255, 150), Line(0, 160, 255, 160), Ink(DarkGrey), Box(0, 165, 255, 175) },
            Enumerable.Range(0, 17).Select(i => Line(i * 16, 150, i * 16, 165)).Prepend(Ink(Grey)).ToArray(),
            Rain(4, Sky)));
    }

    private static void AddSounds(Adventure a)
    {
        void Add(string id, string name, string file, byte[] data, double volume = 1)
        {
            a.Assets["sounds/" + file] = data;
            a.Sounds.Add(new SoundAsset { Id = id, Name = name, AssetName = "sounds/" + file, Volume = volume });
        }
        Add("snd_waves", "Waves", "waves.wav", SoundSynth.Waves(), 0.5);
        Add("snd_foghorn", "Foghorn", "foghorn.wav", SoundSynth.Foghorn());
        Add("snd_creak", "Creaking stairs", "creak.wav", SoundSynth.Creak());
        Add("snd_fanfare", "Fanfare", "fanfare.wav", SoundSynth.Fanfare());
    }
}
