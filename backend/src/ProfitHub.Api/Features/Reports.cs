using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Reports
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();
        g.MapGet("/trades", GetTrades);
        g.MapGet("/summary", GetSummary);
        g.MapGet("/summary/by-ea", GetByEa);
    }

    internal static IQueryable<Trade> Filtered(AppDbContext db, ClaimsPrincipal user,
        string? accountIds, DateTime? from, DateTime? to, long? magic)
    {
        var myAccounts = db.Accounts.Where(a => a.UserId == user.UserId()).Select(a => a.Id);
        var q = db.Trades.Where(t => myAccounts.Contains(t.AccountId));
        if (!string.IsNullOrEmpty(accountIds))
        {
            var ids = accountIds.Split(',').Select(Guid.Parse).ToArray();
            q = q.Where(t => ids.Contains(t.AccountId));
        }
        if (from is not null) q = q.Where(t => t.CloseTimeUtc >= from);
        if (to is not null) q = q.Where(t => t.CloseTimeUtc < to);
        if (magic is not null) q = q.Where(t => t.MagicNumber == magic);
        return q;
    }

    internal static TimeZoneInfo Tz(ClaimsPrincipal user) =>
        TimeZoneInfo.FindSystemTimeZoneById(user.FindFirstValue("tz") ?? "Asia/Bangkok");

    internal static DateOnly Bucket(DateTime closeUtc, TimeZoneInfo tz, string period)
    {
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(closeUtc, tz));
        return period switch
        {
            "week" => local.AddDays(-(((int)local.DayOfWeek + 6) % 7)), // Monday start
            "month" => new DateOnly(local.Year, local.Month, 1),
            _ => local,
        };
    }

    private static async Task<IResult> GetTrades(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to, long? magic, int page = 1, int pageSize = 50)
    {
        var q = Filtered(db, user, accountIds, from, to, magic).OrderByDescending(t => t.CloseTimeUtc);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Results.Ok(new { total, items });
    }

    private static async Task<IResult> GetSummary(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day")
    {
        var tz = Tz(user);
        var trades = await Filtered(db, user, accountIds, from, to, magic)
            .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
        var rows = trades
            .GroupBy(t => Bucket(t.CloseTimeUtc, tz, period))
            .OrderByDescending(grp => grp.Key)
            .Select(grp => new
            {
                periodStart = grp.Key,
                netProfit = grp.Sum(t => t.NetProfit),
                tradeCount = grp.Count(),
                wins = grp.Count(t => t.NetProfit > 0)
            });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetByEa(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to)
    {
        var names = await db.EaNames.Where(e => e.UserId == user.UserId())
            .ToDictionaryAsync(e => e.MagicNumber, e => e.Name);
        var trades = await Filtered(db, user, accountIds, from, to, null)
            .Select(t => new { t.MagicNumber, t.NetProfit }).ToListAsync();
        var rows = trades.GroupBy(t => t.MagicNumber)
            .Select(grp => new
            {
                magicNumber = grp.Key,
                name = names.GetValueOrDefault(grp.Key, grp.Key.ToString()),
                netProfit = grp.Sum(t => t.NetProfit),
                tradeCount = grp.Count()
            })
            .OrderByDescending(r => r.netProfit);
        return Results.Ok(rows);
    }
}
