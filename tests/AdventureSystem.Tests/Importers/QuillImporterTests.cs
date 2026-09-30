using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Importers.Quill;

namespace AdventureSystem.Tests.Importers;

/// <summary>
/// Lays out a Quill database (and optionally an Illustrator graphics database) in a 48K .SNA image exactly as the
/// Quill runtime keeps it in memory: complemented text, pointer tables, vocabulary, connections and condition tables.
/// </summary>
internal sealed class QuillSnapshotBuilder
{
    public readonly byte[] Mem = new byte[65536];
    private int next = 0x7000;

    public int Put(params byte[] data)
    {
        int a = next;
        data.CopyTo(Mem, a);
        next += data.Length;
        return a;
    }

    public void Poke(int address, params byte[] data) => data.CopyTo(Mem, address);
    public void PokeWord(int address, int value) { Mem[address] = (byte)value; Mem[address + 1] = (byte)(value >> 8); }

    /// <summary>Quill text: every byte complemented, terminated by 1Fh (stored as E0h). Raw bytes may be given as \u00XX escapes.</summary>
    public static byte[] Text(string s, bool spectrum = true)
    {
        var bytes = s.Select(c => (byte)(0xFF - (byte)c)).ToList();
        bytes.Add(spectrum ? (byte)0xE0 : (byte)0xFF);
        return bytes.ToArray();
    }

    /// <summary>Stores the texts and returns the address of their pointer table.</summary>
    public int TextTable(IEnumerable<string> texts)
    {
        var addresses = texts.Select(t => Put(Text(t))).ToList();
        int table = next;
        foreach (var a in addresses) Put((byte)a, (byte)(a >> 8));
        return table;
    }

    public int Vocabulary(params (string Word, int Number)[] words)
    {
        int a = next;
        foreach (var (w, n) in words)
        {
            var padded = w.PadRight(4);
            Put(padded.Select(c => (byte)(0xFF - (byte)c)).Append((byte)n).ToArray());
        }
        Put(0, 0, 0, 0, 0);
        return a;
    }

    public int Connections(params (int Word, int Target)[][] perLocation)
    {
        var lists = perLocation.Select(l => Put(l.SelectMany(c => new[] { (byte)c.Word, (byte)c.Target }).Append((byte)0xFF).ToArray())).ToList();
        int table = next;
        foreach (var a in lists) Put((byte)a, (byte)(a >> 8));
        return table;
    }

    /// <summary>Condition table: entries (verb, noun, conditions bytes, action bytes); FFh terminators are added.</summary>
    public int Table(params (int Verb, int Noun, byte[] Conditions, byte[] Actions)[] entries)
    {
        var bodies = entries.Select(e => Put(e.Conditions.Append((byte)0xFF).Concat(e.Actions).Append((byte)0xFF).ToArray())).ToList();
        int table = next;
        for (int i = 0; i < entries.Length; i++)
            Put((byte)entries[i].Verb, (byte)entries[i].Noun, (byte)bodies[i], (byte)(bodies[i] >> 8));
        Put(0);
        return table;
    }

    public byte[] ToSna()
    {
        var sna = new byte[27 + 49152];
        Array.Copy(Mem, 0x4000, sna, 27, 49152);
        return sna;
    }
}

public class QuillImporterTests
{
    // Word numbers
    private const int N = 1, S = 2, U = 3, D = 4, GET = 20, DROP = 21, OPEN = 22, SCORE = 23, WEAR = 24, LAMP = 50, COIN = 51, MAP = 52, DOOR = 53, PORTAL = 60;

    // Late (version C) action numbers
    private const byte INVEN = 0, DESC = 1, END = 3, DONE = 4, OK = 5, SCOREA = 10, AUTOG = 13, AUTOW = 15, PAUSE = 17, INK = 19,
        GOTO = 21, MESSAGE = 22, GETA = 24, SET = 31, PLUS = 33, LET = 35, PLACE = 30;
    // Conditions
    private const byte AT = 0, PRESENT = 4, CARRIED = 8, ZERO = 11, GT = 14, NOTWORN = 7;

