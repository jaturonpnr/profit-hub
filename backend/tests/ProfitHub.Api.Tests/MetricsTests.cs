using ProfitHub.Api.Features;

namespace ProfitHub.Api.Tests;

public class MetricsTests
{
    [Fact]
    public void ProfitFactor_is_gross_profit_over_gross_loss()
    {
        Assert.Equal(3.0m, Metrics.ProfitFactor([100m, 50m, -30m, -20m]));
    }

    [Fact]
    public void ProfitFactor_is_null_when_no_losses()
    {
        Assert.Null(Metrics.ProfitFactor([10m, 5m]));
    }

    [Fact]
    public void ProfitFactor_is_zero_for_no_trades()
    {
        Assert.Equal(0m, Metrics.ProfitFactor([]));
    }

    [Fact]
    public void Expectancy_is_average_net_per_trade()
    {
        Assert.Equal(25m, Metrics.Expectancy([100m, 50m, -30m, -20m]));
        Assert.Equal(0m, Metrics.Expectancy([]));
    }

    [Fact]
    public void RealizedDrawdown_is_deepest_peak_to_trough_of_cumulative()
    {
        var (amount, pct) = Metrics.RealizedDrawdown([100m, -40m, 100m, -50m]);
        Assert.Equal(50m, amount);
        Assert.Equal(31.25m, pct);
    }

    [Fact]
    public void RealizedDrawdown_zero_when_monotonic_up()
    {
        var (amount, pct) = Metrics.RealizedDrawdown([10m, 20m, 30m]);
        Assert.Equal(0m, amount);
        Assert.Equal(0m, pct);
    }

    [Fact]
    public void Sparkline_is_cumulative_and_downsampled()
    {
        var s = Metrics.Sparkline([10m, 10m, 10m], 24);
        Assert.Equal(new[] { 10m, 20m, 30m }, s);
        // downsample: 100 points -> <= 10, last kept
        var many = Enumerable.Repeat(1m, 100).ToList();
        var ds = Metrics.Sparkline(many, 10);
        Assert.True(ds.Count <= 11);
        Assert.Equal(100m, ds[^1]);
    }
}
