using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Paws;

namespace AdventureCreator.Tests.Importers;

/// <summary>
/// Builds a small PAWS database laid out exactly as the Spectrum PAWS interpreter stores it (48K),
/// wraps it in a .SNA snapshot and imports it.
/// </summary>
public class PawsImporterTests
{
    // ------------------------------------------------------------------ builder

    /// <summary>Writes a PAWS database into a 64K memory image and produces a 48K .SNA.</summary>
    internal sealed class PawsDatabaseBuilder
    {
        public const int MainTop = 0x9300;
        private readonly byte[] mem = new byte[65536];
        private int here = 0x9600;
        private readonly Dictionary<int, string> tokens = new() { [165] = " the ", [166] = "ing", [167] = "You " };

        public List<(string Word, int Number, int Type)> Vocabulary { get; } = new();
        public List<string> Locations { get; } = new();
        public List<string> Messages { get; } = new();
        public List<string> SysMessages { get; } = new();
        public List<List<(int Word, int Dest)>> Connections { get; } = new();
        public List<(string Text, int At, int Noun, int Adjective, int Attributes)> Objects { get; } = new();
        public List<List<(int Verb, int Noun, byte[] Condacts)>> Processes { get; } = new();
        public List<(int Attribute, byte[] Draw)> Pictures { get; } = new();
        public bool Compress { get; set; } = true;

        private int Alloc(byte[] data)
        {
            int at = here;
            data.CopyTo(mem, at);
            here += data.Length;
            if (here >= 65497) throw new InvalidOperationException("Test database too large.");
            return at;
        }

        private void Word(int address, int value)
        {
            mem[address] = (byte)value;
            mem[address + 1] = (byte)(value >> 8);
        }