    private static readonly string[] LateSystemMessages =
    {
        "It's too dark to see anything.", "I can also see:", "What now?", "What now?", "What now?", "What now?",
        "I don't understand.", "I can't go in that direction.", "I can't do that.", "I have with me:", " (worn)",
        "Nothing at all.", "Quit?", "END OF GAME", "Bye.", "OK.", "Press any key.", "You have taken ", " turn", "s", ".",
        "You have scored ", "%.", "I'm not wearing it.", "My hands are full.", "I already have it.", "It's not here.",
        "I can't carry any more.", "I don't have it.", "I'm already wearing it.", "Y", "N",
    };

    /// <summary>Builds a later-format ("C") Spectrum Quill game with Illustrator pictures.</summary>
    private static byte[] BuildLateGame(bool withGraphics = true)
    {
        var b = new QuillSnapshotBuilder();
        const int sig = 0x6B85;
        // Colour table signature: INK 6, PAPER 1, FLASH 0, BRIGHT 0, INVERSE 0, OVER 0, BORDER 1.
        b.Poke(sig, 0x10, 6, 0x11, 1, 0x12, 0, 0x13, 0, 0x14, 0, 0x15, 0, 1);
        const int header = sig + 13;
        b.Poke(header, 4, 3, 3, 2, (byte)LateSystemMessages.Length);
        const int h = header + 1;
        int dict = h + 29;
        // Compression dictionary: 80h marker, then words with bit 7 set on their last character.
        b.Poke(dict, 0x80, (byte)'t', (byte)'h', (byte)'e', (byte)(' ' | 0x80), (byte)'o', (byte)(' ' | 0x80));

        int locs = b.TextTable(new[]
        {
            "You are in a dark cellar. Stairs lead up.",
            "Rows of dusty bottles line walls" + "here. I'm in the kitchen.",
            "\u00A5" + "garden." + "\u0010\u0002", // token 165 ("the ", stored complemented as 5Ah) + text; ends with a colour control INK 2 (complemented param)
        });
        int objs = b.TextTable(new[] { "a brass lamp", "some gold coins", "the old map" });
        int msgs = b.TextTable(new[] { "The door creaks open.", "You have won!" });
        int sys = b.TextTable(LateSystemMessages);
        int vocab = b.Vocabulary(("N", N), ("NORT", N), ("S", S), ("SOUT", S), ("U", U), ("UP", U), ("D", D), ("DOWN", D),
            ("GET", GET), ("TAKE", GET), ("DROP", DROP), ("OPEN", OPEN), ("SCOR", SCORE), ("WEAR", WEAR),
            ("LAMP", LAMP), ("COIN", COIN), ("MAP", MAP), ("DOOR", DOOR), ("PORT", PORTAL));
        int conn = b.Connections(
            new[] { (U, 1) },
            new[] { (D, 0), (N, 2), (PORTAL, 0) },
            new[] { (S, 1) });
        int pos = b.Put(0, 254, 252);
        int objmap = b.Put(LAMP, COIN, MAP);
        int resp = b.Table(
            (GET, LAMP, new byte[] { PRESENT, 0 }, new byte[] { GETA, 0, OK }),
            (GET, 255, Array.Empty<byte>(), new byte[] { AUTOG, DONE }),
            (OPEN, DOOR, new byte[] { AT, 1, ZERO, 11 }, new byte[] { MESSAGE, 0, SET, 11, PLUS, 30, 5, PLACE, 2, 1, LET, 5, 3, DONE }),
            (SCORE, 255, Array.Empty<byte>(), new byte[] { SCOREA, DONE }),
            (WEAR, 255, new byte[] { NOTWORN, 0 }, new byte[] { AUTOW, OK }),
            (255, 255, Array.Empty<byte>(), new byte[] { LET, 28, 12, PAUSE, 0, INK, 3, INVEN }));
        int proc = b.Table(
            (255, 255, new byte[] { CARRIED, 2, GT, 11, 0 }, new byte[] { MESSAGE, 1, END }));

        b.PokeWord(h + 4, resp);
        b.PokeWord(h + 6, proc);
        b.PokeWord(h + 8, objs);
        b.PokeWord(h + 10, locs);
        b.PokeWord(h + 12, msgs);
        b.PokeWord(h + 14, sys);
        b.PokeWord(h + 16, conn);
        b.PokeWord(h + 18, vocab);
        b.PokeWord(h + 20, pos);
        b.PokeWord(h + 22, objmap);

        if (withGraphics)
        {
            // Illustrator: three pictures; 0 and 1 are location pictures, 2 is a subroutine.
            int p0 = 0xE000;
            b.Poke(p0,
                0x00, 10, 20,          // PLOT 10,20
                0x81, 30, 10,          // LINE +30,-10
                (2 << 3) | 6,          // INK 2
                0x42, 5, 3,            // FILL -5,+3
                0x22, 1, 1, 0x55,      // SHADE +1,+1 pattern 55h
                0x12, 2, 3, 4, 5,      // BLOCK h=2 w=3 x=4 y=5
                (4 << 3) | 3, 2,       // GOSUB 2 at scale 4
                (2 << 5) | 4,          // RPLOT east
                0x07);                 // END
            int p1 = 0xE100;
            b.Poke(p1, 0x18, 100, 100, 0x07); // AMOVE 100,100; END
            int p2 = 0xE200;
            b.Poke(p2, 0x01, 8, 8, 0x07);      // LINE +8,+8; END
            int ptrs = 0xF000, flags = 0xF010;
            b.PokeWord(ptrs, p0); b.PokeWord(ptrs + 2, p1); b.PokeWord(ptrs + 4, p2);
            b.Poke(flags, 0x80 | (1 << 3) | 6, 0x80 | (7 << 3), 0x00);
            b.PokeWord(0xFAB8, ptrs);
            b.PokeWord(0xFAB8 + 2, flags);
            b.PokeWord(0xFAB8 + 4, flags + 3);
            b.Poke(0xFAB8 + 6, 3);
        }
        return b.ToSna();
    }

