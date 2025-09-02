using FluentAssertions;
using QuestEngine.Domain;
using Xunit;

public class WeightedPickerTests
{
    [Theory]
    [InlineData(new int[]{1,0}, 0.0, 0)]
    [InlineData(new int[]{1,1}, 0.49, 0)]
    [InlineData(new int[]{1,1}, 0.51, 1)]
    [InlineData(new int[]{1,3}, 0.74, 0)]
    [InlineData(new int[]{1,3}, 0.76, 1)]
    public void Picks_Index_By_Weights(int[] weights, double u01, int expected)
    {
        var idx = WeightedPicker.PickIndex(weights, u01);
        idx.Should().Be(expected);
    }
}
