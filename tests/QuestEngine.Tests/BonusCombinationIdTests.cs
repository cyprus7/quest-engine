using FluentAssertions;
using QuestEngine.Domain;
using Xunit;

public class BonusCombinationIdTests
{
    [Fact]
    public void Same_Rewards_Any_Order_Same_Id()
    {
        var a = new [] {
            new RewardDef("free_spins", 20, 0.1, "g1"),
            new RewardDef("xp", 25, null, null)
        };
        var b = new [] {
            new RewardDef("xp", 25, null, null),
            new RewardDef("free_spins", 20, 0.1, "g1")
        };
        var id1 = BonusCombinationId.Compute(a);
        var id2 = BonusCombinationId.Compute(b);
        id1.Should().Be(id2);
    }
}