    [Fact]
    public void Detects_later_quill_snapshot()
    {
        var data = BuildLateGame();
        var importer = new QuillImporter();
        Assert.True(importer.CanImport(data, "castle.sna"));
        var result = importer.Import(data, "the_dark_castle.sna");
        Assert.Contains("later", result.DetectedFormat);
        Assert.Contains("Illustrator", result.DetectedFormat);
        Assert.Equal("The Dark Castle", result.Adventure.Title);
    }

    [Fact]
    public void Rejects_non_quill_data()
    {
        var importer = new QuillImporter();
        var random = new byte[27 + 49152];
        new Random(42).NextBytes(random);
        Assert.False(importer.CanImport(random, "game.sna"));

        // A colour-table signature with a nonsense header must not be claimed (PAWS has a similar table).
        var b = new QuillSnapshotBuilder();
        b.Poke(0x6B85, 0x10, 7, 0x11, 0, 0x12, 0, 0x13, 0, 0x14, 0, 0x15, 0, 0, 5, 5, 5, 5, 5);
        Assert.False(importer.CanImport(b.ToSna(), "game.sna"));
        Assert.False(importer.CanImport(new byte[100], "game.sna"));
    }

    [Fact]
    public void Imports_settings_and_colours()
    {
        var adv = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure;
        Assert.True(adv.Settings.LegacyTableSemantics);
        Assert.Equal("f0", adv.Settings.DarknessVariable);
        Assert.False(adv.Settings.AutoListExits);
        Assert.Equal(4, adv.Settings.SignificantLetters);
        Assert.Equal(4, adv.Settings.MaxCarriedItems);
        Assert.Equal("r0", adv.StartRoomId);
        Assert.Equal("#D7D700", adv.Settings.TextColor);       // INK 6 (yellow)
        Assert.Equal("#0000D7", adv.Settings.BackgroundColor); // PAPER 1 (blue)
    }

    [Fact]
    public void Imports_rooms_exits_and_text()
    {
        var adv = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure;
        Assert.Equal(3, adv.Rooms.Count);
        var cellar = adv.Rooms[0];
        Assert.Equal("r0", cellar.Id);
        Assert.Equal("You are in a dark cellar. Stairs lead up.", cellar.Description);
        Assert.Equal("A dark cellar", cellar.Name);
        var up = Assert.Single(cellar.Exits);
        Assert.Equal("up", up.Direction);
        Assert.Equal("r1", up.TargetRoomId);

        // A word ending exactly at column 32 is followed by the next word without a space in the data.
        Assert.Equal("Rows of dusty bottles line walls here. I'm in the kitchen.", adv.Rooms[1].Description);
        Assert.Contains(adv.Rooms[1].Exits, e => e.Direction == "down" && e.TargetRoomId == "r0");
        Assert.Contains(adv.Rooms[1].Exits, e => e.Direction == "north" && e.TargetRoomId == "r2");
        Assert.Contains(adv.Rooms[1].Exits, e => e.Direction == "port" && e.TargetRoomId == "r0");

        // Dictionary compression and colour controls.
        Assert.Equal("the garden.", adv.Rooms[2].Description);
    }

