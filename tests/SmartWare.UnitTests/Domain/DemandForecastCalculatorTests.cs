using SmartWare.Domain.Services;

namespace SmartWare.UnitTests.Domain;

public sealed class DemandForecastCalculatorTests
{
    [Fact]
    public void EmptyHistory_DoesNotInventForecast()
    {
        var result = DemandForecastCalculator.Calculate([]);

        Assert.False(result.HasHistory);
        Assert.Equal(0, result.ForecastQuantity);
        Assert.Null(result.Mae);
    }

    [Fact]
    public void SevenDayAverage_IsProjectedAcrossHorizon()
    {
        var result = DemandForecastCalculator.Calculate([1, 2, 3, 4, 5, 6, 7], 30, 7);

        Assert.True(result.HasHistory);
        Assert.Equal(4, result.AverageDailyDemand, 5);
        Assert.Equal(120, result.ForecastQuantity, 5);
        Assert.NotNull(result.Rmse);
    }

    [Fact]
    public void NegativeDemand_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DemandForecastCalculator.Calculate([1, -1, 2]));
    }
}
