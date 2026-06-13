using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Export
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/export").RequireAuthorization();

        g.MapGet("/trades.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic) =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            var tz = Reports.Tz(user);
            var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
                .ToDictionaryAsync(a => a.Id, a => a.Name);
            var trades = await Reports.Filtered(db, user, ids, from, to, magic, tz)
                .OrderBy(t => t.CloseTimeUtc).ToListAsync();
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("CloseTime(Local),Account,Symbol,Direction,Lots,OpenPrice,ClosePrice,GrossProfit,Commission,Swap,NetProfit,MagicNumber,Comment\n");
            foreach (var t in trades)
                sb.Append(string.Join(',',
                    TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz).ToString("yyyy-MM-dd HH:mm:ss", inv),
                    Csv(accounts.GetValueOrDefault(t.AccountId, "")), Csv(t.Symbol), t.Direction,
                    t.Lots.ToString(inv), t.OpenPrice.ToString(inv), t.ClosePrice.ToString(inv),
                    t.GrossProfit.ToString(inv), t.Commission.ToString(inv), t.Swap.ToString(inv),
                    t.NetProfit.ToString(inv), t.MagicNumber.ToString(inv), Csv(t.Comment))).Append('\n');
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "trades.csv");
        });

        g.MapGet("/summary.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day") =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            if (!Reports.IsValidPeriod(period))
                return Results.BadRequest(new { error = "period must be day, week or month" });
            var tz = Reports.Tz(user);
            var trades = await Reports.Filtered(db, user, ids, from, to, magic, tz)
                .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("PeriodStart,NetProfit,TradeCount,Wins,WinRate\n");
            foreach (var grp in trades.GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, period)).OrderBy(x => x.Key))
            {
                var count = grp.Count();
                var wins = grp.Count(t => t.NetProfit > 0);
                var winRate = (100.0 * wins / count).ToString("0.0", inv);
                sb.Append(string.Create(inv,
                        $"{grp.Key:yyyy-MM-dd},{grp.Sum(t => t.NetProfit)},{count},{wins},{winRate}%"))
                    .Append('\n');
            }
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"summary-{period}.csv");
        });
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