    [Fact]
    public void Imports_items_with_articles_locations_and_nouns()
    {
        var adv = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure;
        Assert.Equal(3, adv.Items.Count);
        var lamp = adv.Items[0];
        Assert.Equal(("o0", "a", "brass lamp", "r0"), (lamp.Id, lamp.Article, lamp.Name, lamp.Location));
        Assert.Contains("lamp", lamp.Nouns);
        Assert.True(lamp.LightSource);
        Assert.True(lamp.Portable);

        var coins = adv.Items[1];
        Assert.Equal(("some", "gold coins", Locations.Carried), (coins.Article, coins.Name, coins.Location));
        Assert.Equal("coins", coins.Nouns[0]);
        Assert.Contains("coin", coins.Nouns);

        var map = adv.Items[2];
        Assert.Equal(("the", "old map", Locations.Nowhere), (map.Article, map.Name, map.Location));
        Assert.True(map.Wearable); // WEAR _ uses AUTOW, so every object can be worn
    }

    [Fact]
    public void Imports_vocabulary()
    {
        var voc = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure.Vocabulary;
        Assert.Equal(N, voc.LegacyWordNumbers["nort"]);
        Assert.Equal(N, voc.LegacyWordNumbers["n"]);
        Assert.Equal(GET, voc.LegacyWordNumbers["take"]);
        Assert.Equal("north", voc.Directions["nort"]);
        Assert.Equal("north", voc.Directions["n"]);
        Assert.Equal("up", voc.Directions["u"]);
        Assert.Equal("port", voc.Directions["port"]);

        var get = Assert.Single(voc.Verbs, v => v.Id == "get");
        Assert.Equal(new[] { "get", "take" }, get.Words);
        Assert.Empty(get.Grammar);
        Assert.Contains(voc.Verbs, v => v.Id == "open");
        Assert.Contains("lamp", voc.Nouns);
        Assert.Contains("door", voc.Nouns);
        Assert.DoesNotContain("get", voc.Nouns);
    }

    [Fact]
    public void Imports_response_table_as_before_command_triggers()
    {
        var adv = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure;
        var getLamp = adv.FindTrigger("resp0")!;
        Assert.Equal("GET LAMP", getLamp.Name);
        Assert.Equal(TriggerEvent.BeforeCommand, getLamp.Event);
        Assert.Equal("get", getLamp.Verb);
        Assert.Equal("lamp", getLamp.Noun1);
        Assert.False(getLamp.StopsCommand);
        var cond = Assert.Single(getLamp.Conditions);
        Assert.Equal((ConditionType.ItemPresent, "o0", false), (cond.Type, cond.A, cond.Negate));
        Assert.Equal(new[] { ActionType.TakeItem, ActionType.Ok }, getLamp.Actions.Select(a => a.Type));
        Assert.Equal("o0", getLamp.Actions[0].A);

        var getAny = adv.FindTrigger("resp1")!;
        Assert.Equal("*", getAny.Noun1);
        Assert.Equal((ActionType.TakeItem, "$noun1"), (getAny.Actions[0].Type, getAny.Actions[0].A));
        Assert.Equal(ActionType.Done, getAny.Actions[1].Type);

        var open = adv.FindTrigger("resp2")!;
        Assert.Equal("OPEN DOOR", open.Name);
        Assert.Equal((ConditionType.PlayerIn, "r1"), (open.Conditions[0].Type, open.Conditions[0].A));
        Assert.Equal((ConditionType.VarEquals, "f11", 0, false), (open.Conditions[1].Type, open.Conditions[1].A, open.Conditions[1].N, open.Conditions[1].Negate));
        var acts = open.Actions;
        Assert.Equal((ActionType.Message, "The door creaks open."), (acts[0].Type, acts[0].Text));
        Assert.Equal((ActionType.SetVar, "f11", 255), (acts[1].Type, acts[1].A, acts[1].N));
        Assert.Equal((ActionType.AwardScore, 5), (acts[2].Type, acts[2].N));
        Assert.Equal((ActionType.MoveItem, "o2", "r1"), (acts[3].Type, acts[3].A, acts[3].B));
        Assert.Equal((ActionType.SetVar, "f5", 3), (acts[4].Type, acts[4].A, acts[4].N));
        Assert.Equal(ActionType.Done, acts[5].Type);

        var score = adv.FindTrigger("resp3")!;
        Assert.Equal(new[] { ActionType.ShowScore, ActionType.Done }, score.Actions.Select(a => a.Type));

        var wear = adv.FindTrigger("resp4")!;
        Assert.Equal((ConditionType.ItemWorn, "o0", true), (wear.Conditions[0].Type, wear.Conditions[0].A, wear.Conditions[0].Negate));

        // LET 28 12 + PAUSE = restart; INK ignored with a warning; INVEN -> Inventory + Done.
        var special = adv.FindTrigger("resp5")!;
        Assert.Equal(("*", "*"), (special.Verb, special.Noun1));
        Assert.Equal(new[] { ActionType.SetVar, ActionType.Restart, ActionType.Inventory, ActionType.Done }, special.Actions.Select(a => a.Type));
    }