        public byte[] EncodeText(string text)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < text.Length;)
            {
                var token = Compress ? tokens.FirstOrDefault(t => string.CompareOrdinal(text, i, t.Value, 0, t.Value.Length) == 0) : default;
                if (token.Value != null)
                {
                    bytes.Add((byte)(token.Key ^ 0xFF));
                    i += token.Value.Length;
                    continue;
                }
                int c = text[i++] == '\n' ? 7 : text[i - 1];
                bytes.Add((byte)(c ^ 0xFF));
            }
            bytes.Add(31 ^ 0xFF);
            return bytes.ToArray();
        }

        private int TextTable(IReadOnlyList<string> texts)
        {
            var addrs = texts.Select(t => Alloc(EncodeText(t))).ToList();
            int table = Alloc(new byte[2 * Math.Max(1, texts.Count)]);
            for (int i = 0; i < addrs.Count; i++) Word(table + 2 * i, addrs[i]);
            return table;
        }

        public byte[] BuildSna()
        {
            // UDGs, shades and the miscellaneous block at MainTop.
            for (int i = 0; i < 8; i++)
            {
                mem[MainTop + 152 + 8 * 1 + i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);  // shade 1: checkerboard
                mem[MainTop + 152 + 8 * 2 + i] = 0x01;                              // shade 2: vertical line
            }
            mem[MainTop + 281] = 0;
            mem[MainTop + 283] = 0; mem[MainTop + 284] = 255; mem[MainTop + 285] = 255;
            mem[MainTop + 297] = 0; mem[MainTop + 298] = 255; mem[MainTop + 299] = 255;
            byte[] colours = { 16, 4, 17, 0, 18, 0, 19, 0, 20, 0, 21, 0, 0 };
            colours.CopyTo(mem, MainTop + 311);
            mem[MainTop + 324] = (byte)Objects.Count;
            mem[MainTop + 325] = (byte)Locations.Count;
            mem[MainTop + 326] = (byte)Messages.Count;
            mem[MainTop + 327] = (byte)SysMessages.Count;
            mem[MainTop + 328] = (byte)Processes.Count;
            mem[MainTop + 329] = 0;

            // Compression dictionary: a 0 byte marks a compressed database; token 164 is a dummy.
            var dict = new List<byte> { 0, 0x80 };
            for (int t = 165; t <= 255; t++)
            {
                var s = tokens.TryGetValue(t, out var v) ? v : "?";
                for (int k = 0; k < s.Length; k++) dict.Add((byte)(s[k] | (k == s.Length - 1 ? 0x80 : 0)));
            }
            int dictAddr = Alloc(Compress ? dict.ToArray() : new byte[] { 0xFF });
            Word(MainTop + 332, dictAddr);

            // Process tables.
            var tableAddrs = new List<int>();
            foreach (var table in Processes)
            {
                var entryData = table.Select(e => Alloc(e.Condacts.Concat(new byte[] { 255 }).ToArray())).ToList();
                int t = Alloc(new byte[4 * table.Count + 1]);
                for (int i = 0; i < table.Count; i++)
                {
                    mem[t + 4 * i] = (byte)table[i].Verb;
                    mem[t + 4 * i + 1] = (byte)table[i].Noun;
                    Word(t + 4 * i + 2, entryData[i]);
                }
                tableAddrs.Add(t);
            }
            int procPtrs = Alloc(new byte[2 * Processes.Count]);
            for (int i = 0; i < tableAddrs.Count; i++) Word(procPtrs + 2 * i, tableAddrs[i]);
            Word(65497, procPtrs);

            Word(65499, TextTable(Objects.Select(o => o.Text).ToList()));
            Word(65501, TextTable(Locations));
            Word(65503, TextTable(Messages));
            Word(65505, TextTable(SysMessages));

            var conAddrs = Connections.Select(c => Alloc(c.SelectMany(x => new[] { (byte)x.Word, (byte)x.Dest }).Concat(new byte[] { 255 }).ToArray())).ToList();
            int conTable = Alloc(new byte[2 * Locations.Count]);
            for (int i = 0; i < conAddrs.Count; i++) Word(conTable + 2 * i, conAddrs[i]);
            Word(65507, conTable);

            var voc = new List<byte>();
            foreach (var (w, n, type) in Vocabulary)
            {
                var padded = w.ToUpperInvariant().PadRight(5)[..5];
                voc.AddRange(padded.Select(ch => (byte)(ch ^ 0xFF)));
                voc.Add((byte)n);
                voc.Add((byte)type);
            }
            voc.Add(0);
            Word(65509, Alloc(voc.ToArray()));

            Word(65511, Alloc(Objects.Select(o => (byte)o.At).ToArray()));
            Word(65513, Alloc(Objects.SelectMany(o => new[] { (byte)o.Noun, (byte)o.Adjective }).ToArray()));
            Word(65515, Alloc(Objects.Select(o => (byte)o.Attributes).ToArray()));

            var picAddrs = Pictures.Select(p => Alloc(p.Draw)).ToList();
            int picTable = Alloc(new byte[2 * Locations.Count]);
            for (int i = 0; i < picAddrs.Count; i++) Word(picTable + 2 * i, picAddrs[i]);
            Word(65521, picTable);
            Word(65523, Alloc(Pictures.Select(p => (byte)p.Attribute).ToArray()));
            mem[65527] = (byte)'2';
            Word(65533, MainTop);

            var sna = new byte[27 + 49152];
            Array.Copy(mem, 0x4000, sna, 27, 49152);
            return sna;
        }
    }

    // Word values
    private const int VNorth = 2, VSouth = 3, VGet = 20, VOpen = 22, VLook = 23;
    private const int NLamp = 50, NBox = 51, NKey = 52, NCloak = 53, NSand = 54;
    private const int ABrass = 60, AdvQuiet = 70, PrepIn = 80;
    private const byte Any = 1, None = 255;

    private static byte[] C(params int[] bytes) => bytes.Select(b => (byte)b).ToArray();
    private static int Op(string name) => PawsCondactOpcodes.Of(name);

    private static PawsDatabaseBuilder SampleGame()
    {
        var b = new PawsDatabaseBuilder();
        b.Vocabulary.AddRange(new[]
        {
            ("N", VNorth, 0), ("NORTH", VNorth, 0), ("S", VSouth, 0), ("SOUTH", VSouth, 0),
            ("GET", VGet, 0), ("TAKE", VGet, 0), ("OPEN", VOpen, 0), ("LOOK", VLook, 0), ("L", VLook, 0),
            ("LAMP", NLamp, 2), ("LANTERN", NLamp, 2), ("BOX", NBox, 2), ("KEY", NKey, 2), ("CLOAK", NCloak, 2), ("SAND", NSand, 2),
            ("BRASS", ABrass, 3), ("QUIETLY", AdvQuiet, 1), ("IN", PrepIn, 4), ("IT", 90, 6), ("AND", 91, 5),
        });
        b.Locations.AddRange(new[]
        {
            "You are in the hall. A dusty place with a staircase.",
            "GARDEN\nFlowers grow everywhere.",
            "",
        });
        b.Connections.Add(new() { (VNorth, 1) });
        b.Connections.Add(new() { (VSouth, 0) });
        b.Connections.Add(new());

        b.Objects.Add(("A brass lamp", 254, NLamp, ABrass, 2));
        b.Objects.Add(("A small key. Rather rusty.", 2, NKey, None, 1));
        b.Objects.Add(("A wooden box", 0, NBox, None, 0x40 | 5));
        b.Objects.Add(("A cloak", 253, NCloak, None, 0x80 | 3));
        b.Objects.Add(("Some sand", 252, None, None, 1));

        b.Messages.AddRange(new[] { "You already hold the lamp.", "The box creaks open.", "Something glints.", "Score: " });
        for (int i = 0; i < 54; i++)
            b.SysMessages.Add(i switch
            {
                0 => "It is too dark to see.",
                6 => "I don't understand.",
                7 => "You can't go that way.",
                8 => "I can't do that.",
                9 => "I have with me: ",
                11 => "nothing at all.",
                15 => "OK.",
                17 => "You have taken ", 18 => " turn", 19 => "s", 20 => ".",
                21 => "You have scored ", 22 => "%.",
                25 => "You already have the _.",
                36 => "You now have the _.",
                _ => $"System {i}",
            });

        // Response
        b.Processes.Add(new()
        {
            (VGet, NLamp, C(Op("AT"), 0, Op("CARRIED"), 0, Op("MESSAGE"), 0, Op("DONE"))),
            (VOpen, NBox, C(Op("PRESENT"), 2, Op("ZERO"), 11, Op("SET"), 11, Op("MESSAGE"), 1,
                            Op("NOTZERO"), 12, Op("MESSAGE"), 2, Op("DONE"))),
            (None, None, C(Op("ADVERB"), AdvQuiet, Op("PREP"), PrepIn, Op("NOUN2"), None, Op("PROCESS"), 3)),
            (VLook, None, C(Op("DESC"))),
            (VGet, Any, C(Op("ADJECT1"), ABrass, Op("AUTOG"), Op("DONE"))),
            (VNorth, None, C(Op("AT"), 0, Op("ISAT"), 1, 2, Op("SYSMESS"), 7, Op("DONE"))),
            (None, None, C(Op("TIMEOUT"), Op("MODE"), 4, 0, Op("MESSAGE"), 2)),
        });
        // Process 1
        b.Processes.Add(new()
        {
            (None, None, C(Op("LISTOBJ"))),
            (None, None, C(Op("AT"), 1, Op("MES"), 3, Op("PRINT"), 30, Op("NEWLINE"))),
        });
        // Process 2
        b.Processes.Add(new()
        {
            (None, None, C(Op("NOTZERO"), 5, Op("MINUS"), 5, 1)),
            (None, None, C(Op("EQ"), 38, 1, Op("PLUS"), 30, 10, Op("COPYFF"), 30, 100)),
        });
        // Process 3 (sub-process called from Response)
        b.Processes.Add(new()
        {
            (Any, Any, C(Op("ABILITY"), 6, 20, Op("PLACE"), 1, 255, Op("GOTO"), 1, Op("OK"))),
        });

        // Pictures: 0 = location picture, 1 = subroutine, 2 = empty.
        b.Pictures.Add((0x80 | (1 << 3) | 6, C(
            0x00, 10, 20,          // PLOT 10,20
            0x01, 50, 0,           // LINE +50,+0
            0x81, 0, 10,           // LINE +0,-10
            0x02, 5, 5,            // FILL at +5,+5 (pen unchanged)
            0x62, 3, 2, 0x21,      // SHADE at -3,+2 with patterns 1|2
            0x12, 1, 2, 3, 4,      // BLOCK h=1 w=2 col=3 row=4
            0x16,                  // INK 2
            0x23, 1,               // GOSUB sc=4 picture 1
            0x01, 10, 0,           // LINE +10,+0 (continues from the pen left by the subroutine)
            0x04, 65, 5, 2,        // TEXT 'A' at column 5 row 2
            0x18, 0, 0,            // PLOT with OVER+INVERSE = absolute move
            0x08, 1, 1,            // PLOT OVER at 1,1
            0x07)));
        b.Pictures.Add((0x07, C(0x01, 8, 8, 0x81, 8, 8, 0x07)));
        b.Pictures.Add((0x80, C(0x07)));
        return b;
    }

    private static (Adventure Adventure, AdventureCreator.Importers.Common.ImportResult Result) Import(PawsDatabaseBuilder b)
    {
        var importer = new PawsImporter();
        var sna = b.BuildSna();
        Assert.True(importer.CanImport(sna, "sample.sna"));
        var result = importer.Import(sna, "sample.sna");
        return (result.Adventure, result);
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public void DetectsPawsAndRejectsOtherSnapshots()
    {
        var importer = new PawsImporter();
        Assert.True(importer.CanImport(SampleGame().BuildSna(), "game.SNA"));
        Assert.False(importer.CanImport(new byte[27 + 49152], "empty.sna"));
        Assert.False(importer.CanImport(SampleGame().BuildSna(), "game.gac"));
        Assert.False(importer.CanImport(new byte[100], "short.z80"));
    }

    [Fact]
    public void ImportsSettingsAndFormat()
    {
        var (adv, result) = Import(SampleGame());
        Assert.StartsWith("PAWS (ZX Spectrum 48K, compressed", result.DetectedFormat);
        Assert.True(adv.Settings.LegacyTableSemantics);
        Assert.Equal("f0", adv.Settings.DarknessVariable);
        Assert.Equal(5, adv.Settings.SignificantLetters);
        Assert.False(adv.Settings.AutoListExits);
        Assert.True(adv.Settings.AutoListItems);          // the game uses LISTOBJ
        Assert.Equal(6, adv.Settings.MaxCarriedItems);    // from ABILITY 6 20
        Assert.Equal(20, adv.Settings.MaxCarriedWeight);
        Assert.Equal("r0", adv.StartRoomId);
        Assert.Contains(adv.Variables, v => v.Name == "f37" && v.InitialValue == 4);
        Assert.Contains(adv.Variables, v => v.Name == "f0" && v.Description.Contains("Darkness"));
        Assert.Contains(result.Warnings, w => w.Contains("MODE"));
    }

    [Fact]
    public void ImportsRoomsWithCompressedTextAndExits()
    {
        var (adv, _) = Import(SampleGame());
        Assert.Equal(3, adv.Rooms.Count);
        Assert.Equal("You are in the hall. A dusty place with a staircase.", adv.Rooms[0].Description);
        Assert.Equal("You are in the hall", adv.Rooms[0].Name);
        Assert.Equal("Garden", adv.Rooms[1].Name);
        Assert.Equal("GARDEN\nFlowers grow everywhere.", adv.Rooms[1].Description);
        var exit = Assert.Single(adv.Rooms[0].Exits);
        Assert.Equal("north", exit.Direction);
        Assert.Equal("r1", exit.TargetRoomId);
        Assert.Equal("south", adv.Rooms[1].Exits[0].Direction);
        Assert.Equal("north", adv.Vocabulary.Directions["n"]);
        Assert.Equal("south", adv.Vocabulary.Directions["south"]);
    }

    [Fact]
    public void ImportsItemsWithWordsWeightsAndLocations()
    {
        var (adv, _) = Import(SampleGame());
        Assert.Equal(5, adv.Items.Count);
        var lamp = adv.Items[0];
        Assert.Equal(("a", "brass lamp"), (lamp.Article, lamp.Name));
        Assert.Equal(new[] { "lamp", "lante" }, lamp.Nouns);
        Assert.Equal(new[] { "brass" }, lamp.Adjectives);
        Assert.Equal(2, lamp.Weight);
        Assert.Equal(Locations.Carried, lamp.Location);
        Assert.True(lamp.LightSource);

        var key = adv.Items[1];
        Assert.Equal("small key", key.Name);
        Assert.Equal("o2", key.Location);                 // inside the box (container object 2)
        Assert.Contains("Rather rusty", key.Description);

        var box = adv.Items[2];
        Assert.True(box.Container);
        Assert.Equal(5, box.Weight);
        Assert.Equal("r0", box.Location);

        var cloak = adv.Items[3];
        Assert.True(cloak.Wearable);
        Assert.Equal(Locations.Worn, cloak.Location);

        var sand = adv.Items[4];
        Assert.Equal(("some", "sand"), (sand.Article, sand.Name));
        Assert.Equal(Locations.Nowhere, sand.Location);
        Assert.Equal(new[] { "sand" }, sand.Nouns);       // guessed from the name
    }

    [Fact]
    public void ImportsVocabularyByWordType()
    {
        var (adv, _) = Import(SampleGame());
        var voc = adv.Vocabulary;
        var get = Assert.Single(voc.Verbs, v => v.Id == "get");
        Assert.Equal(new[] { "get", "take" }, get.Words);
        Assert.Empty(get.Grammar);
        Assert.DoesNotContain(voc.Verbs, v => v.Id == "n");   // movement verbs become directions
        Assert.Contains("lamp", voc.Nouns);
        Assert.Contains("quiet", voc.Adverbs);
        Assert.Contains("brass", voc.Adjectives);
        Assert.Contains("in", voc.Prepositions);
        Assert.Equal(VGet, voc.LegacyWordNumbers["take"]);
        Assert.Equal(NLamp, voc.LegacyWordNumbers["lante"]);
        Assert.Equal(90, voc.LegacyWordNumbers["it"]);
    }

    [Fact]
    public void ImportsSystemMessages()
    {
        var (adv, _) = Import(SampleGame());
        Assert.Equal("You can't go that way.", adv.Messages[Msg.CantGo]);
        Assert.Equal("I can't do that.", adv.Messages[Msg.CantDoThat]);
        Assert.Equal("It is too dark to see.", adv.Messages[Msg.Dark]);
        Assert.Equal("You already have the {noun1}.", adv.Messages[Msg.AlreadyCarrying]);
        Assert.Equal("You have taken {turns} turns.", adv.Messages[Msg.Turns]);
        Assert.Equal("You have scored {score}%.", adv.Messages[Msg.Score]);
        Assert.Equal("I have with me: {list}", adv.Messages[Msg.Carrying]);
        Assert.Equal("System 40", adv.Messages["sys40"]);
        Assert.Equal("OK.", adv.Messages["sys15"]);
    }

    [Fact]
    public void ImportsResponseTableAsBeforeCommandTriggers()
    {
        var (adv, _) = Import(SampleGame());
        var t = adv.FindTrigger("t0_0")!;
        Assert.Equal(TriggerEvent.BeforeCommand, t.Event);
        Assert.False(t.StopsCommand);
        Assert.Equal(("get", "lamp"), (t.Verb, t.Noun1));
        Assert.Equal(ConditionType.PlayerIn, t.Conditions[0].Type);
        Assert.Equal("r0", t.Conditions[0].A);
        Assert.Equal((ConditionType.ItemIn, "o0", Locations.Carried), (t.Conditions[1].Type, t.Conditions[1].A, t.Conditions[1].B));
        Assert.Equal("You already hold the lamp.", t.Actions[0].Text);
        Assert.Equal(ActionType.Done, t.Actions[1].Type);

        // Condition after an action: split into chained triggers.
        var open1 = adv.FindTrigger("t0_1")!;
        var open2 = adv.FindTrigger("t0_1_1")!;
        Assert.Equal(new[] { ActionType.SetVar, ActionType.Message, ActionType.CopyVar }, open1.Actions.Select(a => a.Type));
        Assert.Equal(("f11", 255), (open1.Actions[0].A, open1.Actions[0].N));
        Assert.Equal(ConditionType.VarEqualsVar, open2.Conditions[0].Type);
        Assert.Equal((ConditionType.VarEquals, "f12", true), (open2.Conditions[1].Type, open2.Conditions[1].A, open2.Conditions[1].Negate));
        Assert.Equal("Something glints.", open2.Actions[1].Text);
        Assert.Contains(adv.Variables, v => v.Name == "chain_t0_1");

        var any = adv.FindTrigger("t0_2")!;
        Assert.Equal(("*", "*"), (any.Verb, any.Noun1));
        Assert.Equal((ConditionType.AdverbUsed, "quiet"), (any.Conditions[0].Type, any.Conditions[0].A));
        Assert.Equal((ConditionType.PrepositionIs, "in"), (any.Conditions[1].Type, any.Conditions[1].A));
        Assert.Equal((ConditionType.Noun2Is, "-"), (any.Conditions[2].Type, any.Conditions[2].A));
        Assert.Equal((ActionType.RunTrigger, "proc3"), (any.Actions[0].Type, any.Actions[0].A));

        var look = adv.FindTrigger("t0_3")!;
        Assert.Equal(("look", "*"), (look.Verb, look.Noun1));
        Assert.Equal(new[] { ActionType.Look, ActionType.Done }, look.Actions.Select(a => a.Type));

        var autog = adv.FindTrigger("t0_4")!;
        Assert.Equal((ConditionType.AdjectiveUsed, "brass"), (autog.Conditions[0].Type, autog.Conditions[0].A));
        Assert.Equal((ActionType.TakeItem, "$noun1"), (autog.Actions[0].Type, autog.Actions[0].A));

        var north = adv.FindTrigger("t0_5")!;
        Assert.Equal("north", north.Verb);
        Assert.Equal((ConditionType.ItemIn, "o1", "o2"), (north.Conditions[1].Type, north.Conditions[1].A, north.Conditions[1].B));
        Assert.Equal(("You can't go that way.", 1), (north.Actions[0].Text, north.Actions[0].N));

        var timeout = adv.FindTrigger("t0_6")!;
        Assert.Contains(timeout.Conditions, c => c.Type == ConditionType.Always && c.Negate);
    }

    [Fact]
    public void ImportsProcessTablesAndSubroutines()
    {
        var (adv, _) = Import(SampleGame());
        var ids = adv.Triggers.Select(t => t.Id).ToList();
        // LISTOBJ-only entry performs no action and is dropped.
        Assert.Null(adv.FindTrigger("t1_0"));
        var p1 = adv.FindTrigger("t1_1")!;
        Assert.Equal(TriggerEvent.AfterDescribe, p1.Event);
        Assert.StartsWith("PRO1", p1.Name);
        Assert.Null(p1.Verb);
        Assert.Equal(("Score: ", 1), (p1.Actions[0].Text, p1.Actions[0].N));
        Assert.Equal("{var:@score}", p1.Actions[1].Text);
        Assert.Equal("", p1.Actions[2].Text);

        var p2 = adv.FindTrigger("t2_0")!;
        Assert.StartsWith("PRO2", p2.Name);
        Assert.Equal((ActionType.AddVar, "f5", -1), (p2.Actions[0].Type, p2.Actions[0].A, p2.Actions[0].N));
        Assert.True(ids.IndexOf("t1_1") < ids.IndexOf("t2_0"));

        var score = adv.FindTrigger("t2_1")!;
        Assert.Equal((ConditionType.VarEquals, "@room", 1), (score.Conditions[0].Type, score.Conditions[0].A, score.Conditions[0].N));
        Assert.Equal((ActionType.AddVar, "@score", 10), (score.Actions[0].Type, score.Actions[0].A, score.Actions[0].N));
        Assert.Equal((ActionType.CopyVar, "f100", "@score"), (score.Actions[1].Type, score.Actions[1].A, score.Actions[1].B));

        var sub = adv.FindTrigger("t3_0")!;
        Assert.Equal(TriggerEvent.Subroutine, sub.Event);
        Assert.Equal("proc3", sub.Name);
        Assert.Equal(("*", "*"), (sub.Verb, sub.Noun1));   // called only from Response
        Assert.Equal((ActionType.MoveItem, "o1", Locations.Here), (sub.Actions[2].Type, sub.Actions[2].A, sub.Actions[2].B));
        Assert.Equal((ActionType.GoTo, "r1"), (sub.Actions[3].Type, sub.Actions[3].A));
        Assert.Equal(ActionType.Ok, sub.Actions[4].Type);
    }

    [Fact]
    public void ImportsPicturesWithLinesFillsAndGosub()
    {
        var (adv, _) = Import(SampleGame());
        Assert.Equal(2, adv.Pictures.Count);                // picture 2 is empty
        Assert.Equal("p0", adv.Rooms[0].PictureId);
        Assert.Null(adv.Rooms[2].PictureId);

        var p0 = adv.FindPicture("p0")!;
        Assert.Equal(PictureRenderMode.SpectrumAttributes, p0.RenderMode);
        Assert.Equal((256, 176, 6, 1), (p0.Width, p0.Height, p0.InitialInk, p0.InitialPaper));
        Assert.False(p0.IsSubroutine);
        var c = p0.Commands;
        Assert.Equal((DrawOp.Plot, 10, 155), (c[0].Op, c[0].X, c[0].Y));
        Assert.Equal((DrawOp.Line, 10, 155, 60, 155), (c[1].Op, c[1].X, c[1].Y, c[1].X2, c[1].Y2));
        Assert.Equal((DrawOp.Line, 60, 155, 60, 165), (c[2].Op, c[2].X, c[2].Y, c[2].X2, c[2].Y2));
        Assert.Equal((DrawOp.Fill, 65, 160), (c[3].Op, c[3].X, c[3].Y));
        Assert.Equal((DrawOp.Shade, 57, 163), (c[4].Op, c[4].X, c[4].Y));
        Assert.Equal(new byte[] { 0xAB, 0x55, 0xAB, 0x55, 0xAB, 0x55, 0xAB, 0x55 }, c[4].Pattern);
        Assert.Equal((DrawOp.AttributeBlock, 24, 32, 47, 47, 6, 1), (c[5].Op, c[5].X, c[5].Y, c[5].X2, c[5].Y2, c[5].Color, c[5].Color2));
        Assert.Equal((DrawOp.SetInk, 2), (c[6].Op, c[6].Color));
        Assert.Equal((DrawOp.Call, "p1", 4, 60), (c[7].Op, c[7].SubPictureId, c[7].Scale, c[7].X));
        Assert.Equal(165 - 88, c[7].Y);                     // pen y minus 175 * 4/8
        // Pen after the subroutine: +8*4/8 +8*4/8 in x, net 0 in y.
        Assert.Equal((DrawOp.Line, 68, 165, 78, 165), (c[8].Op, c[8].X, c[8].Y, c[8].X2, c[8].Y2));
        Assert.Equal((DrawOp.Text, "A", 40, 16), (c[9].Op, c[9].Text, c[9].X, c[9].Y));
        Assert.Equal((DrawOp.Plot, 1, 174, true), (c[10].Op, c[10].X, c[10].Y, c[10].Xor));
        Assert.Equal(11, c.Count);

        var p1 = adv.FindPicture("p1")!;
        Assert.True(p1.IsSubroutine);
        Assert.Equal((DrawOp.Line, 0, 175, 8, 167), (p1.Commands[0].Op, p1.Commands[0].X, p1.Commands[0].Y, p1.Commands[0].X2, p1.Commands[0].Y2));
        Assert.Equal((DrawOp.Line, 8, 167, 16, 175), (p1.Commands[1].Op, p1.Commands[1].X, p1.Commands[1].Y, p1.Commands[1].X2, p1.Commands[1].Y2));
    }

    [Fact]
    public void ImportsUncompressedDatabase()
    {
        var b = SampleGame();
        b.Compress = false;
        var (adv, result) = Import(b);
        Assert.Contains("uncompressed", result.DetectedFormat);
        Assert.Equal("You are in the hall. A dusty place with a staircase.", adv.Rooms[0].Description);
    }
}

