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
            return Results.Ok(new { summary = ToSummaryFn(b), equityCurve = curve });
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