    [Fact]
    public void Imports_process_table_flags_and_timers()
    {
        var result = new QuillImporter().Import(BuildLateGame(), "x.sna");
        var adv = result.Adventure;
        var proc = adv.FindTrigger("proc0")!;
        Assert.Equal(TriggerEvent.EveryTurn, proc.Event);
        Assert.Equal(((string?)null, (string?)null), (proc.Verb, proc.Noun1)); // status-table words are labels only
        Assert.Equal((ConditionType.ItemIn, "o2", Locations.Carried), (proc.Conditions[0].Type, proc.Conditions[0].A, proc.Conditions[0].B));
        Assert.Equal((ConditionType.VarGreater, "f11", 0), (proc.Conditions[1].Type, proc.Conditions[1].A, proc.Conditions[1].N));
        Assert.Equal((ActionType.Message, "You have won!"), (proc.Actions[0].Type, proc.Actions[0].Text));
        Assert.Equal((ActionType.Lose, "END OF GAME"), (proc.Actions[1].Type, proc.Actions[1].Text));

        // Flag 5 is used, so the Quill's automatic per-turn decrement is emulated.
        var timer = adv.FindTrigger("flagtimer5")!;
        Assert.Equal(TriggerEvent.EveryTurn, timer.Event);
        Assert.Equal((ActionType.AddVar, "f5", -1), (timer.Actions[0].Type, timer.Actions[0].A, timer.Actions[0].N));

        Assert.NotNull(adv.FindVariable("f0"));
        Assert.NotNull(adv.FindVariable("f11"));
        Assert.NotNull(adv.FindVariable("f5"));
        Assert.Null(adv.FindVariable("f30")); // the score maps to AwardScore / @score
        Assert.Contains(result.Warnings, w => w.Contains("PAPER/INK/BORDER"));
    }

    [Fact]
    public void Imports_system_messages()
    {
        var adv = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure;
        Assert.Equal("It's too dark to see anything.", adv.Messages[Msg.Dark]);
        Assert.Equal("I can't go in that direction.", adv.Messages[Msg.CantGo]);
        Assert.Equal("I don't understand.", adv.Messages[Msg.DontUnderstand]);
        Assert.Equal("I can't do that.", adv.Messages[Msg.CantDoThat]);
        Assert.Equal("It's not here.", adv.Messages[Msg.NotHere]);
        Assert.Equal("I can't carry any more.", adv.Messages[Msg.TooMany]);
        Assert.Equal("You have taken {turns} turns.", adv.Messages[Msg.Turns]);
        Assert.Equal("You have scored {score}%.", adv.Messages[Msg.Score]);
        Assert.Equal("I can also see: {list}", adv.Messages[Msg.YouCanSee]);
        Assert.Equal("I can't go in that direction.", adv.Messages["sys7"]);
        Assert.Equal("N", adv.Messages["sys31"]);
    }

