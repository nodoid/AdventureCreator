using AdventureCreator.Core.Audio;
using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Samples;

public static partial class ExampleAdventures
{
    /// <summary>
    /// "Genesis" – a fan adventure inspired by the 1975 Doctor Who serial <i>Genesis of the Daleks</i>, used as the
    /// main test and demonstration game. All text is original. Doctor Who, the Daleks and related names belong to the
    /// BBC; this sample is for personal/testing use and should not be distributed commercially.
    /// <para>Demonstrates: a deadly hazard (poison gas) and a wearable counter-measure, showing an item to a guard,
    /// an adverb-driven stealth puzzle, a keycard opening two locked doors, conversations with topics that give
    /// items, custom commands with grammar (PLANT, CONNECT, ERASE), a two-step moral choice, timers and a deadline,
    /// puzzles with progressive hints, pictures and sound.</para>
    /// </summary>
    public static Adventure Genesis()
    {
        var a = new Adventure
        {
            Title = "Genesis",
            Author = "AdventureCreator examples (a fan adventure inspired by Doctor Who: Genesis of the Daleks)",
            Version = "1.0",
            Description = "Skaro, at the end of a thousand-year war. The Time Lords have sent you to stop the creation of the Daleks.",
            Introduction =
                "The world lurches, the TARDIS is nowhere to be seen, and you are standing in a cold grey mist that smells of cordite. " +
                "A Time Lord's voice lingers in your head: \"Skaro. The Kaleds are about to create the Daleks. Avert their creation, " +
                "or affect their development so that they evolve into less aggressive creatures. The Time Ring will bring you home " +
                "when your task is done.\"\n\nOf Sarah, there is no sign.\n\n(Type HELP for instructions, HINT if you get stuck.)",
            StartRoomId = "wasteland",
            IntroPictureId = "g_wasteland",
            IntroSoundId = "g_demat",
        };
        a.Settings.MaxCarriedItems = 6;
        a.Settings.PlayerHealth = 10;
        a.Settings.DeathMessage = "Your strength gives out. Skaro has claimed another victim.";
        a.Settings.BackgroundColor = "#F4F4EF";
        a.Settings.TextColor = "#1E2320";
        a.Messages[Engine.Msg.CantGo] = "Barbed wire, rubble and craters block the way.";

        // ------------------------------------------------------------ rooms
        a.Rooms.AddRange(new[]
        {
            new Room
            {
                Id = "wasteland", Name = "No Man's Land", PictureId = "g_wasteland", SoundId = "g_wind",
                Description = "A cratered waste stretches away into the mist. Tangles of rusted wire sag between shattered posts, and the " +
                              "only sound is the wind and, far away, the thump of artillery. To the north, a zig-zag trench has been cut into the mud.",
                Exits = { new Exit { Direction = "north", TargetRoomId = "trench" } },
            },
            new Room
            {
                Id = "trench", Name = "Kaled Trench", PictureId = "g_trench", SoundId = "g_wind",
                Description = "Sandbags and duckboards, slick with mud. Whoever held this trench left in a hurry – or didn't leave at all. " +
                              "Eastward, across a stretch of open ground, a sickly yellow-green haze drifts low over the earth, and beyond it rises " +
                              "the grey curve of a great dome. No Man's Land is back to the south.",
                Exits = { new Exit { Direction = "south", TargetRoomId = "wasteland" }, new Exit { Direction = "east", TargetRoomId = "entrance" } },
            },
            new Room
            {
                Id = "entrance", Name = "Bunker Entrance", PictureId = "g_entrance", SoundId = "g_hum",
                Description = "The poison haze thins against the flank of the Kaled dome. A heavy steel door, studded with rivets, leads north " +
                              "into the bunker. The trench lies back to the west, across the gas.",
                Exits =
                {
                    new Exit { Direction = "north", TargetRoomId = "corridor", DoorItemId = "bunkerdoor" },
                    new Exit { Direction = "in", TargetRoomId = "corridor", DoorItemId = "bunkerdoor", Hidden = true },
                    new Exit { Direction = "west", TargetRoomId = "trench" },
                },
            },
            new Room
            {
                Id = "corridor", Name = "Bunker Corridor", PictureId = "g_corridor", SoundId = "g_hum",
                Description = "A long concrete corridor hums with hidden machinery. Doors lead west to the security offices and east to the " +
                              "scientific wing; a wider passage runs north towards the central laboratory, and a stairwell descends to the cells. " +
                              "The entrance is south.",
                Exits =
                {
                    new Exit { Direction = "south", TargetRoomId = "entrance", DoorItemId = "bunkerdoor" },
                    new Exit { Direction = "west", TargetRoomId = "office" },
                    new Exit { Direction = "east", TargetRoomId = "lab" },
                    new Exit { Direction = "north", TargetRoomId = "davroslab" },
                    new Exit { Direction = "down", TargetRoomId = "cellblock" },
                },
            },
            new Room
            {
                Id = "office", Name = "Security Office", PictureId = "g_office", ScoreOnFirstVisit = 10,
                Description = "A spartan office: a steel desk, a chair, a wall map of the front line stuck with pins. The officer who works here " +
                              "is, thankfully, elsewhere. The corridor is back to the east.",
                Exits = { new Exit { Direction = "east", TargetRoomId = "corridor" }, new Exit { Direction = "out", TargetRoomId = "corridor", Hidden = true } },
            },
            new Room
            {
                Id = "lab", Name = "Scientific Wing", PictureId = "g_lab", SoundId = "g_hum",
                Description = "Workbenches crowded with glassware and humming equipment. Most of the scientists have gone to some meeting; one " +
                              "remains, pretending very hard to be busy. The corridor is west.",
                Exits = { new Exit { Direction = "west", TargetRoomId = "corridor" }, new Exit { Direction = "out", TargetRoomId = "corridor", Hidden = true } },
            },
            new Room
            {
                Id = "cellblock", Name = "Cell Block", PictureId = "g_cellblock",
                Description = "A cramped landing at the foot of the stairs. A single cell door, barred and bolted, is set into the north wall. " +
                              "The stairs lead back up.",
                Exits =
                {
                    new Exit { Direction = "up", TargetRoomId = "corridor" },
                    new Exit { Direction = "north", TargetRoomId = "cell", DoorItemId = "celldoor" },
                    new Exit { Direction = "in", TargetRoomId = "cell", DoorItemId = "celldoor", Hidden = true },
                },
            },
            new Room
            {
                Id = "cell", Name = "Cell", PictureId = "g_cell",
                Description = "A bare concrete cell with a bench bolted to the wall. The door is south.",
                Exits = { new Exit { Direction = "south", TargetRoomId = "cellblock", DoorItemId = "celldoor" }, new Exit { Direction = "out", TargetRoomId = "cellblock", DoorItemId = "celldoor", Hidden = true } },
            },
            new Room
            {
                Id = "davroslab", Name = "Central Laboratory", PictureId = "g_davroslab", SoundId = "g_hum", ScoreOnFirstVisit = 5,
                Description = "The heart of the bunker. Banks of instruments blink and chatter around a raised platform, and on it sits the " +
                              "creator of all this in his life-support chair. Beside him stands a squat metal casing on a flared skirt – a " +
                              "\"travel machine\". A reinforced door leads north to the incubator room; the corridor is south.",
                Exits =
                {
                    new Exit { Direction = "south", TargetRoomId = "corridor" },
                    new Exit { Direction = "north", TargetRoomId = "incubator", DoorItemId = "incdoor" },
                },
            },
            new Room
            {
                Id = "incubator", Name = "Incubator Room", PictureId = "g_incubator", SoundId = "g_hum",
                Description = "Rows of glass tanks glow a sickly green. In each, something small and many-tentacled twitches in the fluid – " +
                              "the future of the Kaled race, if their creator has his way. The door is south.",
                Exits = { new Exit { Direction = "south", TargetRoomId = "davroslab", DoorItemId = "incdoor" }, new Exit { Direction = "out", TargetRoomId = "davroslab", DoorItemId = "incdoor", Hidden = true } },
            },
        });

        // ------------------------------------------------------------ items and characters
        a.Items.AddRange(new[]
        {
            new Item { Id = "ring", Name = "Time Ring", Article = "the", Nouns = { "ring", "time ring", "bracelet", "band" }, Adjectives = { "time", "silver", "metal" },
                       Location = Locations.Worn, Wearable = true,
                       Description = "A heavy silver band on your wrist, faintly warm. When your mission is complete, a twist of it will take you home." },
            new Item { Id = "wire", Name = "barbed wire", Article = "some", Nouns = { "wire", "barbed wire", "posts" }, Adjectives = { "barbed", "rusty", "rusted" },
                       Location = "wasteland", Portable = false, Scenery = true, Description = "Rusted coils of wire. Something – someone – is caught in it further off. You decide not to look closer." },
            new Item { Id = "soldier", Name = "dead soldier", Nouns = { "soldier", "body", "corpse", "man" }, Adjectives = { "dead", "kaled", "young" },
                       Location = "trench", Portable = false, Scenery = true,
                       Description = "A Kaled soldier, barely more than a boy, slumped against the sandbags. His uniform is a patchwork of odd pieces, as though the army ran out of everything years ago. His tunic pocket bulges slightly." },
            new Item { Id = "pass", Name = "Kaled security pass", Nouns = { "pass", "papers", "identity", "document" }, Adjectives = { "kaled", "security", "identity" },
                       Location = "", Readable = true,
                       ReadText = "\"ELITE SCIENTIFIC CORPS – BUNKER ACCESS. By order of the Chief Scientist.\" The photograph is too smudged to recognise anyone." },
            new Item { Id = "mask", Name = "gas mask", Nouns = { "mask", "gas mask", "respirator" }, Adjectives = { "gas", "rubber", "old" },
                       Location = "trench", Wearable = true,
                       Description = "A battered rubber respirator with round glass eyepieces. The filter looks usable.",
                       RoomDescription = "A gas mask lies in the mud at the bottom of the trench." },
            new Item { Id = "rifle", Name = "rifle", Nouns = { "rifle", "gun" }, Adjectives = { "old", "bolt-action", "rusty" }, Location = "trench", Weight = 3,
                       Description = "An ancient bolt-action rifle, rusted solid. Its firing days are over – and you never carry guns anyway." },
            new Item { Id = "gas", Name = "gas", Article = "the", Nouns = { "gas", "haze", "mist", "fog" }, Adjectives = { "poison", "yellow", "green" },
                       Location = "trench", Portable = false, Scenery = true, Description = "A low, oily haze, yellow-green and quite still. Nothing grows near it." },
            new Item { Id = "guard", Name = "Kaled guard", Nouns = { "guard", "soldier", "sentry", "man" }, Adjectives = { "kaled", "black-uniformed" },
                       Location = "entrance", IsCharacter = true, Portable = false,
                       Description = "A guard in a black uniform, rifle at the ready, studying you through the misted eyepieces of his mask.",
                       RoomDescription = "A Kaled guard stands before the door, rifle raised.",
                       Topics =
                       {
                           new Topic { Keywords = { "hello", "talk", "door", "bunker", "in", "entry" }, Response = "\"Nobody enters the bunker without authority,\" he snaps. \"Papers!\"" },
                           new Topic { Keywords = { "davros", "scientist", "scientists" }, Response = "\"That's none of your business. Show me your pass or clear off.\"" },
                       } },
            new Item { Id = "bunkerdoor", Name = "steel door", Nouns = { "door" }, Adjectives = { "steel", "heavy", "bunker", "riveted" },
                       Location = "entrance", Portable = false, Scenery = true, Openable = true, IsOpen = false,
                       Description = "A heavy door of riveted steel. It opens from the inside – or for anyone the guard chooses to let through." },
            new Item { Id = "sentry", Name = "sentry", Nouns = { "sentry", "guard", "soldier" }, Adjectives = { "bored", "kaled" }, Location = "corridor", IsCharacter = true, Portable = false,
                       Description = "A bored sentry leans beside the door to the security offices, half-watching the corridor. Walk past him openly and he'll certainly stop you.",
                       RoomDescription = "A sentry lounges beside the west door, not quite asleep." },
            new Item { Id = "desk", Name = "steel desk", Nouns = { "desk", "table" }, Adjectives = { "steel", "metal" }, Location = "office", Portable = false, Scenery = true, Supporter = true,
                       Description = "A grey steel desk with a single drawer." },
            new Item { Id = "drawer", Name = "drawer", Nouns = { "drawer" }, Adjectives = { "desk", "steel" }, Location = "office", Portable = false, Scenery = true, Container = true, Openable = true, IsOpen = false,
                       Description = "A shallow drawer in the desk." },
            new Item { Id = "keycard", Name = "security keycard", Nouns = { "keycard", "card", "key" }, Adjectives = { "security", "magnetic", "red" }, Location = "drawer", ScoreOnTake = 5,
                       Description = "A red magnetic keycard stamped SECURITY – ALL LEVELS." },
            new Item { Id = "memo", Name = "memo", Nouns = { "memo", "note", "paper", "memorandum" }, Adjectives = { "typed", "official" }, Location = "desk", Readable = true,
                       Description = "A typed memorandum.",
                       ReadText = "\"To all security staff: the Thal prisoner and the off-worlders are to be held in the cell block. The Chief Scientist's " +
                                  "work in the incubator room is to continue without interruption. Full records of the project are kept on tape in the central laboratory.\"" },
            new Item { Id = "map", Name = "wall map", Nouns = { "map", "pins" }, Adjectives = { "wall", "front", "line" }, Location = "office", Portable = false, Scenery = true,
                       Description = "The front line between the Kaled dome and the Thal city, marked in pins. It has hardly moved in a very long time." },
            new Item { Id = "ronson", Name = "Ronson", Article = "", Nouns = { "ronson", "scientist", "man" }, Adjectives = { "nervous", "tired" }, Location = "lab", IsCharacter = true, Portable = false,
                       Description = "A tired-looking scientist in a white coat. He keeps glancing at the door.",
                       RoomDescription = "Ronson, a nervous scientist, is fiddling with a microscope.",
                       Topics =
                       {
                           new Topic { Keywords = { "explosives", "explosive", "charges", "bomb", "bombs", "incubator", "help", "stop" },
                                       Conditions = { new Condition(ConditionType.VarEquals, "helped", 0) },
                                       Response = "Ronson checks the door, then pulls a canvas satchel from under the bench. \"Demolition charges. I've been saving them for the day " +
                                                  "someone had the courage to use them. The creatures in the incubator room – they're Davros's future for us. Plant these among the tanks " +
                                                  "and connect the wires. And for pity's sake, don't let anyone see you.\"",
                                       Actions = { new GameAction(ActionType.MoveItem, "charges", b: Locations.Carried), new GameAction(ActionType.SetVar, "helped", 1), new GameAction(ActionType.AwardScore, "ronson", 5) } },
                           new Topic { Keywords = { "explosives", "explosive", "charges", "bomb", "incubator" }, Response = "\"That's all I have. Go, before someone comes!\"" },
                           new Topic { Keywords = { "hello", "talk" }, Response = "\"Who are you? You're not one of us... Are you against him? Ask me about the incubator, then – quickly.\"" },
                           new Topic { Keywords = { "davros", "chief" }, Response = "\"He doesn't want a better Kaled race. He wants a race with no pity in it at all. He's been removing feelings from the creatures one by one.\"" },
                           new Topic { Keywords = { "daleks", "dalek", "creatures", "mutants", "machine" }, Response = "\"He calls them Daleks. The travel machine gives them a body; the incubator gives them... everything else. Everything he chooses.\"" },
                           new Topic { Keywords = { "tapes", "records", "research" }, Response = "\"Every step of the programme is on the tapes in his laboratory. Destroy the creatures and he can grow more. Destroy the tapes as well, and he'll be years starting again.\"" },
                           new Topic { Keywords = { "sarah", "friend", "prisoner", "prisoners", "girl" }, Response = "\"A young woman was brought in yesterday, with a Thal. They're in the cell block. Security has the only key.\"" },
                           new Topic { Keywords = { "thals", "thal", "war" }, Response = "\"A thousand years of war. Nobody remembers how it started. I'm not sure anyone remembers how to stop.\"" },
                       } },
            new Item { Id = "equipment", Name = "equipment", Article = "some", Nouns = { "equipment", "glassware", "microscope", "benches", "bench" }, Adjectives = { "scientific" },
                       Location = "lab", Portable = false, Scenery = true, Description = "Beakers, retorts and a microscope. None of it is anything you need." },
            new Item { Id = "celldoor", Name = "cell door", Nouns = { "door", "bars" }, Adjectives = { "cell", "barred", "bolted" }, Location = "cellblock", Portable = false, Scenery = true,
                       Openable = true, IsOpen = false, Lockable = true, IsLocked = true, KeyItemId = "keycard",
                       Description = "A heavy barred door with a magnetic lock. Through the bars you glimpse someone sitting on a bench." },
            new Item { Id = "sarah", Name = "Sarah", Article = "", Nouns = { "sarah", "sarah jane", "friend", "companion", "journalist" }, Adjectives = { "brave" }, Location = "cell", IsCharacter = true, Portable = false,
                       Npc = new NpcBehaviour
                       {
                           OpensDoors = true,
                           DepartureMessage = "Sarah hurries off, heading {direction}.",
                           ArrivalMessage = "Sarah comes hurrying in from the {direction}.",
                           IdleMessages = { "Sarah shivers and pulls her coat tighter.", "Sarah keeps glancing towards the mist.", "\"Come on,\" says Sarah. \"Let's get out of this horrible place.\"" },
                           IdleChance = 20,
                       },
                       Description = "Your friend Sarah – muddy, furious and very glad to see you.",
                       RoomDescription = "Sarah is here.",
                       Topics =
                       {
                           new Topic { Keywords = { "hello", "talk" }, Response = "\"About time! Well, what are we waiting for? You've got that look – the one that means trouble.\"" },
                           new Topic { Keywords = { "daleks", "davros", "mission", "incubator" }, Response = "\"If you're going to stop him, stop him. I'll be waiting where we arrived – that dreadful muddy place.\"" },
                       } },
            new Item { Id = "davros", Name = "Davros", Article = "", Nouns = { "davros", "scientist", "creator", "chief" }, Adjectives = { "chief", "crippled" },
                       Location = "davroslab", IsCharacter = true, Portable = false,
                       Description = "What is left of a man, sealed into a mobile life-support chair. A single blue lens glows where one eye should be, and a withered hand " +
                                     "hovers over a panel of switches. He regards you with cold curiosity.",
                       RoomDescription = "Davros watches you from his chair, his blue lens unblinking.",
                       Topics =
                       {
                           new Topic { Keywords = { "virus", "power", "question", "hypothetical", "life", "everything" },
                                       Response = "You put it to him: suppose he held in his hand a single glass phial that could end all life, everywhere. Would he break it?\n\n" +
                                                  "Davros is silent for a long moment. Then his hand rises, trembling, as if around something precious. \"Yes,\" he breathes. " +
                                                  "\"To hold such power – the power of life and death over every living thing – would set me above the gods. And through the Daleks, I shall have it.\"\n\n" +
                                                  "Any doubt you had about your mission is gone.",
                                       Actions = { new GameAction(ActionType.AwardScore, "davros", 5) } },
                           new Topic { Keywords = { "daleks", "dalek", "creatures", "machine", "mutants" }, Response = "\"The Dalek is the ultimate in survival. It has no weakness of pity, no flaw of conscience. It will outlast every other form of life.\"" },
                           new Topic { Keywords = { "kaleds", "kaled", "people" }, Response = "\"The Kaled race is dying. The Daleks are what it will become – whether it wishes to or not.\"" },
                           new Topic { Keywords = { "thals", "thal", "war" }, Response = "\"The war has been a useful teacher. It has shown me what a species must be to survive.\"" },
                           new Topic { Keywords = { "tapes", "records" }, Response = "\"My life's work,\" he says, and for the first time his voice holds something like warmth. \"Irreplaceable.\"" },
                           new Topic { Keywords = { "hello", "talk", "doctor", "you", "me" }, Response = "\"You are not a Kaled, nor a Thal. How interesting. I have many questions for you... in time.\"" },
                       } },
            new Item { Id = "harry", Name = "Harry", Article = "", Nouns = { "harry", "harry sullivan", "sullivan", "surgeon", "lieutenant" }, Adjectives = { "dazed", "muddy" },
                       Location = "trench", IsCharacter = true, Portable = false,
                       Description = "Surgeon-Lieutenant Harry Sullivan, Royal Navy – rather muddier than regulations allow, and looking thoroughly bewildered.",
                       RoomDescription = "Harry Sullivan is sitting on an ammunition box, rubbing his head.",
                       Topics =
                       {
                           new Topic { Keywords = { "hello", "talk", "harry", "come", "follow" },
                                       Response = "\"Doctor! Thank heavens. One minute we were in that police box of yours, the next – this.\" Harry scrambles to his feet. \"Lead on, old chap. I'll stick with you.\"",
                                       Actions = { new GameAction(ActionType.SetNpc, "harry", 1, "Following") } },
                           new Topic { Keywords = { "sarah" }, Response = "\"No sign of her, I'm afraid. I say, you don't think those chaps in black have got her?\"" },
                           new Topic { Keywords = { "gas", "mask" }, Response = "\"Nasty stuff. I've got my own mask, don't worry about me.\"" },
                           new Topic { Keywords = { "daleks", "davros", "kaleds" }, Response = "\"Never heard of 'em. Should I have?\"" },
                       },
                       Npc = new NpcBehaviour
                       {
                           ObeysOrders = true,
                           FollowMessage = "Harry follows close behind.",
                           ArrivalMessage = "Harry catches up with you, puffing.",
                           ObeyMessage = "\"Right-ho,\" says Harry.",
                           IdleMessages = { "Harry mutters something about the Navy never covering this sort of thing.", "\"I say,\" says Harry, \"cheerful sort of place, isn't it?\"", "Harry peers around warily." },
                           IdleChance = 15,
                       } },
            new Item { Id = "nyder", Name = "Nyder", Article = "", Nouns = { "nyder", "officer", "security chief" }, Adjectives = { "cold", "black-uniformed" },
                       Location = "office", IsCharacter = true, Portable = false,
                       Description = "Davros's chief of security: a thin, cold-eyed man in a black uniform with an iron cross at his throat. He misses nothing.",
                       Topics =
                       {
                           new Topic { Keywords = { "hello", "talk", "davros" }, Response = "\"The Chief Scientist is not to be disturbed,\" says Nyder. \"Whoever you are.\"" },
                           new Topic { Keywords = { "ronson", "traitor" }, Response = "Nyder's eyes narrow. \"Ronson? What do you know about Ronson?\"" },
                       },
                       Npc = new NpcBehaviour
                       {
                           Movement = NpcMovement.Patrol, Route = { "office", "davroslab" }, RouteLoops = false, MoveEvery = 3, MoveChance = 100,
                           ArrivalMessage = "Nyder strides in from the {direction}, glancing sharply at you.",
                           DepartureMessage = "Nyder marches out, heading {direction}.",
                           IdleMessages = { "Nyder watches you with cold suspicion.", "Nyder makes a note in a small black book." },
                           IdleChance = 25,
                       } },
            new Item { Id = "dalek", Name = "Dalek", Nouns = { "dalek", "machine", "creature" }, Adjectives = { "armed", "bronze", "living" },
                       Location = "", IsCharacter = true, Portable = false,
                       Description = "The travel machine – no longer empty. Its eyestalk tracks your every move and its gun-stick twitches. Its only weakness: it glides on a flat base, and can't manage stairs.",
                       Npc = new NpcBehaviour
                       {
                           Movement = NpcMovement.Seek, MoveChance = 100, MoveEvery = 2, Hostile = true, AttackChance = 40, Damage = 2,
                           AllowedRooms = { "davroslab", "corridor", "lab", "office", "incubator", "entrance" },
                           ArrivalMessage = "A Dalek glides in from the {direction}, eyestalk swivelling.",
                           DepartureMessage = "The Dalek glides away {direction}.",
                           AttackMessage = "\"EXTERMINATE!\" A searing bolt from the Dalek's gun-stick grazes you.",
                           KillMessage = "\"EXTERMINATE! EXTERMINATE!\" The world turns to negative, and then to nothing.",
                           IdleMessages = { "The Dalek's dome lights flash. \"YOU WILL BE EXTERMINATED!\"" }, IdleChance = 30,
                       } },
            new Item { Id = "machine", Name = "travel machine", Nouns = { "machine", "travel machine", "casing", "dalek", "mark", "prototype" }, Adjectives = { "travel", "metal", "squat", "bronze" },
                       Location = "davroslab", Portable = false, Scenery = true,
                       Description = "A squat metal casing, studded with hemispheres around its skirt, a dome on top and a single eyestalk. It is empty – for now." },
            new Item { Id = "tapes", Name = "tapes", Article = "the", Plural = true, Nouns = { "tapes", "tape", "records", "reels", "data bank", "bank" }, Adjectives = { "magnetic", "research", "project" },
                       Location = "davroslab", Portable = false, Scenery = true,
                       Description = "Spools of magnetic tape turn slowly in a data bank beside Davros's platform: the entire record of the Dalek programme." },
            new Item { Id = "instruments", Name = "instruments", Article = "the", Plural = true, Nouns = { "instruments", "monitors", "screens", "switches", "panel" }, Adjectives = { "blinking" },
                       Location = "davroslab", Portable = false, Scenery = true, Description = "Screens show the incubator tanks from a dozen angles." },
            new Item { Id = "incdoor", Name = "reinforced door", Nouns = { "door" }, Adjectives = { "reinforced", "incubator", "north" }, Location = "davroslab", Portable = false, Scenery = true,
                       Openable = true, IsOpen = false, Lockable = true, IsLocked = true, KeyItemId = "keycard",
                       Description = "A reinforced door with a magnetic card lock, marked with a warning in red." },
            new Item { Id = "tanks", Name = "incubator tanks", Article = "the", Plural = true, Nouns = { "tanks", "tank", "incubator", "incubators", "glass" }, Adjectives = { "glass", "glowing", "green" },
                       Location = "incubator", Portable = false, Scenery = true,
                       Description = "Row upon row of glass tanks. The creatures inside turn blind, lidless faces towards you as you pass." },
            new Item { Id = "mutants", Name = "creatures", Article = "the", Plural = true, Nouns = { "creatures", "mutants", "daleks", "creature", "mutant" }, Adjectives = { "small", "tentacled" },
                       Location = "incubator", Portable = false, Scenery = true, Description = "Small, soft, many-tentacled things. Hard to believe they could ever threaten anyone. Harder still to believe they won't." },
            new Item { Id = "charges", Name = "demolition charges", Article = "some", Plural = true, Nouns = { "charges", "explosives", "explosive", "satchel", "bombs", "charge" }, Adjectives = { "demolition", "canvas" },
                       Location = "", Weight = 2, Description = "A canvas satchel of demolition charges, with a reel of detonating wire." },
            new Item { Id = "wires", Name = "detonating wires", Article = "the", Plural = true, Nouns = { "wires", "wire", "leads", "ends" }, Adjectives = { "detonating", "trailing", "two", "bare" },
                       Location = "", Portable = false, Scenery = true,
                       Description = "Two bare wire ends, trailing from the charges among the tanks. Touch them together, and it's done." },
        });

        a.Variables.AddRange(new[]
        {
            new Variable { Name = "passShown", Description = "1 once the guard has seen the pass." },
            new Variable { Name = "helped", Description = "1 once Ronson has handed over the charges." },
            new Variable { Name = "sarah", Description = "1 once Sarah is free." },
            new Variable { Name = "tapesGone", Description = "1 once the Dalek tapes are destroyed." },
            new Variable { Name = "planted", Description = "1 once the charges are planted." },
            new Variable { Name = "hesitated", Description = "1 after the first time you hold the wires." },
            new Variable { Name = "boom", Description = "1 once the incubator room is destroyed." },
            new Variable { Name = "reunited", Description = "1 once you and Sarah meet again in No Man's Land." },
        });

        // ------------------------------------------------------------ custom commands
        a.Vocabulary.Verbs.Add(new VerbDefinition
        {
            Id = "plant", Words = { "plant", "rig", "hide explosives" },
            Grammar = { "* {held} in|on|under|among|around|beside {noun2}", "* {held}" },
            DefaultResponse = "This isn't the place for that.",
            Help = "Plant something somewhere, e.g. PLANT THE CHARGES AMONG THE TANKS.",
        });
        a.Vocabulary.Verbs.Add(new VerbDefinition
        {
            Id = "connect", Words = { "connect", "join", "complete", "touch together" },
            Grammar = { "* {noun} together", "* {noun} to|with {noun2}", "* {noun}" },
            DefaultResponse = "There's nothing to connect {the noun1} to.",
        });
        a.Vocabulary.Verbs.Add(new VerbDefinition
        {
            Id = "erase", Words = { "erase", "wipe", "degauss", "wreck" },
            Grammar = { "* {noun}", "* {noun} with {held2}" },
            DefaultResponse = "You can't erase that.",
        });
        a.Vocabulary.Adverbs.AddRange(new[] { "sneakily", "furtively", "casually" });
        a.Vocabulary.Replacements["sneak west"] = "quietly go west";
        a.Vocabulary.Replacements["sneak past sentry"] = "quietly go west";
        a.Vocabulary.Replacements["sneak past the sentry"] = "quietly go west";

        // ------------------------------------------------------------ triggers
        int n = 0;
        string Id() => $"g{++n}";

        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Search the soldier", Verb = "search|examine|touch|take", Noun1 = "soldier",
            Conditions = { new Condition(ConditionType.ItemIn, "pass", b: "") },
            Actions =
            {
                GameAction.Say("Gently, you go through the dead soldier's pockets. In his tunic you find a folded security pass."),
                new GameAction(ActionType.MoveItem, "pass", b: Locations.Here),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Gas without a mask", Verb = "go", RoomId = "trench", Priority = 5,
            Conditions = { new Condition(ConditionType.WordUsed, "east|e"), new Condition(ConditionType.ItemWorn, "mask", negate: true) },
            Actions = { new GameAction(ActionType.Lose, text: "You stride out into the haze. The first breath burns; the second is agony. You stagger, fall, and the yellow-green mist closes over you.\n\n(The gas is deadly. Perhaps something in the trench would help – type UNDO to try again.)") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Gas with a mask", Verb = "go", RoomId = "trench",
            Conditions = { new Condition(ConditionType.WordUsed, "east|e"), new Condition(ConditionType.ItemWorn, "mask") },
            Actions = { GameAction.Say("Breathing hard through the mask's filter, you pick your way through the drifting gas."), new GameAction(ActionType.Continue) },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Back through the gas", Verb = "go", RoomId = "entrance", Priority = 5,
            Conditions = { new Condition(ConditionType.WordUsed, "west|w"), new Condition(ConditionType.ItemWorn, "mask", negate: true) },
            Actions = { GameAction.Say("Without the mask? Not a chance. You'd be dead before you were halfway across.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Show the pass", Verb = "show|give", Noun1 = "pass", Noun2 = "guard",
            Conditions = { new Condition(ConditionType.VarEquals, "passShown", 0) },
            Actions =
            {
                GameAction.Say("The guard studies the pass, then your face, then the pass again. \"Scientific Corps. You people all look alike.\" He hammers on the door, which grinds open. \"Go on, then.\""),
                new GameAction(ActionType.SetVar, "passShown", 1),
                new GameAction(ActionType.SetOpen, "bunkerdoor", 1),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Guard blocks the door", Verb = "open|go|enter", RoomId = "entrance",
            Conditions = { new Condition(ConditionType.VarEquals, "passShown", 0), new Condition(ConditionType.WordUsed, "door|north|n|in|bunker|enter") },
            Actions = { GameAction.Say("The guard levels his rifle at you. \"Papers first. Show me your pass!\"") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Attack the guard", Verb = "attack|kick|break", Noun1 = "guard|sentry",
            Actions = { GameAction.Say("You have never been one for violence, and a man with a rifle is a poor place to start.") },
        });

        // Stealth past the sentry: an adverb puzzle
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Caught by the sentry", Verb = "go", RoomId = "corridor", Priority = 5,
            Conditions =
            {
                new Condition(ConditionType.WordUsed, "west|w"),
                new Condition(ConditionType.AdverbUsed, "quietly|silently|stealthily|sneakily|carefully|furtively|slowly|cautiously", negate: true),
            },
            Actions =
            {
                new GameAction(ActionType.PlaySound, "g_siren"),
                GameAction.Say("You march straight for the security door. The sentry jerks upright. \"Oi! Where do you think you're going?\" " +
                               "He marches you back to the entrance and stands glaring until you slink back inside. Perhaps a little more discretion next time."),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Past the sentry", Verb = "go", RoomId = "corridor",
            Conditions = { new Condition(ConditionType.WordUsed, "west|w"), new Condition(ConditionType.AdverbUsed, "quietly|silently|stealthily|sneakily|carefully|furtively|slowly|cautiously") },
            Actions = { GameAction.Say("Keeping to the shadows, you slip {adverb} past the dozing sentry and through the west door."), new GameAction(ActionType.Continue) },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Casually past the sentry", Verb = "go", RoomId = "corridor", Priority = 6,
            Conditions = { new Condition(ConditionType.WordUsed, "west|w"), new Condition(ConditionType.AdverbUsed, "casually|confidently|boldly") },
            Actions = { GameAction.Say("You stroll {adverb} towards the door as if you own the place. It nearly works. \"Oi!\" – the sentry isn't *that* asleep. Try something quieter.") },
        });

        // Sarah
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Free Sarah", Event = TriggerEvent.EnterRoom, RoomId = "cell", OnceOnly = true,
            Actions =
            {
                GameAction.Say("Sarah leaps up from the bench. \"I knew you'd come! Mind you, you took your time.\" She squeezes your arm. " +
                               "\"I heard the guards talking – Davros is going to finish his creatures any day now. You do what you have to. " +
                               "I'll get out the way we came in and wait in No Man's Land.\""),
                new GameAction(ActionType.SetVar, "sarah", 1),
                new GameAction(ActionType.NpcGoTo, "sarah", b: "wasteland"),
            },
        });

        // Destroying the tapes
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Destroy the tapes", Verb = "break|erase|attack|burn|cut|pull|take|kick", Noun1 = "tapes",
            Conditions = { new Condition(ConditionType.VarEquals, "tapesGone", 0) },
            Actions =
            {
                GameAction.Say("Before Davros can reach his switches you are at the data bank, tearing spools from their spindles and dragging tape through your hands until it lies in shining, useless heaps. " +
                               "Davros screams for the guards, but the corridor is empty – everyone is at that meeting."),
                new GameAction(ActionType.PlaySound, "g_siren"),
                new GameAction(ActionType.SetVar, "tapesGone", 1),
                new GameAction(ActionType.SetItemDescription, "tapes", text: "Tangled heaps of ruined tape, spilling from an empty data bank."),
                GameAction.Say("Davros's withered hand stabs at a switch. Beside him, the travel machine's eyestalk glows blue. It lifts, turns – and a grating voice fills the laboratory: \"EX-TER-MIN-ATE!\"\n\n(Run! And remember: it can't follow you down stairs.)"),
                new GameAction(ActionType.DestroyItem, "machine"),
                new GameAction(ActionType.MoveItem, "dalek", b: "davroslab"),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Tapes already gone", Verb = "break|erase|attack|burn|cut|pull|take|kick", Noun1 = "tapes",
            Actions = { GameAction.Say("There's nothing left of them to destroy.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Davros won't be harmed", Verb = "attack|kick|break|push|pull|take", Noun1 = "davros|machine",
            Actions = { GameAction.Say("You check yourself. Whatever else you are, you are not a murderer – and the chair is armoured besides.") },
        });

        // Planting the charges and the choice
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Plant the charges", Verb = "plant|insert|puton|drop|put|use", Noun1 = "charges", RoomId = "incubator",
            Conditions = { new Condition(ConditionType.ItemCarried, "charges") },
            Actions =
            {
                GameAction.Say("Working quickly, you tuck the charges among the incubator tanks and pay out the detonating wire until you stand by the door, two bare ends in your hands."),
                new GameAction(ActionType.DestroyItem, "charges"),
                new GameAction(ActionType.MoveItem, "wires", b: "incubator"),
                new GameAction(ActionType.SetVar, "planted", 1),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Plant elsewhere", Verb = "plant", Noun1 = "charges",
            Actions = { GameAction.Say("Here? It would do no good. The creatures are in the incubator room.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "The choice", Verb = "connect|touch|use|push|join", Noun1 = "wires",
            Conditions = { new Condition(ConditionType.VarEquals, "hesitated", 0) },
            Actions =
            {
                GameAction.Say("You hold the two wires a finger's breadth apart – and stop.\n\n" +
                               "These things will become the most ruthless killers in the universe. You have seen the worlds they will burn. " +
                               "And yet... to wipe out a whole species, here, now, before it has done a single wrong? If you do this, " +
                               "are you any better than they will be? And some good may yet come out of the terror they bring – whole peoples " +
                               "who would never have united, united against them.\n\nThe wires tremble in your hands. (CONNECT THE WIRES again if you are certain.)"),
                new GameAction(ActionType.SetVar, "hesitated", 1),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "The explosion", Verb = "connect|touch|use|push|join", Noun1 = "wires",
            Conditions = { new Condition(ConditionType.VarEquals, "hesitated", 1), new Condition(ConditionType.VarEquals, "boom", 0) },
            Actions =
            {
                new GameAction(ActionType.PlaySound, "g_explosion"),
                new GameAction(ActionType.ShowPicture, "g_boom"),
                GameAction.Say("You close your eyes and bring the ends together.\n\nThe floor heaves. Glass and fire roar through the incubator room as you throw " +
                               "yourself back through the door. When the smoke clears, the tanks are gone.\n\nYou know it isn't the end of them – Davros will " +
                               "begin again, and history cannot simply be deleted. But you have bought the universe time. Perhaps a thousand years of it."),
                new GameAction(ActionType.SetVar, "boom", 1),
                new GameAction(ActionType.DestroyItem, "wires"),
                new GameAction(ActionType.DestroyItem, "mutants"),
                new GameAction(ActionType.SetItemDescription, "tanks", text: "Twisted frames and shattered glass. Nothing lives here any more."),
                new GameAction(ActionType.SetRoomDescription, "incubator", text: "A blackened ruin of shattered glass and twisted metal. Smoke hangs in the air. The door is south."),
                new GameAction(ActionType.GoTo, "davroslab"),
            },
        });

        // Going home
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Don't take the ring off", Verb = "remove|drop|throw|give", Noun1 = "ring",
            Actions = { GameAction.Say("Take off the Time Ring? It's your only way home. Better not.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Home", Verb = "turn|use|touch|rub|switchon|push|pull", Noun1 = "ring", Priority = 10,
            Conditions =
            {
                new Condition(ConditionType.PlayerIn, "wasteland"),
                new Condition(ConditionType.VarEquals, "boom", 1),
                new Condition(ConditionType.VarEquals, "tapesGone", 1),
                new Condition(ConditionType.VarEquals, "sarah", 1),
                new Condition(ConditionType.NpcIn, "sarah", b: "wasteland"),
            },
            Actions =
            {
                new GameAction(ActionType.PlaySound, "g_demat"),
                new GameAction(ActionType.AwardScore, "home", 10),
                new GameAction(ActionType.Win, text: "Sarah grips your hand. You twist the ring. The mist, the wire, the distant guns all fade into a rising, groaning roar – " +
                                                      "and then there is only the familiar hum of the TARDIS console room around you.\n\n\"Did we do it?\" Sarah asks.\n\n" +
                                                      "You think of the creatures in their tanks, of Davros and his shaking hand. \"We did something,\" you say. \"Out of great evil, some good may come. " +
                                                      "Let's hope it's enough.\""),
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Not yet", Verb = "turn|use|touch|rub|switchon|push|pull", Noun1 = "ring",
            Actions = { GameAction.Say("The ring stays cold. Your mission isn't finished – and you won't leave without Sarah. (She said she'd wait in No Man's Land.)") },
        });
        // The reunion happens whichever of you reaches No Man's Land second.
        var reunion = new List<GameAction>
        {
            GameAction.Say("\"There you are!\" Sarah throws her arms around you. \"Now can we please go home?\""),
            new GameAction(ActionType.SetVar, "reunited", 1),
        };
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Sarah reaches you in No Man's Land", Event = TriggerEvent.NpcArrives, Subject = "sarah", RoomId = "wasteland",
            Conditions = { new Condition(ConditionType.VarEquals, "reunited", 0) },
            Actions = reunion.ToList(),
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "You find Sarah in No Man's Land", Event = TriggerEvent.EnterRoom, RoomId = "wasteland",
            Conditions = { new Condition(ConditionType.VarEquals, "reunited", 0), new Condition(ConditionType.NpcIn, "sarah", b: "wasteland") },
            Actions = reunion.ToList(),
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Harry and the gas", Event = TriggerEvent.EnterRoom, RoomId = "entrance", OnceOnly = true,
            Conditions = { new Condition(ConditionType.NpcFollowing, "harry") },
            Actions = { GameAction.Say("Harry emerges from the gas behind you, coughing inside a battered respirator of his own. \"Ghastly stuff!\"") },
        });

        // Atmosphere and the deadline
        a.RandomEvents.AddRange(new[]
        {
            new RandomEvent
            {
                Id = "gev_shell", Name = "Thal shell lands", Where = EventLocation.Anywhere, Rooms = { "wasteland", "trench" }, Chance = 12, Cooldown = 4,
                WitnessMessage = "A shell screams down out of the mist and bursts nearby, showering you with mud and stones!",
                DistantMessage = "Somewhere out in No Man's Land, a shell lands with a dull crump. The ground shivers.",
                Actions = { new GameAction(ActionType.RunTrigger, "shell_hits") },
            },
            new RandomEvent
            {
                Id = "gev_mines", Name = "Thal sappers lay a mine", Where = EventLocation.AwayFromPlayer, Rooms = { "wasteland", "trench" }, Chance = 6, Cooldown = 10, MaxTimes = 2,
                Conditions = { new Condition(ConditionType.RoomTrapped, Locations.EventRoom, negate: true) },
                Actions = { new GameAction(ActionType.SetTrap, Locations.EventRoom, 2, "You spot the three prongs of a mine poking out of the mud – and step well around it.",
                                               "Click. A mine! You throw yourself flat as it bursts, and shrapnel tears at your coat.") },
            },
            new RandomEvent
            {
                Id = "gev_power", Name = "Power failure", Where = EventLocation.Anywhere, Rooms = { "corridor" }, Chance = 6, Cooldown = 8,
                Conditions = { new Condition(ConditionType.RoomHasFlag, Locations.EventRoom, b: "powercut", negate: true) },
                WitnessMessage = "The lights stutter, buzz – and die.",
                DistantMessage = "Somewhere in the bunker, a generator coughs and falls silent.",
                Actions = { new GameAction(ActionType.SetDark, Locations.EventRoom, 1), new GameAction(ActionType.SetRoomFlag, Locations.EventRoom, 1, "powercut") },
            },
            new RandomEvent
            {
                Id = "gev_power_back", Name = "Power restored", Where = EventLocation.Anywhere, Rooms = { "corridor" }, Chance = 35, Cooldown = 1,
                Conditions = { new Condition(ConditionType.RoomHasFlag, Locations.EventRoom, b: "powercut") },
                WitnessMessage = "With a clunk, the lights flicker back on.",
                Actions = { new GameAction(ActionType.SetDark, Locations.EventRoom, 0), new GameAction(ActionType.SetRoomFlag, Locations.EventRoom, 0, "powercut") },
            },
            new RandomEvent
            {
                Id = "gev_tannoy", Name = "Loudspeaker announcement", Where = EventLocation.PlayerRoom, Rooms = { "corridor", "lab", "office", "cellblock", "davroslab", "entrance" },
                Chance = 8, Cooldown = 10,
                WitnessMessage = "A loudspeaker crackles: \"Attention. Elite scientific staff to report for the meeting. By order of the Chief Scientist.\"",
            },
            new RandomEvent
            {
                Id = "gev_arrest", Name = "Ronson is arrested", Where = EventLocation.AwayFromPlayer, Rooms = { "lab" }, Chance = 15, MaxTimes = 1,
                Conditions = { new Condition(ConditionType.VarEquals, "helped", 1) },
                DistantMessage = "From the direction of the scientific wing comes shouting, then a scuffle, then silence. Someone has been arrested.",
                Actions =
                {
                    new GameAction(ActionType.DestroyItem, "ronson"),
                    new GameAction(ActionType.SetRoomDescription, "lab", text: "Workbenches crowded with glassware, now overturned and smashed. Ronson is gone – the scuffle marks on the floor tell you how. The corridor is west."),
                },
            },
        });
        a.Triggers.Add(new Trigger
        {
            Id = "shell_hits", Name = "shell_hits", Event = TriggerEvent.Subroutine,
            Conditions = { new Condition(ConditionType.PlayerIn, Locations.EventRoom) },
            Actions = { new GameAction(ActionType.HurtPlayer, n: 1) },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Warning", Event = TriggerEvent.Timer, Turn = 90,
            Conditions = { new Condition(ConditionType.VarEquals, "boom", 0) },
            Actions = { new GameAction(ActionType.PlaySound, "g_siren"), GameAction.Say("A voice crackles from a loudspeaker: \"Attention. Final stage of the Dalek programme begins shortly. All personnel to stations.\"") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Too late", Event = TriggerEvent.Timer, Turn = 120,
            Conditions = { new Condition(ConditionType.VarEquals, "boom", 0) },
            Actions = { new GameAction(ActionType.Lose, text: "A grating, metallic voice echoes along every corridor of the bunker, repeating a single word over and over. The first Daleks are awake. You were too late.") },
        });
        a.Triggers.Add(new Trigger
        {
            Id = Id(), Name = "Davros notices you", Event = TriggerEvent.EnterRoom, RoomId = "davroslab", OnceOnly = true,
            Actions = { GameAction.Say("Davros's chair swivels to face you with a faint electric whine. \"A stranger,\" he says softly. \"Come closer. Ask me what you will.\"") },
        });

        // ------------------------------------------------------------ puzzles
        a.Puzzles.AddRange(new[]
        {
            new Puzzle { Id = "gz_gas", Name = "Through the gas", Points = 10, HintRoomIds = { "trench", "wasteland" },
                         Hints = { "The haze to the east is poison.", "There's something lying in the mud of the trench.", "TAKE THE MASK, WEAR IT, then go EAST." },
                         SolvedWhen = { new Condition(ConditionType.RoomVisited, "entrance") } },
            new Puzzle { Id = "gz_pass", Name = "Papers, please", Points = 10, HintRoomIds = { "entrance", "trench" },
                         Hints = { "The guard wants to see your authority.", "The dead soldier in the trench might have something.", "SEARCH THE SOLDIER in the trench, then SHOW THE PASS TO THE GUARD." },
                         SolvedWhen = { new Condition(ConditionType.VarEquals, "passShown", 1) } },
            new Puzzle { Id = "gz_sneak", Name = "Past the sentry", Points = 0, HintRoomIds = { "corridor" },
                         Hints = { "The sentry stops anyone who walks past him openly.", "How you do something matters as much as what you do.", "Try QUIETLY GO WEST." },
                         SolvedWhen = { new Condition(ConditionType.RoomVisited, "office") } },
            new Puzzle { Id = "gz_sarah", Name = "Rescue Sarah", Points = 15, HintRoomIds = { "cellblock", "office", "corridor" },
                         Hints = { "The cell door has a magnetic lock.", "Security offices tend to keep keys.", "Get the keycard from the drawer in the security office, then UNLOCK THE CELL DOOR WITH THE KEYCARD." },
                         SolvedWhen = { new Condition(ConditionType.VarEquals, "sarah", 1) } },
            new Puzzle { Id = "gz_tapes", Name = "The records", Points = 15, HintRoomIds = { "davroslab", "lab" },
                         Hints = { "Destroying the creatures alone won't stop Davros starting again.", "Ronson knows where the records are kept.", "DESTROY THE TAPES in the central laboratory." },
                         SolvedWhen = { new Condition(ConditionType.VarEquals, "tapesGone", 1) } },
            new Puzzle { Id = "gz_charges", Name = "Demolition", Points = 5, HintRoomIds = { "lab", "incubator", "davroslab" },
                         Hints = { "You need something to deal with the incubator.", "Ronson seems sympathetic. Ask him about the incubator.", "In the incubator room, PLANT THE CHARGES AMONG THE TANKS." },
                         SolvedWhen = { new Condition(ConditionType.VarEquals, "planted", 1) } },
            new Puzzle { Id = "gz_choice", Name = "The choice", Points = 20, HintRoomIds = { "incubator" },
                         Hints = { "It's a hard decision. Take your time.", "CONNECT THE WIRES – twice, if you're sure." },
                         SolvedWhen = { new Condition(ConditionType.VarEquals, "boom", 1) } },
            new Puzzle { Id = "gz_home", Name = "Home", Points = 0, HintRoomIds = { "wasteland" },
                         Hints = { "When everything's done, find Sarah where you arrived.", "TWIST THE RING in No Man's Land." } },
        });

        AddGenesisPictures(a);
        AddGenesisSounds(a);
        return a;
    }

    /// <summary>Recordings from the built-in CC0 sound library (credits in assets/sounds/CREDITS.md).</summary>
    private static void AddGenesisSounds(Adventure a)
    {
        void Add(string id, string name, string library, double volume = 1)
        {
            a.Assets[$"sounds/{library}.wav"] = SoundLibrary.Embedded(library) ?? throw new InvalidOperationException($"Missing library sound {library}.");
            a.Sounds.Add(new SoundAsset { Id = id, Name = name, AssetName = $"sounds/{library}.wav", Volume = volume });
        }
        Add("g_wind", "Wind over No Man's Land", "wind", 0.6);
        Add("g_hum", "Bunker machinery", "engine_hum", 0.4);
        Add("g_siren", "Alarm", "alarm", 0.6);
        Add("g_explosion", "Explosion", "explosion_deep");
        Add("g_demat", "Dematerialisation", "force_field", 0.8);
    }

    // Colour indices (Palettes.Extended)
    private const int Magenta = 3, Green4 = 4, Cyan = 5, Yellow6 = 6, BrightGreen = 12, BrightMagenta = 11, Olive = 21, Pink = 29, Burgundy = 30;

    private static DrawCommand Circle(int cx, int cy, int r, bool filled = true) => Oval(cx - r, cy - r, cx + r, cy + r, filled);

    private static DrawCommand[] Person(int x, int feet, int body, int head, int legs = DarkGrey, int hair = -1)
    {
        var list = new List<DrawCommand>
        {
            Ink(legs), Box(x - 6, feet - 20, x - 2, feet), Box(x + 2, feet - 20, x + 6, feet),
            Ink(body), Poly(x - 9, feet - 20, x + 9, feet - 20, x + 8, feet - 46, x - 8, feet - 46),
            Box(x - 12, feet - 44, x - 9, feet - 26), Box(x + 9, feet - 44, x + 12, feet - 26),
            Ink(Tan), Circle(x, feet - 53, 7),
        };
        if (head >= 0) { list.Add(Ink(head)); list.Add(Oval(x - 8, feet - 62, x + 8, feet - 52)); }
        if (hair >= 0) { list.Add(Ink(hair)); list.Add(Oval(x - 8, feet - 61, x + 8, feet - 53)); list.Add(Box(x - 8, feet - 56, x - 5, feet - 44)); list.Add(Box(x + 5, feet - 56, x + 8, feet - 44)); }
        return list.ToArray();
    }

    private static DrawCommand[] Mist(int seed, int color, int top, int bottom)
    {
        var rnd = new Random(seed);
        var list = new List<DrawCommand> { Ink(color) };
        for (int i = 0; i < 9; i++)
        {
            int x = rnd.Next(-40, 240), y = rnd.Next(top, bottom);
            list.Add(Oval(x, y, x + rnd.Next(50, 110), y + rnd.Next(8, 18)));
        }
        return list.ToArray();
    }

    private static DrawCommand[] BarbedWire(int y, int x1, int x2)
    {
        var list = new List<DrawCommand> { Ink(DarkBrown) };
        for (int x = x1; x <= x2; x += 38) { list.Add(Box(x, y - 22, x + 3, y + 4)); }
        list.Add(Ink(Grey));
        for (int x = x1; x < x2; x += 6) { list.Add(Line(x, y - 14 + (x / 6 % 2) * 6, x + 6, y - 14 + ((x / 6 + 1) % 2) * 6)); }
        for (int x = x1; x < x2; x += 7) { list.Add(Line(x, y - 4 + (x / 7 % 2) * 5, x + 7, y - 4 + ((x / 7 + 1) % 2) * 5)); }
        return list.ToArray();
    }

    private static DrawCommand[] Dome(int cx, int horizon, int rx, int ry, int color, int shade)
    {
        return new[]
        {
            Ink(color), Oval(cx - rx, horizon - ry, cx + rx, horizon + ry),
            Ink(shade), Line(cx - rx + 10, horizon - ry / 3, cx + rx - 10, horizon - ry / 3), Line(cx - rx / 2, horizon - ry + 8, cx + rx / 2, horizon - ry + 8),
            Line(cx, horizon - ry, cx, horizon),
        };
    }

    private static DrawCommand[] TravelMachine(int cx, int bottom, int scale = 1)
    {
        int s = scale;
        var list = new List<DrawCommand>
        {
            Ink(Brown), Poly(cx - 22 * s, bottom, cx + 22 * s, bottom, cx + 14 * s, bottom - 44 * s, cx - 14 * s, bottom - 44 * s),
            Ink(Tan), Box(cx - 13 * s, bottom - 56 * s, cx + 13 * s, bottom - 44 * s),
            Ink(DarkGrey), Line(cx - 13 * s, bottom - 50 * s, cx + 13 * s, bottom - 50 * s),
            Ink(Grey), Oval(cx - 12 * s, bottom - 72 * s, cx + 12 * s, bottom - 52 * s),
            Ink(DarkGrey), Line(cx + 6 * s, bottom - 64 * s, cx + 24 * s, bottom - 66 * s),
            Ink(BrightBlue), Circle(cx + 25 * s, bottom - 66 * s, 2 * s),
            Ink(DarkGrey), Line(cx + 10 * s, bottom - 48 * s, cx + 26 * s, bottom - 44 * s), Line(cx - 10 * s, bottom - 48 * s, cx - 26 * s, bottom - 42 * s),
            Ink(DarkBrown),
        };
        for (int row = 0; row < 3; row++)
            for (int col = -2; col <= 2; col++)
                list.Add(Circle(cx + col * 7 * s - (row - 1) * s, bottom - 10 * s - row * 12 * s, 2 * s));
        return list.ToArray();
    }

    private static void AddGenesisPictures(Adventure a)
    {
        a.Pictures.Add(Pic("g_wasteland", "No Man's Land",
            new[] { Clear(Slate), Ink(Grey) },
            Mist(21, Grey, 5, 60),
            Dome(200, 100, 46, 26, DarkGrey, Slate),
            new[] { Ink(Brown), Box(0, 100, 255, 175), Ink(DarkBrown), Poly(0, 118, 70, 108, 150, 116, 255, 106, 255, 175, 0, 175) },
            new[] { Ink(DarkGrey), Oval(30, 128, 90, 142), Oval(150, 145, 230, 162), Oval(100, 112, 140, 120), Ink(Black), Oval(40, 131, 80, 139), Oval(162, 149, 218, 158) },
            BarbedWire(124, 0, 255),
            new[] { Ink(Black), Line(60, 100, 60, 60), Line(60, 75, 48, 62), Line(60, 70, 72, 55), Line(60, 82, 68, 76) },
            Mist(22, Wheat, 90, 130)));

        a.Pictures.Add(Pic("g_trench", "Kaled Trench",
            new[] { Clear(Slate) },
            Mist(31, Grey, 0, 30),
            new[] { Ink(Brown), Poly(0, 30, 70, 50, 70, 175, 0, 175), Poly(255, 30, 185, 50, 185, 175, 255, 175), Ink(DarkBrown), Box(70, 50, 185, 175) },
            new[] { Ink(Wheat), Oval(0, 22, 40, 34), Oval(34, 30, 74, 42), Oval(180, 30, 220, 42), Oval(215, 22, 255, 34), Ink(Tan), Line(0, 28, 74, 40), Line(180, 38, 255, 28) },
            new[] { Ink(Tan), Line(70, 140, 185, 140), Line(70, 150, 185, 150), Line(70, 160, 185, 160), Line(70, 170, 185, 170) },
            new[] { Ink(DarkGreen), Poly(95, 120, 140, 110, 150, 125, 105, 135), Ink(DarkGrey), Oval(84, 110, 104, 122), Ink(Tan), Oval(88, 118, 100, 128), Ink(DarkGreen), Box(140, 118, 160, 124) },
            new[] { Ink(Grey), Oval(150, 150, 170, 166), Ink(Black), Circle(156, 156, 2), Circle(164, 156, 2), Ink(DarkGrey), Circle(160, 164, 3) },
            new[] { Ink(Olive), Oval(170, 60, 255, 80), Oval(200, 45, 255, 62) }));

        a.Pictures.Add(Pic("g_entrance", "Bunker Entrance",
            new[] { Clear(Olive), Ink(Green) },
            Mist(41, Green, 0, 40),
            new[] { Ink(DarkGrey), Oval(-40, 20, 296, 240), Ink(Slate), Oval(-20, 34, 276, 230, false) },
            new[] { Ink(Slate), Box(96, 70, 160, 150), Ink(DarkGrey), Frame(96, 70, 160, 150), Line(128, 70, 128, 150) },
            Enumerable.Range(0, 5).SelectMany(i => new[] { Ink(Grey), Circle(101, 78 + i * 16, 1), Circle(155, 78 + i * 16, 1) }).ToArray(),
            new[] { Ink(Brown), Box(0, 150, 255, 175) },
            Person(200, 160, Black, Black, Black),
            new[] { Ink(Grey), Oval(193, 104, 207, 114), Ink(DarkGrey), Line(214, 120, 226, 96), Line(215, 121, 227, 97) },
            Mist(42, Olive, 140, 172)));

        a.Pictures.Add(Pic("g_corridor", "Bunker Corridor",
            new[] { Clear(DarkGrey), Ink(Grey), Poly(0, 0, 100, 60, 100, 116, 0, 175), Poly(255, 0, 156, 60, 156, 116, 255, 175) },
            new[] { Ink(Slate), Poly(0, 175, 100, 116, 156, 116, 255, 175), Ink(Black), Box(100, 60, 156, 116) },
            new[] { Ink(Yellow), Poly(60, 10, 90, 10, 84, 16, 66, 16), Poly(112, 44, 144, 44, 140, 48, 116, 48), Poly(165, 10, 195, 10, 189, 16, 171, 16) },
            new[] { Ink(DarkGrey), Poly(14, 40, 44, 58, 44, 140, 14, 160), Ink(Black), Line(14, 40, 44, 58) },
            new[] { Ink(DarkGrey), Poly(242, 40, 212, 58, 212, 140, 242, 160) },
            Person(58, 158, Black, Black, Black)));

        a.Pictures.Add(Pic("g_office", "Security Office",
            new[] { Clear(Grey), Ink(Slate), Box(0, 120, 255, 175) },
            new[] { Ink(Wheat), Box(30, 20, 130, 80), Ink(Brown), Frame(30, 20, 130, 80), Ink(BrightRed), Circle(60, 40, 2), Circle(90, 55, 2), Circle(110, 35, 2), Ink(Blue), Circle(50, 62, 2), Circle(100, 70, 2), Ink(DarkBrown), Line(40, 50, 120, 48) },
            new[] { Ink(Black), Box(170, 20, 220, 60), Ink(BrightRed), Poly(180, 28, 210, 28, 195, 52) },
            new[] { Ink(DarkGrey), Box(60, 110, 200, 118), Box(66, 118, 72, 160), Box(188, 118, 194, 160), Box(140, 118, 186, 136), Ink(Grey), Frame(140, 118, 186, 136), Line(158, 127, 168, 127) },
            new[] { Ink(BrightWhite), Poly(90, 104, 118, 102, 120, 110, 92, 111), Ink(Black), Line(95, 105, 112, 104), Line(95, 107, 115, 106) }));

        a.Pictures.Add(Pic("g_lab", "Scientific Wing",
            new[] { Clear(BrightWhite), Ink(Grey), Box(0, 110, 255, 175), Ink(Slate), Box(0, 104, 255, 112) },
            new[] { Ink(Sky), Poly(20, 104, 30, 70, 40, 104), Ink(BrightGreen), Box(50, 84, 58, 104), Ink(Cyan), Oval(66, 88, 84, 104), Ink(Magenta), Box(92, 90, 98, 104) },
            new[] { Ink(DarkGrey), Box(200, 70, 214, 104), Box(196, 60, 218, 70), Circle(207, 56, 4) },
            Person(150, 170, BrightWhite, -1, Slate, Grey),
            new[] { Ink(Black), Line(143, 115, 157, 115) }));

        a.Pictures.Add(Pic("g_cellblock", "Cell Block",
            new[] { Clear(DarkGrey), Ink(Slate), Box(0, 140, 255, 175) },
            new[] { Ink(Black), Box(70, 30, 186, 140), Ink(Grey) },
            Enumerable.Range(0, 9).Select(i => Box(76 + i * 13, 30, 79 + i * 13, 140)).Prepend(Ink(Grey)).ToArray(),
            new[] { Ink(Grey), Box(70, 30, 186, 36), Box(70, 84, 186, 88), Ink(BrightRed), Box(190, 80, 200, 94), Ink(Yellow), Circle(195, 87, 1) },
            new[] { Ink(Wheat), Oval(118, 70, 132, 86), Ink(Brown), Oval(116, 66, 134, 78) },
            new[] { Ink(Slate), Poly(0, 175, 30, 120, 50, 120, 20, 175), Line(10, 160, 36, 160), Line(20, 145, 42, 145), Line(28, 132, 46, 132) }));

        a.Pictures.Add(Pic("g_cell", "Cell",
            new[] { Clear(Grey), Ink(Slate), Box(0, 130, 255, 175), Ink(DarkGrey), Box(150, 110, 240, 118), Box(156, 118, 160, 130), Box(230, 118, 234, 130) },
            new[] { Ink(DarkGrey), Box(20, 20, 60, 60), Ink(Sky) },
            Enumerable.Range(0, 4).Select(i => Box(24 + i * 10, 20, 27 + i * 10, 60)).Prepend(Ink(DarkGrey)).ToArray(),
            Person(110, 168, Pink, -1, Royal, Brown)));

        a.Pictures.Add(Pic("g_davroslab", "Central Laboratory",
            new[] { Clear(DarkGrey), Ink(Slate), Box(0, 130, 255, 175), Ink(Grey), Box(60, 120, 200, 132) },
            new[] { Ink(Black), Box(4, 20, 60, 110), Box(196, 20, 252, 110), Ink(Blue), Box(10, 26, 54, 50), Box(202, 26, 246, 50), Ink(BrightGreen), Line(12, 40, 22, 32), Line(22, 32, 34, 44), Line(34, 44, 52, 30), Ink(BrightCyan), Oval(210, 30, 238, 46, false) },
            Enumerable.Range(0, 6).SelectMany(i => new[] { Ink(i % 2 == 0 ? BrightRed : Yellow), Circle(12 + i * 8, 62, 2), Ink(i % 3 == 0 ? BrightGreen : BrightBlue), Circle(204 + i * 8, 62, 2) }).ToArray(),
            new[] { Ink(Black), Circle(20, 88, 10), Circle(44, 88, 10), Ink(Grey), Circle(20, 88, 7, false), Circle(44, 88, 7, false) },
            new[] { Ink(DarkGrey), Poly(100, 126, 150, 126, 142, 88, 108, 88), Ink(Black), Circle(110, 118, 2), Circle(120, 118, 2), Circle(130, 118, 2), Circle(140, 118, 2) },
            new[] { Ink(Black), Box(114, 60, 136, 90), Ink(Grey), Box(112, 88, 138, 92), Ink(Wheat), Oval(116, 46, 134, 64), Ink(DarkGrey), Oval(116, 44, 134, 54), Ink(BrightBlue), Circle(125, 55, 2), Ink(Wheat), Box(136, 84, 146, 88) },
            TravelMachine(180, 128)));

        var incubator = new[]
        {
            new[] { Clear(Black), Ink(DarkGrey), Box(0, 140, 255, 175) },
            Enumerable.Range(0, 5).SelectMany(i => new[]
            {
                Ink(DarkGreen), Box(10 + i * 50, 40, 44 + i * 50, 136),
                Ink(Green), Box(13 + i * 50, 50, 41 + i * 50, 133),
                Ink(DarkGreen), Oval(18 + i * 50, 80, 36 + i * 50, 100), Line(22 + i * 50, 100, 18 + i * 50, 112), Line(28 + i * 50, 100, 28 + i * 50, 116), Line(33 + i * 50, 100, 38 + i * 50, 112),
                Ink(BrightGreen), Circle(24 + i * 50, 88, 1),
                Ink(Grey), Frame(10 + i * 50, 40, 44 + i * 50, 136), Box(8 + i * 50, 34, 46 + i * 50, 40),
            }).ToArray(),
        };
        a.Pictures.Add(Pic("g_incubator", "Incubator Room", incubator));
        a.Pictures.Add(Pic("g_boom", "The Incubator Explodes", incubator.Append(new[]
        {
            Ink(Orange), Poly(0, 175, 20, 60, 50, 110, 80, 20, 110, 90, 140, 10, 170, 100, 200, 30, 230, 110, 255, 50, 255, 175),
            Ink(Yellow), Poly(20, 175, 50, 100, 80, 140, 120, 60, 150, 130, 190, 80, 220, 175),
            Ink(BrightWhite), Poly(80, 175, 110, 120, 130, 150, 160, 110, 180, 175),
            Ink(DarkGrey), Oval(30, 0, 120, 30), Oval(140, 0, 240, 24),
        }).ToArray()));
    }
}
