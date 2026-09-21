using SmartWare.Application.Dashboard;

namespace SmartWare.UnitTests.Application;

public sealed class DashboardSnapshotTests
{
    [Fact]
    public void ChartMaximum_ReturnsLargestMovementQuantity()
    {
        var snapshot = CreateSnapshot(
            new MonthlyStockMovement(new DateOnly(2026, 8, 1), 25, 40),
            new MonthlyStockMovement(new DateOnly(2026, 9, 1), 80, 35));

        Assert.Equal(80, snapshot.ChartMaximum);
    }

    [Fact]
    public void ChartMaximum_ReturnsOneWhenThereIsNoMovement()
    {
        var snapshot = CreateSnapshot();

        Assert.Equal(1, snapshot.ChartMaximum);
    }

    private static DashboardSnapshot CreateSnapshot(params MonthlyStockMovement[] movements) =>
        new(0, 0, 0, 0, 0, [], [], movements);
}
