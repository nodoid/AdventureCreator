using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Tests.Engine;

public class VariableComparisonTests
{
    /// <summary>A one-room game where PROBE prints which comparisons of "a" and "b" (with offset 2) are true.</summary>
    private static GameEngine Game(int a, int b)
    {
        var adv = new Adventure { Title = "Compare", StartRoomId = "room" };
        adv.Rooms.Add(new Room { Id = "room", Name = "Room", Description = "A room." });
        adv.Variables.Add(new Variable { Name = "a", InitialValue = a });
        adv.Variables.Add(new Variable { Name = "b", InitialValue = b });
        adv.Vocabulary.Verbs.Add(new VerbDefinition { Id = "probe", Words = { "probe" } });
        void Probe(string text, ConditionType type, int offset) => adv.Triggers.Add(new Trigger
        {
            Id = text, Verb = "probe", StopsCommand = false,
            Conditions = { new Condition(type, "a", offset, "b") },
            Actions = { GameAction.Say(text) },
        });
        Probe("[eq]", ConditionType.VarEqualsVar, 0);
        Probe("[gt]", ConditionType.VarGreaterVar, 0);
        Probe("[lt]", ConditionType.VarLessVar, 0);
        Probe("[eq+2]", ConditionType.VarEqualsVar, 2);
        Probe("[gt+2]", ConditionType.VarGreaterVar, 2);
        Probe("[lt+2]", ConditionType.VarLessVar, 2);
        var e = new GameEngine(adv, randomSeed: 1);
        e.Start();
        return e;
    }

    [Theory]
    [InlineData(5, 5, "[eq]", "[lt+2]")]
    [InlineData(7, 3, "[gt]", "[gt+2]")]
    [InlineData(2, 6, "[lt]", "[lt+2]")]
    [InlineData(8, 6, "[gt]", "[eq+2]")]
    public void ComparesTwoVariablesWithAnOffset(int a, int b, string first, string second)
    {
        var text = Game(a, b).Submit("probe").Text;
        var shown = new[] { "[eq]", "[gt]", "[lt]", "[eq+2]", "[gt+2]", "[lt+2]" }.Where(t => text.Contains(t)).ToArray();
        Assert.Equal(new[] { first, second }, shown);
    }

    [Fact]
    public void DescribesComparisons()
    {
        Assert.Equal("c7 < c8", new Condition(ConditionType.VarLessVar, "c7", 0, "c8").ToString());
        Assert.Equal("c7 > @turns - 2", new Condition(ConditionType.VarGreaterVar, "c7", -2, "@turns").ToString());
    }
}