/// <summary>Opcode lookup for building condact lists in tests.</summary>
internal static class PawsCondactOpcodes
{
    private static readonly string[] Names =
    {
        "AT", "NOTAT", "ATGT", "ATLT", "PRESENT", "ABSENT", "WORN", "NOTWORN", "CARRIED", "NOTCARR", "CHANCE", "ZERO",
        "NOTZERO", "EQ", "GT", "LT", "ADJECT1", "ADVERB", "INVEN", "DESC", "QUIT", "END", "DONE", "OK", "ANYKEY", "SAVE",
        "LOAD", "TURNS", "SCORE", "CLS", "DROPALL", "AUTOG", "AUTOD", "AUTOW", "AUTOR", "PAUSE", "TIMEOUT", "GOTO",
        "MESSAGE", "REMOVE", "GET", "DROP", "WEAR", "DESTROY", "CREATE", "SWAP", "PLACE", "SET", "CLEAR", "PLUS", "MINUS",
        "LET", "NEWLINE", "PRINT", "SYSMESS", "ISAT", "COPYOF", "COPYOO", "COPYFO", "COPYFF", "LISTOBJ", "EXTERN",
        "RAMSAVE", "RAMLOAD", "BEEP", "PAPER", "INK", "BORDER", "PREP", "NOUN2", "ADJECT2", "ADD", "SUB", "PARSE",
        "LISTAT", "PROCESS", "SAME", "MES", "CHARSET", "NOTEQ", "NOTSAME", "MODE", "LINE", "TIME", "PICTURE", "DOALL",
        "PROMPT", "GRAPHIC", "ISNOTAT", "WEIGH", "PUTIN", "TAKEOUT", "NEWTEXT", "ABILITY", "WEIGHT", "RANDOM", "INPUT",
        "SAVEAT", "BACKAT", "PRINTAT", "WHATO", "RESET", "PUTO", "NOTDONE", "AUTOP", "AUTOT", "MOVE", "PROTECT",
    };

    public static int Of(string name)
    {
        int i = Array.IndexOf(Names, name);
        if (i < 0) throw new ArgumentException(name);
        return i;
    }
}
