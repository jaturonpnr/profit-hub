using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// Read-only analytics over realized Trades: the Monthly Performance Heatmap and the
/// per-EA drill-down. Reuses Reports.Filtered for ownership + account/EA scoping.
public static class Analytics
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();
        g.MapGet("/heatmap", GetHeatmap);
        g.MapGet("/eas/{magic:long}", GetEaDetail);
    }

    // Monthly Net Profit grid. Respects account + EA filter; IGNORES date (all years).
    private static async Task<IResult> GetHeatmap(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, long? magic)
    {
        if (!Reports.TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        var tz = Reports.Tz(user);
        var trades = await Reports.Filtered(db, user, ids, null, null, magic, tz)
            .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
        var cells = trades
            .GroupBy(t =>
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz);
                return (local.Year, local.Month);
            })
            .Select(grp => new
            {
                year = grp.Key.Year,
                month = grp.Key.Month,
                netProfit = grp.Sum(t => t.NetProfit),
                tradeCount = grp.Count(),
            })
            .OrderBy(c => c.year).ThenBy(c => c.month);
        return Results.Ok(cells);
    }

    // One EA's drill-down: summary metrics, equity curve, monthly breakdown,
    // day-of-week x hour heatmap (user tz), and recent trades. Respects account filter.
    private static async Task<IResult> GetEaDetail(long magic, ClaimsPrincipal user, AppDbContext db,
        string? accountIds)
    {
        if (!Reports.TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        var tz = Reports.Tz(user);
        var name = await db.EaNames
            .Where(e => e.UserId == user.UserId() && e.MagicNumber == magic)
            .Select(e => e.Name).FirstOrDefaultAsync();

        var trades = await Reports.Filtered(db, user, ids, null, null, magic, tz)
            .OrderBy(t => t.CloseTimeUtc).ThenBy(t => t.Id) // stable order for basket EAs (simultaneous closes)
            .Select(t => new { t.CloseTimeUtc, t.NetProfit, t.Symbol, t.Direction, t.Lots, t.Commission, t.Swap, t.ExecutionMs })
            .ToListAsync();

        if (trades.Count == 0)
            return Results.NotFound();

        var nets = trades.Select(t => t.NetProfit).ToList();
        var wins = nets.Count(n => n > 0);
        var (ddAmount, ddPct) = Metrics.RealizedDrawdown(nets);
        var execs = trades.Where(t => t.ExecutionMs != null).Select(t => t.ExecutionMs!.Value).ToList();
        decimal? avgExec = execs.Count > 0 ? Math.Round(execs.Average(), 1) : null;
        decimal? maxExec = execs.Count > 0 ? execs.Max() : null;

        var curveFull = new List<object>();
        decimal run = 0m;
        foreach (var t in trades)
        {
            run += t.NetProfit;
            curveFull.Add(new { t = t.CloseTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"), balance = Math.Round(run, 2) });
        }
        var curve = Downsample(curveFull, 500);

        var heat = trades
            .Select(t =>
            {
                var l = TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz);
                return new { dow = ((int)l.DayOfWeek + 6) % 7, hour = l.Hour, t.NetProfit };
            })
            .GroupBy(x => (x.dow, x.hour))
            .Select(grp => new { dow = grp.Key.dow, hour = grp.Key.hour, netProfit = grp.Sum(x => x.NetProfit), tradeCount = grp.Count() });

        var monthly = trades
            .GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, "month"))
            .OrderBy(grp => grp.Key)
            .Select(grp => new { periodStart = grp.Key, netProfit = grp.Sum(t => t.NetProfit), tradeCount = grp.Count() });

        var recent = trades.AsEnumerable().Reverse().Take(20)
            .Select(t => new
            {
                closeTimeUtc = t.CloseTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                symbol = t.Symbol, direction = t.Direction, lots = t.Lots,
                netProfit = t.NetProfit, commission = t.Commission, swap = t.Swap,
            });

        return Results.Ok(new
        {
            magicNumber = magic,
            name = name ?? "",
            netProfit = nets.Sum(),
            tradeCount = nets.Count,
            winRate = nets.Count > 0 ? Math.Round(100m * wins / nets.Count, 1) : 0m,
            profitFactor = Metrics.ProfitFactor(nets),
            expectancy = Metrics.Expectancy(nets),
            drawdownAmount = ddAmount,
            drawdownPct = ddPct,
            swap = trades.Sum(t => t.Swap),
            commission = trades.Sum(t => t.Commission),
            firstTradeUtc = trades.First().CloseTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            lastTradeUtc = trades.Last().CloseTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            avgExecutionMs = avgExec,
            maxExecutionMs = maxExec,
            equityCurve = curve,
            heatmap = heat,
            monthly,
            recentTrades = recent,
        });
    }

    private static List<object> Downsample(List<object> pts, int max)
    {
        if (pts.Count <= max) return pts;
        var stride = (int)Math.Ceiling(pts.Count / (double)max);
        var outp = new List<object>();
        for (var i = 0; i < pts.Count; i += stride) outp.Add(pts[i]);
        if (!ReferenceEquals(outp[^1], pts[^1])) outp[^1] = pts[^1]; // keep the final point, stay <= max
        return outp;
    }
}
