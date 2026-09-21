using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Services;

public static class StockLevelClassifier
{
    public static StockLevelStatus Classify(
        int currentQuantity,
        int reservedQuantity,
        int minimumStock,
        int maximumStock)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(reservedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumStock);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStock);
        if (reservedQuantity > currentQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedQuantity),
                "Reserved quantity cannot exceed current quantity.");
        }

        var availableQuantity = currentQuantity - reservedQuantity;
        if (availableQuantity == 0)
        {
            return StockLevelStatus.Out;
        }

        if (availableQuantity <= minimumStock)
        {
            return StockLevelStatus.Low;
        }

        if (maximumStock > 0 && currentQuantity > maximumStock)
        {
            return StockLevelStatus.Excess;
        }

        return StockLevelStatus.Healthy;
    }
}