    [Fact]
    public void Imports_illustrator_pictures()
    {
        var adv = new QuillImporter().Import(BuildLateGame(), "x.sna").Adventure;
        Assert.Equal(3, adv.Pictures.Count);
        Assert.Equal("p0", adv.Rooms[0].PictureId);
        Assert.Equal("p1", adv.Rooms[1].PictureId);
        Assert.Null(adv.Rooms[2].PictureId);

        var p0 = adv.FindPicture("p0")!;
        Assert.Equal(PictureRenderMode.SpectrumAttributes, p0.RenderMode);
        Assert.Equal((256, 176), (p0.Width, p0.Height));
        Assert.Equal((6, 1), (p0.InitialInk, p0.InitialPaper));
        Assert.False(p0.IsSubroutine);

        var c = p0.Commands;
        Assert.Equal(new[] { DrawOp.Plot, DrawOp.Line, DrawOp.SetInk, DrawOp.Fill, DrawOp.Shade, DrawOp.AttributeBlock, DrawOp.Call, DrawOp.Plot },
            c.Select(x => x.Op));
        Assert.Equal((10, 155), (c[0].X, c[0].Y));                        // y flipped: 175 - 20
        Assert.Equal((10, 155, 40, 165), (c[1].X, c[1].Y, c[1].X2, c[1].Y2)); // relative +30,-10
        Assert.Equal(2, c[2].Color);
        Assert.Equal((35, 162), (c[3].X, c[3].Y));                        // 40-5, 10+3 -> 175-13
        Assert.Equal((36, 161), (c[4].X, c[4].Y));
        Assert.Equal(8, c[4].Pattern!.Length);
        Assert.Equal((32, 40, 55, 55, 2, 1), (c[5].X, c[5].Y, c[5].X2, c[5].Y2, c[5].Color, c[5].Color2));
        Assert.Equal(("p2", 36, 161, 4), (c[6].SubPictureId, c[6].X, c[6].Y, c[6].Scale));
        // The subroutine moved the cursor by (8,8) at scale 4/8 = (4,4); RPLOT east adds one pixel.
        Assert.Equal((41, 175 - 18), (c[7].X, c[7].Y));

        Assert.Empty(adv.FindPicture("p1")!.Commands); // an absolute move draws nothing

        var p2 = adv.FindPicture("p2")!;
        Assert.True(p2.IsSubroutine);
        var line = Assert.Single(p2.Commands);
        Assert.Equal((DrawOp.Line, 0, 0, 8, -8), (line.Op, line.X, line.Y, line.X2, line.Y2)); // relative to the call point
    }

    [Fact]
    public void Shade_pattern_expands_the_4x2_tile()
    {
        // All bits set -> solid; bit 0 only -> pixel (x%4==0, spectrum y odd) i.e. top-down even rows.
        Assert.All(IllustratorGraphicsPattern(0xFF), b => Assert.Equal(0xFF, b));
        var p = IllustratorGraphicsPattern(0x01);
        Assert.Equal(0x88, p[1]); // top-down row 1 = spectrum y 174 (even) -> bit index 0 at x%4==0
        Assert.Equal(0x00, p[0]);
    }

    private static byte[] IllustratorGraphicsPattern(int f) => BuildShadeOnly(f);

    private static byte[] BuildShadeOnly(int pattern)
    {
        var data = BuildLateGame();
        // Patch picture 1 to a single SHADE command with the requested pattern.
        var mem = new byte[65536];
        Array.Copy(data, 27, mem, 0x4000, 49152);
        new byte[] { 0x22, 0, 0, (byte)pattern, 0x07 }.CopyTo(mem, 0xE100);
        var sna = new byte[data.Length];
        Array.Copy(data, sna, 27);
        Array.Copy(mem, 0x4000, sna, 27, 49152);
        var adv = new QuillImporter().Import(sna, "x.sna").Adventure;
        return adv.FindPicture("p1")!.Commands.Single(x => x.Op == DrawOp.Shade).Pattern!;
    }

    // ------------------------------------------------------------------ early (version A) databases

