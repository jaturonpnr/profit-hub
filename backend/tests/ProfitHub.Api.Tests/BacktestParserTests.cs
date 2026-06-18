using ProfitHub.Api.Features;

namespace ProfitHub.Api.Tests;

public class BacktestParserTests
{
    private static ParsedBacktest ParseFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", name);
        using var fs = File.OpenRead(path);
        return BacktestParser.Parse(fs);
    }

    [Fact]
    public void Parses_qa_summary_kpis()
    {
        var r = ParseFixture("qa.xlsx");
        Assert.Equal("Quantum Athena", r.ExpertName);
        Assert.Equal("XAUUSD-ECN", r.Symbol);
        Assert.Equal("M15", r.Timeframe);
        Assert.Equal(new DateOnly(2026, 1, 1), r.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 6, 11), r.PeriodTo);
        Assert.Equal(1500m, r.InitialDeposit);
        Assert.Equal(3950.68m, r.NetProfit);
        Assert.Equal(4397.07m, r.GrossProfit);
        Assert.Equal(9.850288m, r.ProfitFactor);
        Assert.Equal(63.516736m, r.SharpeRatio);
        Assert.Equal(3.34m, r.BalanceDrawdownMaxPct);
        Assert.Equal(19.94m, r.EquityDrawdownMaxPct);
        Assert.Equal(831.81m, r.EquityDrawdownMaxAbs);
        Assert.Equal(306, r.TotalTrades);
        Assert.Equal(92.81m, r.WinRatePct);
        Assert.Equal(263.38m, r.ReturnPct);
        Assert.Equal(5442L, r.MagicNumber);
    }

    [Fact]
    public void Parses_qq_with_space_thousands_separator()
    {
        var r = ParseFixture("qq.xlsx");
        Assert.Equal("Quantum Queen MT5", r.ExpertName);
        Assert.Equal(3000m, r.InitialDeposit);
        Assert.Equal(7694.78m, r.NetProfit);
        Assert.Equal(16.51m, r.EquityDrawdownMaxPct);
        Assert.Equal(1620.16m, r.EquityDrawdownMaxAbs);
        Assert.Equal(1234L, r.MagicNumber);
    }
}
