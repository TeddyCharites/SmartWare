using SmartWare.Domain.Services;

namespace SmartWare.UnitTests.Domain;

public sealed class MovingAverageCostCalculatorTests
{
    [Fact]
    public void EmptyInventory_UsesIncomingCost()
    {
        var result = MovingAverageCostCalculator.Calculate(0, 999, 5, 125_000);

        Assert.Equal(125_000, result);
    }

    [Fact]
    public void ExistingInventory_CalculatesWeightedAverage()
    {
        var result = MovingAverageCostCalculator.Calculate(10, 100, 5, 160);

        Assert.Equal(120, result);
    }

    [Fact]
    public void Result_IsRoundedToFourDecimalPlaces()
    {
        var result = MovingAverageCostCalculator.Calculate(2, 10, 1, 11);

        Assert.Equal(10.3333m, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IncomingQuantity_MustBePositive(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingAverageCostCalculator.Calculate(1, 10, quantity, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IncomingCost_MustBePositive(decimal cost)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingAverageCostCalculator.Calculate(1, 10, 1, cost));
    }
}
