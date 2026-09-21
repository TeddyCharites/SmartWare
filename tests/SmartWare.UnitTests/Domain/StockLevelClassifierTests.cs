using SmartWare.Domain.Enums;
using SmartWare.Domain.Services;

namespace SmartWare.UnitTests.Domain;

public sealed class StockLevelClassifierTests
{
    [Theory]
    [InlineData(0, 0, 5, 100, StockLevelStatus.Out)]
    [InlineData(10, 10, 5, 100, StockLevelStatus.Out)]
    [InlineData(10, 6, 5, 100, StockLevelStatus.Low)]
    [InlineData(101, 0, 5, 100, StockLevelStatus.Excess)]
    [InlineData(50, 5, 5, 100, StockLevelStatus.Healthy)]
    public void Classify_ReturnsExpectedStatus(
        int current,
        int reserved,
        int minimum,
        int maximum,
        StockLevelStatus expected)
    {
        var result = StockLevelClassifier.Classify(current, reserved, minimum, maximum);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void LowAvailability_TakesPriorityOverPhysicalExcess()
    {
        var result = StockLevelClassifier.Classify(120, 118, 5, 100);

        Assert.Equal(StockLevelStatus.Low, result);
    }

    [Fact]
    public void ReservedQuantity_CannotExceedCurrentQuantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StockLevelClassifier.Classify(5, 6, 2, 10));
    }
}
