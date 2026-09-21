namespace SmartWare.Domain.Services;

public sealed record DemandForecastResult(
    bool HasHistory,
    int ObservationDays,
    double AverageDailyDemand,
    double ForecastQuantity,
    double? Mae,
    double? Rmse,
    double? Mape);

public static class DemandForecastCalculator
{
    public static DemandForecastResult Calculate(
        IReadOnlyList<int> dailyDemand,
        int horizonDays = 30,
        int windowDays = 7)
    {
        ArgumentNullException.ThrowIfNull(dailyDemand);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizonDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowDays);

        if (dailyDemand.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(dailyDemand));
        }

        if (dailyDemand.Count == 0 || dailyDemand.All(value => value == 0))
        {
            return new DemandForecastResult(false, dailyDemand.Count, 0, 0, null, null, null);
        }

        var window = dailyDemand.TakeLast(Math.Min(windowDays, dailyDemand.Count)).ToArray();
        var average = window.Average();
        var errors = new List<double>();
        var absolutePercentageErrors = new List<double>();

        for (var index = 1; index < dailyDemand.Count; index++)
        {
            var priorWindow = dailyDemand
                .Skip(Math.Max(0, index - windowDays))
                .Take(Math.Min(windowDays, index));
            var prediction = priorWindow.Average();
            var actual = dailyDemand[index];
            var error = actual - prediction;
            errors.Add(error);
            if (actual > 0)
            {
                absolutePercentageErrors.Add(Math.Abs(error) / actual * 100d);
            }
        }

        return new DemandForecastResult(
            true,
            dailyDemand.Count,
            average,
            average * horizonDays,
            errors.Count == 0 ? null : errors.Average(Math.Abs),
            errors.Count == 0 ? null : Math.Sqrt(errors.Average(error => error * error)),
            absolutePercentageErrors.Count == 0 ? null : absolutePercentageErrors.Average());
    }
}
