using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// Backtest analysis. Users upload an MT5 Strategy Tester .xlsx; it is parsed
/// server-side (BacktestParser) and stored as a Backtest owned by the user.
/// Isolated from live Account/Trade data (ADR 0004).
public static class Backtests
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/backtests").RequireAuthorization();

        // List — summary columns only. The projection runs server-side so the heavy
        // EquityCurveJson / RawMetricsJson columns are never loaded for the list.
        g.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            await db.Backtests.Where(b => b.UserId == user.UserId())
                .OrderByDescending(b => b.CreatedAtUtc)
                .Select(ToSummary).ToListAsync());

        // Detail — full row, including the equity curve.
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var b = await db.Backtests.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId());
            if (b is null) return Results.NotFound();
            List<EquityPoint> curve;
            try { curve = JsonSerializer.Deserialize<List<EquityPoint>>(b.EquityCurveJson) ?? []; }
            catch (JsonException) { curve = []; }
            List<InputEntry> inputs;
            try { inputs = JsonSerializer.Deserialize<List<InputEntry>>(b.InputsJson ?? "[]") ?? []; }
            catch (JsonException) { inputs = []; }
            List<BtTrade> trades;
            try { trades = JsonSerializer.Deserialize<List<BtTrade>>(string.IsNullOrEmpty(b.TradesJson) ? "[]" : b.TradesJson) ?? []; }
            catch (JsonException) { trades = []; }

            // Day-of-week (0=Mon) × hour heatmap + monthly buckets, broker-file time
            // AS-IS (no timezone conversion — see plan: broker time as-is).
            // InvariantCulture on BOTH parse and format: on a Thai-locale host the
            // ambient culture uses the Buddhist calendar, turning "2026-05" into "2569-05".
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var heatmap = trades
                .Select(t => (dt: DateTime.Parse(t.T, inv), t.Profit))
                .GroupBy(x => (dow: ((int)x.dt.DayOfWeek + 6) % 7, x.dt.Hour))
                .Select(g => new
                {
                    g.Key.dow,
                    hour = g.Key.Hour,
                    netProfit = Math.Round(g.Sum(x => x.Profit), 2),
                    tradeCount = g.Count(),
                })
                .ToList();
            var monthly = trades
                .GroupBy(t => DateTime.Parse(t.T, inv).ToString("yyyy-MM", inv))
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    month = g.Key,
                    netProfit = Math.Round(g.Sum(x => x.Profit), 2),
                    tradeCount = g.Count(),
                })
                .ToList();
            var tradeStats = BuildTradeStats(b.RawMetricsJson);

            // trades: the compact per-trade series itself — the frontend drills
            // month → day → trades from it client-side (a few KB at most).
            return Results.Ok(new { summary = ToSummaryFn(b), equityCurve = curve, inputs, tradeStats, heatmap, monthly, trades });
        });

        // Upload — multipart/form-data, field name "file".
        g.MapPost("/", async (HttpRequest req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "expected multipart/form-data" });
            var form = await req.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "no file" });
            if (file.Length > 10 * 1024 * 1024) return Results.BadRequest(new { error = "ไฟล์ใหญ่เกินไป (สูงสุด 10MB)" });

            ParsedBacktest parsed;
            try
            {
                await using var stream = file.OpenReadStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                parsed = BacktestParser.Parse(ms);
            }
            catch (BacktestParseException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }

            var b = new Backtest
            {
                UserId = user.UserId(),
                ExpertName = parsed.ExpertName,
                Symbol = parsed.Symbol,
                Timeframe = parsed.Timeframe,
                PeriodFrom = parsed.PeriodFrom,
                PeriodTo = parsed.PeriodTo,
                MagicNumber = parsed.MagicNumber,
                InitialDeposit = parsed.InitialDeposit,
                Currency = parsed.Currency,
                NetProfit = parsed.NetProfit,
                GrossProfit = parsed.GrossProfit,
                GrossLoss = parsed.GrossLoss,
                ReturnPct = parsed.ReturnPct,
                ProfitFactor = parsed.ProfitFactor,
                ExpectedPayoff = parsed.ExpectedPayoff,
                RecoveryFactor = parsed.RecoveryFactor,
                SharpeRatio = parsed.SharpeRatio,
                BalanceDrawdownMaxPct = parsed.BalanceDrawdownMaxPct,
                EquityDrawdownMaxPct = parsed.EquityDrawdownMaxPct,
                EquityDrawdownMaxAbs = parsed.EquityDrawdownMaxAbs,
                TotalTrades = parsed.TotalTrades,
                WinRatePct = parsed.WinRatePct,
                EquityCurveJson = JsonSerializer.Serialize(parsed.EquityCurve),
                RawMetricsJson = JsonSerializer.Serialize(parsed.Raw),
                InputsJson = JsonSerializer.Serialize(parsed.Inputs),
                TradesJson = JsonSerializer.Serialize(parsed.Trades),
                SourceFileName = file.FileName,
            };
            db.Backtests.Add(b);
            await db.SaveChangesAsync();
            return Results.Ok(ToSummaryFn(b));
        }).DisableAntiforgery();

        // Delete own backtest.
        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var b = await db.Backtests.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId());
            if (b is null) return Results.NotFound();
            db.Backtests.Remove(b);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    // Bilingual label map: RawMetricsJson keys (lowercased labels from BuildKeyValues)
    // → stable stat keys. Values are the report's raw strings (e.g. "384.6",
    // "30 (2 711.33)", "1:48:37") — displayed as-is, no numeric parsing. Works for
    // backtests uploaded before TradesJson existed, since Raw has been stored since v1.
    private static readonly (string KeyOut, string[] Labels)[] StatLabels =
    [
        ("largestWin",  ["largest profit trade", "สูงสุด การซื้อขายที่กำไร"]),
        ("largestLoss", ["largest loss trade", "สูงสุด การซื้อขายที่ขาดทุน"]),
        ("avgWin",      ["average profit trade", "เฉลี่ย การซื้อขายที่กำไร"]),
        ("avgLoss",     ["average loss trade", "เฉลี่ย การซื้อขายที่ขาดทุน"]),
        ("maxConsecWins",   ["maximum consecutive wins ($)", "สูงสุด กำไรติดต่อกัน ($)"]),
        ("maxConsecLosses", ["maximum consecutive losses ($)", "สูงสุด ขาดทุนติดต่อกัน ($)"]),
        ("avgHolding",  ["average position holding time", "เวลาถือสถานะเฉลี่ย"]),
        ("maxHolding",  ["maximal position holding time", "เวลาถือสถานะสูงสุด"]),
    ];

    private static Dictionary<string, string> BuildTradeStats(string? rawMetricsJson)
    {
        Dictionary<string, string> raw;
        try { raw = JsonSerializer.Deserialize<Dictionary<string, string>>(string.IsNullOrEmpty(rawMetricsJson) ? "{}" : rawMetricsJson) ?? []; }
        catch (JsonException) { raw = []; }
        var stats = new Dictionary<string, string>();
        foreach (var (keyOut, labels) in StatLabels)
            foreach (var label in labels)
                if (raw.TryGetValue(label.ToLowerInvariant(), out var v)) { stats[keyOut] = v; break; }
        return stats;
    }

    /// Light DTO for list + detail. Serializes to camelCase JSON (Web defaults), so the
    /// shape stays { expertName, ... }. Excludes the heavy EquityCurveJson/RawMetricsJson.
    public record BacktestSummary(
        Guid Id, string ExpertName, string Symbol, string Timeframe,
        DateOnly? PeriodFrom, DateOnly? PeriodTo, long? MagicNumber,
        decimal InitialDeposit, string Currency, decimal NetProfit, decimal ReturnPct,
        decimal ProfitFactor, decimal ExpectedPayoff, decimal RecoveryFactor, decimal SharpeRatio,
        decimal BalanceDrawdownMaxPct, decimal EquityDrawdownMaxPct, int TotalTrades,
        decimal WinRatePct, string SourceFileName, DateTime CreatedAtUtc);

    // One projection, used both server-side (EF translates the expression to SELECT the
    // scalar columns only) and in memory for the already-loaded detail row.
    private static readonly Expression<Func<Backtest, BacktestSummary>> ToSummary = b => new BacktestSummary(
        b.Id, b.ExpertName, b.Symbol, b.Timeframe, b.PeriodFrom, b.PeriodTo, b.MagicNumber,
        b.InitialDeposit, b.Currency, b.NetProfit, b.ReturnPct, b.ProfitFactor, b.ExpectedPayoff,
        b.RecoveryFactor, b.SharpeRatio, b.BalanceDrawdownMaxPct, b.EquityDrawdownMaxPct,
        b.TotalTrades, b.WinRatePct, b.SourceFileName, b.CreatedAtUtc);

    private static readonly Func<Backtest, BacktestSummary> ToSummaryFn = ToSummary.Compile();
}
