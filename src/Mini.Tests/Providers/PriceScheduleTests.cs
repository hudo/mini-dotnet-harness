using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Mini.Tests.Fakes;

namespace Mini.Tests.Providers;

/// <summary>PriceSchedule at fixed instants: which share of the list prices applies, for the shipped DeepSeek windows and for others.</summary>
public class PriceScheduleTests
{
    static (PriceSchedule Prices, ModelCatalog Catalog) At(string utc)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(utc + ":00Z", System.Globalization.CultureInfo.InvariantCulture));
        return (new PriceSchedule(time), new TestRuntime().Catalog);
    }

    // Python test: test_deepseek_peak_hours_at_their_boundaries
    // The instants are Fri 6 Nov, Sun 8 Nov and Mon 9 Nov 2026; the lower hour of a window is peak, the upper one is not.
    [Theory]
    [InlineData("2026-11-06 01:00", 1.0)]
    [InlineData("2026-11-06 00:59", 0.5)]
    [InlineData("2026-11-06 03:59", 1.0)]
    [InlineData("2026-11-06 04:00", 0.5)]
    [InlineData("2026-11-06 06:00", 1.0)]
    [InlineData("2026-11-06 10:00", 0.5)]
    [InlineData("2026-11-08 02:00", 0.5)]
    [InlineData("2026-11-09 01:00", 1.0)]
    public void DeepseekPeakHoursAtTheirBoundaries(string utc, double share)
    {
        var (prices, catalog) = At(utc.Replace(' ', 'T'));

        Assert.Equal(share, prices.Rate(catalog.For("deepseek-flash")));
        Assert.Equal(1.0, prices.Rate(new ModelConfig())); // no peak_utc: always the list price
    }

    // C#-only: the shipped windows and off-peak share the boundary cases above rely on
    [Fact]
    public void TheShippedDeepseekEntryNamesItsWindows()
    {
        var shipped = new TestRuntime().Catalog.For("deepseek-flash");

        Assert.Equal("[[1,4],[6,10]]", JsonSerializer.Serialize(shipped.PeakUtc));
        Assert.Equal(0.5, shipped.OffPeak);
    }

    // C#-only: one of several windows matches, and an empty list of windows is never peak
    [Theory]
    [InlineData("2026-11-06T02:00", 1.0)] // Friday, in [0, 3)
    [InlineData("2026-11-06T05:00", 0.25)] // between the windows
    [InlineData("2026-11-06T23:00", 1.0)] // Friday, in [22, 24)
    [InlineData("2026-11-07T23:00", 0.25)] // Saturday: weekends are off-peak all day
    public void OneOfSeveralWindowsMatches(string utc, double share)
    {
        var (prices, _) = At(utc);
        var model = new ModelConfig { PeakUtc = [[0, 3], [22, 24]], OffPeak = 0.25 };

        Assert.Equal(share, prices.Rate(model));
        Assert.Equal(0.25, prices.Rate(new ModelConfig { PeakUtc = [], OffPeak = 0.25 }));
    }

    // C#-only: the clock is read at each call, not once at construction
    [Fact]
    public void TheRateFollowsTheClock()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-06T02:30:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var prices = new PriceSchedule(time);
        var model = new TestRuntime().Catalog.For("deepseek-flash");

        Assert.Equal(1.0, prices.Rate(model));
        time.Advance(TimeSpan.FromHours(3)); // 05:30: between the windows

        Assert.Equal(0.5, prices.Rate(model));
    }
}
