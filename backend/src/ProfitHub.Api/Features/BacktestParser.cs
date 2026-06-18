using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ProfitHub.Api.Features;

/// Raised when an uploaded file is not a recognisable MT5 Strategy Tester report.
public class BacktestParseException(string message) : Exception(message);

/// One equity-curve point: a running balance at a point in time.
public record EquityPoint(string T, decimal Balance);

/// The parsed result of one MT5 Strategy Tester .xlsx, before persistence.
public record ParsedBacktest
{
    public required string ExpertName { get; init; }
    public string Symbol { get; init; } = "";
    public string Timeframe { get; init; } = "";
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public long? MagicNumber { get; init; }
    public decimal InitialDeposit { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal NetProfit { get; init; }
    public decimal GrossProfit { get; init; }
    public decimal GrossLoss { get; init; }
    public decimal ReturnPct { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal ExpectedPayoff { get; init; }
    public decimal RecoveryFactor { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal BalanceDrawdownMaxPct { get; init; }
    public decimal EquityDrawdownMaxPct { get; init; }
    public decimal EquityDrawdownMaxAbs { get; init; }
    public int TotalTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public IReadOnlyList<EquityPoint> EquityCurve { get; init; } = [];
    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();
}

public static class BacktestParser
{
    // Canonical field -> accepted labels (Thai + English). Labels are matched after
    // normalising: trimmed, trailing ':' removed, lower-cased. Many MT5 KPI labels are
    // already English even in a Thai report (Profit Factor, Sharpe Ratio, drawdowns).
    private static readonly Dictionary<string, string[]> Labels = new()
    {
        ["expert"] = ["expert"],
        ["symbol"] = ["สัญลักษณ์", "symbol"],
        ["period"] = ["ช่วงเวลา", "period"],
        ["currency"] = ["สกุลเงิน", "currency"],
        ["deposit"] = ["เงินตั้งต้น", "initial deposit"],
        ["netProfit"] = ["กำไรสุทธิ", "total net profit"],
        ["grossProfit"] = ["กำไรทั้งหมด", "gross profit"],
        ["grossLoss"] = ["ขาดทุนทั้งหมด", "gross loss"],
        ["profitFactor"] = ["profit factor"],
        ["expectedPayoff"] = ["expected payoff"],
        ["recoveryFactor"] = ["recovery factor"],
        ["sharpe"] = ["sharpe ratio"],
        ["balanceDdMax"] = ["balance drawdown maximal"],
        ["equityDdMax"] = ["equity drawdown maximal"],
        ["totalTrades"] = ["จำนวนการซื้อขายทั้งหมด", "total trades"],
        ["profitTrades"] = ["การซื้อขายที่กำไร (% ของทั้งหมด)", "profit trades (% of total)"],
    };

    // Cell text that marks the start of the deals table.
    private static readonly string[] DealsSection = ["การซื้อขาย", "deals"];

    public static ParsedBacktest Parse(Stream xlsx)
    {
        string[][] grid;
        try
        {
            using var wb = new XLWorkbook(xlsx);
            grid = ToGrid(wb.Worksheets.First());
        }
        catch (Exception e) when (e is not BacktestParseException)
        {
            throw new BacktestParseException("ไฟล์นี้อ่านไม่ออก ไม่ใช่ไฟล์ Excel ที่ถูกต้อง");
        }

        var kv = BuildKeyValues(grid);

        var expert = Get(kv, "expert");
        if (string.IsNullOrWhiteSpace(expert))
            throw new BacktestParseException("ไฟล์นี้ไม่ใช่รายงาน Strategy Tester (ไม่พบชื่อ Expert)");

        var (tf, from, to) = ParsePeriod(Get(kv, "period"));
        var deposit = Num(Get(kv, "deposit"));
        var net = Num(Get(kv, "netProfit"));

        return new ParsedBacktest
        {
            ExpertName = expert!.Trim(),
            Symbol = Get(kv, "symbol")?.Trim() ?? "",
            Timeframe = tf,
            PeriodFrom = from,
            PeriodTo = to,
            Currency = string.IsNullOrWhiteSpace(Get(kv, "currency")) ? "USD" : Get(kv, "currency")!.Trim(),
            MagicNumber = FindMagic(grid),
            InitialDeposit = deposit,
            NetProfit = net,
            GrossProfit = Num(Get(kv, "grossProfit")),
            GrossLoss = Num(Get(kv, "grossLoss")),
            ReturnPct = deposit > 0 ? Math.Round(net / deposit * 100m, 2) : 0m,
            ProfitFactor = Num(Get(kv, "profitFactor")),
            ExpectedPayoff = Num(Get(kv, "expectedPayoff")),
            RecoveryFactor = Num(Get(kv, "recoveryFactor")),
            SharpeRatio = Num(Get(kv, "sharpe")),
            BalanceDrawdownMaxPct = PercentInParens(Get(kv, "balanceDdMax")),
            EquityDrawdownMaxPct = PercentInParens(Get(kv, "equityDdMax")),
            EquityDrawdownMaxAbs = NumBeforeParens(Get(kv, "equityDdMax")),
            TotalTrades = (int)Num(Get(kv, "totalTrades")),
            WinRatePct = PercentInParens(Get(kv, "profitTrades")),
            EquityCurve = ParseEquityCurve(grid),
            Raw = kv,
        };
    }

    // ── grid + key/value helpers ───────────────────────────────────────────────

    private static string[][] ToGrid(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var rows = new string[lastRow][];
        for (var r = 1; r <= lastRow; r++)
        {
            var cols = new string[lastCol];
            for (var c = 1; c <= lastCol; c++)
                cols[c - 1] = ws.Cell(r, c).GetString().Trim();
            rows[r - 1] = cols;
        }
        return rows;
    }

    // Treat any cell ending in ':' as a label whose value is the next NON-EMPTY cell on
    // the same row. MT5 reports use merged cells so the value can be 1–3 columns away.
    private static Dictionary<string, string> BuildKeyValues(string[][] grid)
    {
        var kv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in grid)
            for (var c = 0; c < row.Length - 1; c++)
            {
                var cell = row[c];
                if (!cell.EndsWith(':')) continue;
                // Find the next non-empty cell on this row
                for (var v = c + 1; v < row.Length; v++)
                {
                    if (row[v].Length == 0) continue;
                    // Stop if we hit another label (ends with ':')
                    if (row[v].EndsWith(':')) break;
                    var key = Normalize(cell);
                    if (!kv.ContainsKey(key)) kv[key] = row[v];
                    break;
                }
            }
        return kv;
    }

    private static string Normalize(string label) =>
        label.TrimEnd().TrimEnd(':').Trim().ToLowerInvariant();

    private static string? Get(Dictionary<string, string> kv, string canonical)
    {
        foreach (var label in Labels[canonical])
            if (kv.TryGetValue(label.ToLowerInvariant(), out var v)) return v;
        return null;
    }

    // ── value parsing ──────────────────────────────────────────────────────────

    // MT5 uses a space (or comma) as the thousands separator and a dot as decimal.
    public static decimal Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        var cleaned = s.Replace(" ", "").Replace(",", "").Replace("%", "").Trim();
        var m = Regex.Match(cleaned, @"-?\d+(\.\d+)?");
        return m.Success && decimal.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    // "136.78 (3.34%)" -> 3.34
    public static decimal PercentInParens(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        var m = Regex.Match(s, @"\(([-\d\s.,]+)%\)");
        return m.Success ? Num(m.Groups[1].Value) : 0m;
    }

    // "831.81 (19.94%)" -> 831.81
    public static decimal NumBeforeParens(string? s) =>
        Num(string.IsNullOrWhiteSpace(s) ? null : s.Split('(')[0]);

    // "M15 (2026.01.01 - 2026.06.11)" -> ("M15", 2026-01-01, 2026-06-11)
    private static (string tf, DateOnly? from, DateOnly? to) ParsePeriod(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ("", null, null);
        var tf = Regex.Match(s, @"^\s*(\w+)").Groups[1].Value;
        var dates = Regex.Matches(s, @"(\d{4})\.(\d{2})\.(\d{2})");
        DateOnly? D(int i) => i < dates.Count
            ? new DateOnly(int.Parse(dates[i].Groups[1].Value), int.Parse(dates[i].Groups[2].Value), int.Parse(dates[i].Groups[3].Value))
            : null;
        return (tf, D(0), D(1));
    }

    // Best-effort magic number: any input line like "InpMagicNumber=5442".
    private static long? FindMagic(string[][] grid)
    {
        foreach (var row in grid)
            foreach (var cell in row)
            {
                var m = Regex.Match(cell, @"magic\w*\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                if (m.Success && long.TryParse(m.Groups[1].Value, out var v)) return v;
            }
        return null;
    }

    // Read the deals table ("การซื้อขาย"/"Deals"): each row's running "Balance" column
    // becomes an equity point. The first row is the opening "balance" deposit. Long runs
    // are stride-downsampled to <=1000 points (always keeping the first and last).
    private static IReadOnlyList<EquityPoint> ParseEquityCurve(string[][] grid)
    {
        var section = -1;
        for (var r = 0; r < grid.Length; r++)
            if (grid[r].Length > 0 && DealsSection.Contains(grid[r][0].ToLowerInvariant()))
            { section = r; break; }
        if (section < 0 || section + 1 >= grid.Length) return [];

        var header = grid[section + 1];
        int timeCol = IndexOf(header, "เวลา", "time");
        int balCol = IndexOf(header, "balance", "ยอดคงเหลือ");
        if (timeCol < 0 || balCol < 0) return [];

        var points = new List<EquityPoint>();
        for (var r = section + 2; r < grid.Length; r++)
        {
            var row = grid[r];
            if (balCol >= row.Length || timeCol >= row.Length) continue;
            if (row[timeCol].Length == 0 && row[balCol].Length == 0) continue;
            if (row[balCol].Length == 0) continue;
            // Skip summary/totals rows that have no timestamp
            if (row[timeCol].Length == 0) continue;
            points.Add(new EquityPoint(NormalizeTime(row[timeCol]), Num(row[balCol])));
        }
        return Downsample(points, 1000);
    }

    private static int IndexOf(string[] header, params string[] labels)
    {
        for (var c = 0; c < header.Length; c++)
            if (labels.Contains(header[c].ToLowerInvariant())) return c;
        return -1;
    }

    // "2026.01.01 00:00:00" -> "2026-01-01T00:00:00" (ISO, for the frontend chart).
    private static string NormalizeTime(string s)
    {
        var m = Regex.Match(s, @"(\d{4})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2}):(\d{2})");
        return m.Success
            ? $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}T{m.Groups[4].Value}:{m.Groups[5].Value}:{m.Groups[6].Value}"
            : s;
    }

    private static IReadOnlyList<EquityPoint> Downsample(List<EquityPoint> pts, int max)
    {
        if (pts.Count <= max) return pts;
        var stride = (int)Math.Ceiling(pts.Count / (double)max);
        var outp = new List<EquityPoint>();
        for (var i = 0; i < pts.Count; i += stride) outp.Add(pts[i]);
        if (outp[^1] != pts[^1]) outp.Add(pts[^1]); // always keep the final balance
        return outp;
    }
}
