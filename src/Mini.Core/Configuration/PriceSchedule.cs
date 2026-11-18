// Python: loop.py rate
namespace Mini.Core.Configuration;

/// <summary>What share of the list prices applies at a given time. Some providers charge less off-peak.</summary>
// Python: loop.py rate
public sealed class PriceSchedule(TimeProvider time)
{
    /// <summary>Share of the list prices that applies now: 1.0, or the model's off-peak share outside its peak hours (UTC, weekdays).</summary>
    // Python: loop.py rate
    public double Rate(ModelConfig cfg)
    {
        if (cfg.PeakUtc is null)
            return 1.0;
        var now = time.GetUtcNow();
        var weekday = now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        var peak = weekday && cfg.PeakUtc.Any(hours => hours.Count >= 2 && hours[0] <= now.Hour && now.Hour < hours[1]);
        return peak ? 1.0 : cfg.OffPeak;
    }
}
