using AdventureSystem.Core.Model;
using AdventureSystem.Core.Parsing;

namespace AdventureSystem.Tests.Parsing;

public class ParserTests
{
    private static Adventure World()
    {
        var a = new Adventure { StartRoomId = "r" };
        a.Rooms.Add(new Room { Id = "r", Name = "Room" });
        a.Items.Add(new Item { Id = "redkey", Name = "red key", Nouns = { "key" }, Adjectives = { "red", "small" }, Location = "r" });
        a.Items.Add(new Item { Id = "bluekey", Name = "blue key", Nouns = { "key" }, Adjectives = { "blue" }, Location = "r" });
        a.Items.Add(new Item { Id = "box", Name = "wooden box", Nouns = { "box" }, Adjectives = { "wooden" }, Container = true, Location = "r" });
        a.Items.Add(new Item { Id = "panel", Name = "control panel", Nouns = { "control panel", "panel" }, Adjectives = { "metal" }, Location = "r" });
        a.Items.Add(new Item { Id = "robot", Name = "robot", Nouns = { "robot" }, IsCharacter = true, Location = "r" });
        a.Items.Add(new Item { Id = "coin", Name = "gold coin", Nouns = { "coin" }, Adjectives = { "gold" }, Location = "r" });
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "polish", Words = { "polish", "buff up" }, Grammar = { "* {noun}", "* {noun} with {held2}" } });
        return a;
    }

    private static Parser P(Adventure? a = null) => new(new Lexicon(a ?? World()));

    private static ParsedCommand One(string input, Adventure? a = null)
    {
        var o = P(a).Parse(input);
        Assert.True(o.Success, o.Error + " " + o.UnknownWord);
        Assert.Single(o.Commands);
        return o.Commands[0];
    }

    [Theory]
    [InlineData("n", "north")]
    [InlineData("go north", "north")]
    [InlineData("walk south-west", "southwest")]
    [InlineData("head up", "up")]
    [InlineData("in", "in")]
    public void Directions(string input, string dir)
    {
        var c = One(input);
        Assert.Equal("go", c.ActionId);
        Assert.Equal(dir, c.Direction);
    }

    [Fact]
    public void AdjectivesAndArticles()
    {
        var c = One("take the small red key");
        Assert.Equal("take", c.ActionId);
        Assert.Equal("key", c.Object1!.Noun);
        Assert.Equal(new[] { "small", "red" }, c.Object1.Adjectives);
    }

    [Fact]
    public void PhrasalVerb()
    {
        var c = One("pick up the coin");
        Assert.Equal("take", c.ActionId);
        Assert.Equal("coin", c.Object1!.Noun);
        Assert.Equal("pick up", c.VerbWords);
    }

    [Fact]
    public void SplitPhrasalVerb()
    {
        var c = One("pick the coin up");
        Assert.Equal("take", c.ActionId);
        Assert.Equal("coin", c.Object1!.Noun);
    }

    [Fact]
    public void IndirectObjectWithPreposition()
    {
        var c = One("put the gold coin into the wooden box");
        Assert.Equal("insert", c.ActionId);
        Assert.Equal("coin", c.Object1!.Noun);
        Assert.Equal("box", c.Object2!.Noun);
        Assert.Equal("into", c.Preposition);
    }

    [Fact]
    public void GrammarRedirect_PutOnMeansWear()
    {
        Assert.Equal("wear", One("put on the coin").ActionId);
        Assert.Equal("puton", One("put coin on box").ActionId);
    }

    [Theory]
    [InlineData("quietly open the box", "quietly")]
    [InlineData("open the box quietly", "quietly")]
    [InlineData("open the box very carefully", "carefully")]
    [InlineData("glumly open box", "glumly")]
    public void Adverbs(string input, string adverb)
    {
        var c = One(input);
        Assert.Equal("open", c.ActionId);
        Assert.Contains(adverb, c.Adverbs);
        Assert.Equal("box", c.Object1!.Noun);
    }

    [Fact]
    public void AdjectiveThatIsAlsoAdverbStaysAdjective()
    {
        var a = World();
        a.Items.Add(new Item { Id = "hat", Name = "hard hat", Nouns = { "hat" }, Adjectives = { "hard" }, Location = "r" });
        var c = One("wear the hard hat", a);
        Assert.Empty(c.Adverbs);
        Assert.Contains("hard", c.Object1!.Adjectives);
    }

    [Fact]
    public void MultipleObjectsAndExcept()
    {
        var c = One("take all except the blue key and the coin");
        Assert.True(c.Object1!.All);
        Assert.Equal(2, c.Object1.Except.Count);

        var d = One("take the coin and the red key");
        Assert.True(d.Object1!.IsCompound);
        Assert.Equal(2, d.Object1.Parts.Count);
    }

    [Fact]
    public void MultipleCommands()
    {
        var o = P().Parse("take the coin and go north. open box then drop coin, look");
        Assert.True(o.Success);
        Assert.Equal(new[] { "take", "go", "open", "drop", "look" }, o.Commands.Select(c => c.ActionId));
    }

    [Fact]
    public void MultiWordNoun()
    {
        var c = One("examine the metal control panel");
        Assert.Equal("control panel", c.Object1!.Noun);
        Assert.Equal(new[] { "metal" }, c.Object1.Adjectives);
    }

    [Fact]
    public void Plurals()
    {
        var c = One("take keys");
        Assert.Equal("key", c.Object1!.Noun);
        Assert.True(c.Object1.Plural);
    }

    [Fact]
    public void QuotedTextAndTopic()
    {
        var s = One("say \"open sesame\" to the robot");
        Assert.Equal("say", s.ActionId);
        Assert.Equal("open sesame", s.Text);
        Assert.Equal("robot", s.Object2!.Noun);

        var a = One("ask the robot about the gold coin");
        Assert.Equal("ask", a.ActionId);
        Assert.Equal("robot", a.Object1!.Noun);
        Assert.Equal("the gold coin", a.Text);
    }

    [Fact]
    public void AddressingCharacters()
    {
        var c = One("robot, take the coin");
        Assert.Equal("take", c.ActionId);
        Assert.Equal("robot", c.Addressee!.Noun);

        var t = One("tell the robot to go north");
        Assert.Equal("order", t.ActionId);
        Assert.Equal("go north", t.Text);
    }

    [Fact]
    public void Numbers()
    {
        var a = World();
        a.Items.Add(new Item { Id = "dial", Name = "dial", Nouns = { "dial" }, Location = "r" });
        var c = One("turn the dial to 7", a);
        Assert.Equal(7, c.Number);
        Assert.Equal(12, One("turn dial to twelve", a).Number);
    }

    [Fact]
    public void Idioms_And_Filler()
    {
        Assert.Equal("look", One("where am I?").ActionId);
        Assert.Equal("open", One("I want to open the box").ActionId);
        Assert.Equal("open", One("please open the box").ActionId);
        Assert.Equal("inventory", One("what am I carrying").ActionId);
    }

    [Fact]
    public void SpellingCorrection()
    {
        var o = P().Parse("exmaine the wodden box");
        Assert.True(o.Success);
        Assert.Equal("examine", o.Commands[0].ActionId);
        Assert.Contains(o.Corrections, c => c.To == "wooden");
    }

    [Fact]
    public void UnknownWord()
    {
        var o = P().Parse("frobnicate the zzzq");
        Assert.False(o.Success);
        Assert.Equal("frobnicate", o.UnknownWord);
    }

    [Fact]
    public void CustomVerbWithGrammar()
    {
        var c = One("buff up the coin with the red key");
        Assert.Equal("polish", c.ActionId);
        Assert.Equal("coin", c.Object1!.Noun);
        Assert.Equal("key", c.Object2!.Noun);
        Assert.Equal("with", c.Preposition);
    }

    [Fact]
    public void CustomVerbWithoutGrammarAcceptsObjects()
    {
        var a = World();
        a.Vocabulary.Verbs.Add(new VerbDefinition { Id = "zap", Words = { "zap" } });
        var c = One("zap the robot", a);
        Assert.Equal("zap", c.ActionId);
        Assert.Equal("robot", c.Object1!.Noun);
    }

    [Fact]
    public void GiveWithTwoObjectOrders()
    {
        var c = One("give the robot the coin");
        Assert.Equal("give", c.ActionId);
        Assert.Equal("coin", c.Object1!.Noun);
        Assert.Equal("robot", c.Object2!.Noun);
        var d = One("give coin to robot");
        Assert.Equal("coin", d.Object1!.Noun);
        Assert.Equal("robot", d.Object2!.Noun);
    }

    [Fact]
    public void Pronouns()
    {
        var c = One("drop it");
        Assert.Equal("it", c.Object1!.Pronoun);
        var her = One("ask her about the coin");
        Assert.Equal("her", her.Object1!.Pronoun);
        Assert.Equal("the coin", her.Text);
        var possessive = One("take her gold coin");
        Assert.Null(possessive.Object1!.Pronoun);
        Assert.Equal("coin", possessive.Object1.Noun);
    }

    [Fact]
    public void SignificantLettersLikePaws()
    {
        var a = World();
        a.Settings.SignificantLetters = 5;
        var c = One("exami the woode boxxx", a);
        Assert.Equal("examine", c.ActionId);
    }

    [Fact]
    public void LenientParseWhenGrammarFails()
    {
        var o = P().Parse("open box with coin quickly and loudly");
        Assert.True(o.Success);
        Assert.Contains("quickly", o.Commands[0].Adverbs);
    }

    [Fact]
    public void Replacements()
    {
        var a = World();
        a.Vocabulary.Replacements["xyzzy2"] = "look";
        Assert.Equal("look", One("xyzzy2", a).ActionId);
    }
}