    private static byte[] BuildEarlyGame()
    {
        var b = new QuillSnapshotBuilder();
        const int sig = 0x6D04;
        b.Poke(sig, 0x10, 0, 0x11, 7, 0x12, 0, 0x13, 0, 0x14, 0, 0x15, 0, 7);
        const int h = sig + 13;
        b.Poke(h, 5, 2, 2, 1);
        int locs = b.TextTable(new[] { "I am in a shed.", "I am in a field." });
        int objs = b.TextTable(new[] { "A candle", "A rusty key" });
        int msgs = b.TextTable(new[] { "Click!" });
        int vocab = b.Vocabulary(("N", 1), ("S", 2), ("GET", 20), ("UNLO", 21), ("KEY", 30), ("CAND", 31));
        int conn = b.Connections(new[] { (1, 1) }, new[] { (2, 0) });
        int pos = b.Put(0, 1);
        // Early action numbers: GOTO = 12, MESSAGE = 13, GET = 15, SET = 21, PLUS = 23, BEEP = 26, PAUSE = 11.
        int resp = b.Table(
            (21, 30, new byte[] { 8, 1 }, new byte[] { 13, 0, 21, 12, 12, 0, 1 }),
            (20, 30, Array.Empty<byte>(), new byte[] { 15, 1, 11, 50, 26, 10, 60, 5 }));
        int proc = b.Table();
        b.PokeWord(h + 4, resp);
        b.PokeWord(h + 6, proc);
        b.PokeWord(h + 8, objs);
        b.PokeWord(h + 10, locs);
        b.PokeWord(h + 12, msgs);
        b.PokeWord(h + 14, conn);
        b.PokeWord(h + 16, vocab);
        b.PokeWord(h + 18, pos);
        // System messages live in the runtime, 168 bytes after the UDGs (system variable UDG at 23675).
        const int udg = 0xF000;
        b.PokeWord(23675, udg);
        int a = udg + 168;
        for (int i = 0; i < 32; i++)
        {
            var t = QuillSnapshotBuilder.Text(i == 7 ? "I can't go that way." : $"System {i}");
            b.Poke(a, t);
            a += t.Length;
        }
        return b.ToSna();
    }

    [Fact]
    public void Imports_early_quill_database()
    {
        var importer = new QuillImporter();
        var data = BuildEarlyGame();
        Assert.True(importer.CanImport(data, "early.sna"));
        var result = importer.Import(data, "early.sna");
        Assert.Contains("early", result.DetectedFormat);
        var adv = result.Adventure;

        Assert.Equal(new[] { "A shed", "A field" }, adv.Rooms.Select(r => r.Name));
        Assert.Equal("I can't go that way.", adv.Messages[Msg.CantGo]);
        Assert.Equal("System 31", adv.Messages["sys31"]);
        Assert.Empty(adv.Pictures);

        // No object-word table in early games: the noun is guessed from the description.
        var key = adv.Items[1];
        Assert.Equal(("a", "rusty key", "r1"), (key.Article, key.Name, key.Location));
        Assert.Contains("key", key.Nouns);
        Assert.Contains("candle", adv.Items[0].Nouns);

        var unlock = adv.FindTrigger("resp0")!;
        Assert.Equal(("unlo", "key"), (unlock.Verb, unlock.Noun1));
        Assert.Equal((ConditionType.ItemIn, "o1", Locations.Carried), (unlock.Conditions[0].Type, unlock.Conditions[0].A, unlock.Conditions[0].B));
        Assert.Equal(new[] { ActionType.Message, ActionType.SetVar, ActionType.GoTo, ActionType.Look, ActionType.Done },
            unlock.Actions.Select(x => x.Type));
        Assert.Equal("Click!", unlock.Actions[0].Text);
        Assert.Equal(("f12", 255), (unlock.Actions[1].A, unlock.Actions[1].N));
        Assert.Equal("r0", unlock.Actions[2].A);

        var get = adv.FindTrigger("resp1")!;
        Assert.Equal(new[] { ActionType.TakeItem, ActionType.Pause, ActionType.Beep, ActionType.Ok }, get.Actions.Select(x => x.Type));
        Assert.Equal(1000, get.Actions[1].N); // PAUSE 50 = one second
    }
}
