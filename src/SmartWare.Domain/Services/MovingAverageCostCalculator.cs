namespace SmartWare.Domain.Services;

public static class MovingAverageCostCalculator
{
    public static decimal Calculate(
        int currentQuantity,
        decimal currentAverageCost,
        int incomingQuantity,
        decimal incomingUnitCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(currentAverageCost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(incomingQuantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(incomingUnitCost);

        var newQuantity = checked(currentQuantity + incomingQuantity);
        var totalValue = currentQuantity * currentAverageCost + incomingQuantity * incomingUnitCost;
        return decimal.Round(totalValue / newQuantity, 4, MidpointRounding.AwayFromZero);
    }
}
