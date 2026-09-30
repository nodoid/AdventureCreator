using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;
using AdventureCreator.Importers.ScottAdams;

namespace AdventureCreator.Tests.Importers;

public class ScottAdamsImporterTests
{
    /// <summary>
    /// A tiny game in the Scott Adams .dat format: a hall and a vault, a lamp (item 9 is the light source), a door
    /// opened with a key, a treasure, an automatic action and a CONTINUE chain.
    /// </summary>
    private static byte[] Game()
    {
        static int V(int verb, int noun) => verb * 150 + noun;
        static int C(int code, int arg) => arg * 20 + code;
        static int A(int a, int b) => a * 150 + b;
        var actions = new List<int[]>
        {
            // OPEN DOOR: in the hall, carrying the key → message 1, set flag 1
            new[] { V(11, 8), C(4, 1), C(1, 2), C(0, 1), 0, 0, A(1, 58), 0 },
            // GO DOOR: flag 1 set → goto room 2 and look (the look is redundant after goto)
            new[] { V(1, 8), C(8, 1), C(0, 2), 0, 0, 0, A(54, 64), 0 },
            // SCORE
            new[] { V(12, 0), 0, 0, 0, 0, 0, A(65, 0), 0 },
            // SHOUT: print message 2, then continue
            new[] { V(13, 0), 0, 0, 0, 0, 0, A(2, 73), 0 },
            // continuation: message 3
            new[] { 0, 0, 0, 0, 0, 0, A(3, 0), 0 },
            // automatic, always: in the vault → message 4
            new[] { V(0, 100), C(4, 2), 0, 0, 0, 0, A(4, 0), 0 },
        };
        var verbs = new[] { "AUT", "GO", "*ENT", "*CLI", "", "", "", "", "", "", "GET", "OPE", "SCO", "SHO", "", "", "", "", "DRO" };
        var nouns = new[] { "ANY", "NOR", "SOU", "EAS", "WES", "UP", "DOW", "LAM", "DOO", "KEY", "GOL", "", "", "", "", "", "", "", "" };
        var rooms = new (int[] Exits, string Text)[]
        {
            (new[] { 0, 0, 0, 0, 0, 0 }, ""),
            (new[] { 0, 0, 0, 0, 0, 0 }, "dusty hall"),
            (new[] { 0, 0, 0, 1, 0, 0 }, "*I'm in the vault."),
        };
        var messages = new[] { "", "The door creaks open.", "HELLO!", "...echo.", "The vault is cold." };
        var items = new List<(string, int)>();
        for (int i = 0; i < 9; i++) items.Add(($"thing {i}", 0));
        items[2] = ("Brass key/KEY/", 1);
        items[3] = ("*Gold bar*/GOL/", 2);
        items.Add(("Lamp/LAM/", 255));     // item 9: the light source, carried

        var sb = new System.Text.StringBuilder();
        void N(int v) => sb.Append(v).Append('\n');
        void S(string t) => sb.Append('"').Append(t).Append("\"\n");
        N(0); N(items.Count - 1); N(actions.Count - 1); N(verbs.Length - 1); N(rooms.Length - 1);
        N(4); N(1); N(1); N(3); N(50); N(messages.Length - 1); N(1);
        foreach (var a in actions) foreach (var v in a) N(v);
        for (int i = 0; i < verbs.Length; i++) { S(verbs[i]); S(nouns[i]); }
        foreach (var (e, t) in rooms) { foreach (var v in e) N(v); S(t); }
        foreach (var m in messages) S(m);
        foreach (var (t, l) in items) { S(t); N(l); }
        foreach (var _ in actions) S("");
        return System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact]
    public void DetectsAndConvertsTheGame()
    {
        var data = Game();
        var importer = ImporterRegistry.Detect(data, "tiny.dat");
        Assert.IsType<ScottAdamsImporter>(importer);
        var a = importer!.Import(data, "tiny.dat").Adventure;
        Assert.Equal(2, a.Rooms.Count);
        Assert.Equal("r1", a.StartRoomId);
        Assert.Equal("I'm in a dusty hall", a.FindRoom("r1")!.Description);
        Assert.Equal("I'm in the vault.", a.FindRoom("r2")!.Description);
        Assert.Contains("key", a.FindItem("o2")!.Nouns);
        Assert.True(a.FindItem("o9")!.LightSource);
        Assert.Equal(Locations.Carried, a.FindItem("o9")!.Location);
        Assert.Equal(3, a.Settings.SignificantLetters);
        Assert.Equal("go|ent|cli", a.Triggers.First(t => t.Id == "a1").Verb);
    }

    [Fact]
    public void ThePuzzleCanBeSolved()
    {
        var a = new ScottAdamsImporter().Import(Game(), "tiny.dat").Adventure;
        var e = new GameEngine(a, randomSeed: 1);
        e.Start();
        Assert.Contains("O.K.", e.Submit("get key").Text);
        Assert.Contains("The door creaks open.", e.Submit("open door").Text);
        var enter = e.Submit("climb door").Text;             // CLIMB is a synonym of GO
        Assert.Equal("r2", e.State.CurrentRoomId);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(enter, "I'm in the vault"));   // described once
        Assert.Contains("The vault is cold.", enter);         // the automatic action
        var shout = e.Submit("shout").Text;
        Assert.Contains("HELLO!", shout);
        Assert.Contains("...echo.", shout);                   // CONTINUE ran the next entry
        e.Submit("get gold");
        e.Submit("w");
        e.Submit("drop gold");
        var score = e.Submit("score");
        Assert.Contains("stored 1 treasures out of 1", score.Text);
        Assert.True(score.Won);
    }
}
