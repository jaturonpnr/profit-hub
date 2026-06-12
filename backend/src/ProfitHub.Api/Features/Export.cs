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
            var tz = Reports.Tz(user);
            var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
                .ToDictionaryAsync(a => a.Id, a => a.Name);
            var trades = await Reports.Filtered(db, user, accountIds, from, to, magic)
                .OrderBy(t => t.CloseTimeUtc).ToListAsync();
            var sb = new StringBuilder("CloseTime,Account,Symbol,Direction,Lots,OpenPrice,ClosePrice,GrossProfit,Commission,Swap,NetProfit,MagicNumber,Comment\n");
            foreach (var t in trades)
                sb.AppendLine(string.Join(',',
                    TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz).ToString("yyyy-MM-dd HH:mm:ss"),
                    Csv(accounts.GetValueOrDefault(t.AccountId, "")), Csv(t.Symbol), t.Direction, t.Lots,
                    t.OpenPrice, t.ClosePrice, t.GrossProfit, t.Commission, t.Swap, t.NetProfit,
                    t.MagicNumber, Csv(t.Comment)));
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "trades.csv");
        });

        g.MapGet("/summary.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day") =>
        {
            var tz = Reports.Tz(user);
            var trades = await Reports.Filtered(db, user, accountIds, from, to, magic)
                .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
            var sb = new StringBuilder("PeriodStart,NetProfit,TradeCount,Wins,WinRate\n");
            foreach (var grp in trades.GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, period)).OrderBy(x => x.Key))
            {
                var wins = grp.Count(t => t.NetProfit > 0);
                sb.AppendLine($"{grp.Key:yyyy-MM-dd},{grp.Sum(t => t.NetProfit)},{grp.Count()},{wins},{(double)wins / grp.Count():P1}");
            }
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"summary-{period}.csv");
        });
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
