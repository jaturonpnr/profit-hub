using ClosedXML.Excel;
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

    [Fact]
    public void Builds_equity_curve_from_deals()
    {
        var r = ParseFixture("qa.xlsx");
        Assert.NotEmpty(r.EquityCurve);
        // First deal is the "balance" deposit row: balance == initial deposit.
        Assert.Equal(1500m, r.EquityCurve[0].Balance);
        Assert.StartsWith("2026-01-01", r.EquityCurve[0].T);
        // Last point is the final running balance of the run.
        Assert.Equal(5450.68m, r.EquityCurve[^1].Balance);
        Assert.True(r.EquityCurve.Count <= 1000); // downsampled cap
    }

    [Fact]
    public void Parses_ea_inputs_grouped_by_section()
    {
        var r = ParseFixture("omg.xlsx");
        Assert.Contains(r.Inputs, i => i.Key == "MAGIC" && i.Value == "7337");
        var sl = r.Inputs.Single(i => i.Key == "InpSlUSD");
        Assert.Equal("30.0", sl.Value);
        var autoLot = r.Inputs.Single(i => i.Key == "Inp_auto_lot");
        Assert.Equal("Risk Management", autoLot.Section);   // from "lineRisk=>>> Risk Management"
        Assert.Contains(r.Inputs, i => i.Section == "Permitted Days" && i.Key == "Inp_TradeSunday");
        // KPIs of the new fixture still parse (sanity)
        Assert.Equal("Quantum OmniGold", r.ExpertName);
        Assert.Equal(2485.91m, r.NetProfit);
        Assert.Equal(7337L, r.MagicNumber);
    }

    [Fact]
    public void Parses_thai_report_inputs_too()
    {
        var r = ParseFixture("qa.xlsx");
        Assert.Contains(r.Inputs, i => i.Key == "InpMagicNumber" && i.Value == "5442");
        Assert.Contains(r.Inputs, i => i.Key == "InpLotsFixed"); // grouped under "input group #1"
    }

    [Fact]
    public void Emits_backtest_trades_that_reconcile_with_net_profit_omg()
    {
        var r = ParseFixture("omg.xlsx");
        Assert.Equal(49, r.Trades.Count);                 // report: Total Trades 49
        Assert.All(r.Trades, t => Assert.Contains(t.Dir, new[] { "buy", "sell" }));
        // Invariant: per-trade profits (incl. commission/swap of both legs) sum to
        // the report's Total Net Profit exactly.
        Assert.Equal(2485.91m, Math.Round(r.Trades.Sum(t => t.Profit), 2));
        Assert.StartsWith("2026-05-", r.Trades[0].T);
        Assert.All(r.Trades, t => Assert.True(t.Lots > 0));
    }

    [Fact]
    public void Emits_backtest_trades_that_reconcile_with_net_profit_qa()
    {
        var r = ParseFixture("qa.xlsx");
        Assert.Equal(306, r.Trades.Count);                // report: Total Trades 306
        Assert.All(r.Trades, t => Assert.Contains(t.Dir, new[] { "buy", "sell" }));
        Assert.Equal(3950.68m, Math.Round(r.Trades.Sum(t => t.Profit), 2));
    }

    private static Stream EnglishReportXlsx()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        void Put(int r, params string[] cells) { for (var c = 0; c < cells.Length; c++) ws.Cell(r, c + 1).Value = cells[c]; }
        Put(1, "Strategy Tester Report");
        Put(2, "Settings");
        Put(3, "Expert:", "My EA");
        Put(4, "Symbol:", "XAUUSD");
        Put(5, "Period:", "H1 (2026.01.01 - 2026.02.01)");
        Put(6, "Initial Deposit:", "2 000");
        Put(7, "Currency:", "USD");
        Put(8, "InpMagicNumber=777");
        Put(9, "Results");
        Put(10, "Total Net Profit:", "500.50", "Balance Drawdown Maximal:", "10.00 (1.50%)", "Equity Drawdown Maximal:", "50.00 (5.00%)");
        Put(11, "Profit Factor:", "2.5", "Total Trades:", "10");
        Put(12, "Profit Trades (% of total):", "7 (70.00%)");
        Put(13, "Deals");
        Put(14, "Time", "Deal", "Symbol", "Type", "Direction", "Volume", "Price", "Order", "Commission", "Swap", "Profit", "Balance", "Comment");
        Put(15, "2026.01.01 00:00:00", "1", "balance", "", "", "", "2000", "", "", "", "", "2000");
        Put(16, "2026.01.02 10:00:00", "2", "XAUUSD", "buy", "out", "0.10", "2700", "2", "-0.50", "0", "500.50", "2500.50");
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Parses_english_labels()
    {
        using var s = EnglishReportXlsx();
        var r = BacktestParser.Parse(s);
        Assert.Equal("My EA", r.ExpertName);
        Assert.Equal(2000m, r.InitialDeposit);       // "2 000" with space thousands sep
        Assert.Equal(500.50m, r.NetProfit);
        Assert.Equal(5.00m, r.EquityDrawdownMaxPct);
        Assert.Equal(70.00m, r.WinRatePct);
        Assert.Equal(777L, r.MagicNumber);
        Assert.Equal(2500.50m, r.EquityCurve[^1].Balance);
        // This synthetic report has no "Inputs:" label row — the inputs pass must yield
        // an empty list, not crash or capture stray key=value lines outside the region.
        Assert.Empty(r.Inputs);
    }

    [Fact]
    public void Input_value_containing_equals_splits_on_first_equals_only()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        void Put(int r, params string[] cells) { for (var c = 0; c < cells.Length; c++) ws.Cell(r, c + 1).Value = cells[c]; }
        Put(1, "Strategy Tester Report");
        Put(2, "Settings");
        Put(3, "Expert:", "My EA");
        Put(4, "Inputs:", "InpComment=QQ=v2");
        Put(5, "InpFormula=a=b+c");
        Put(6, "Company:", "X");
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        var r = BacktestParser.Parse(ms);
        Assert.Equal("QQ=v2", r.Inputs.Single(i => i.Key == "InpComment").Value);
        Assert.Equal("a=b+c", r.Inputs.Single(i => i.Key == "InpFormula").Value);
    }

    [Fact]
    public void Throws_on_non_report_file()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        ws.Cell(1, 1).Value = "hello";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        Assert.Throws<BacktestParseException>(() => BacktestParser.Parse(ms));
    }

    [Fact]
    public void Throws_on_garbage_bytes()
    {
        using var ms = new MemoryStream("not an xlsx"u8.ToArray());
        Assert.Throws<BacktestParseException>(() => BacktestParser.Parse(ms));
    }
}
