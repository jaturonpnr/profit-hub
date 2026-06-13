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

    internal static bool TryParseAccountIds(string? accountIds, out Guid[] ids)
    {
        ids = [];
        if (string.IsNullOrEmpty(accountIds)) return true;
        var parts = accountIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parsed = new Guid[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!Guid.TryParse(parts[i], out parsed[i])) return false;
        ids = parsed;
        return true;
    }

    /// <summary>
    /// Interprets from/to in the user's timezone. A bare date (Kind.Unspecified, midnight)
    /// means the whole local day: `from` is the start of that day, `to` is exclusive of the
    /// start of the NEXT day, so a date range includes the entire `to` day.
    /// Results always have DateTimeKind.Utc (required for Npgsql timestamptz).
    /// </summary>
    internal static (DateTime? FromUtc, DateTime? ToUtc) NormalizeRange(DateTime? from, DateTime? to, TimeZoneInfo tz)
    {
        DateTime? fromUtc = from switch
        {
            null => null,
            { Kind: DateTimeKind.Unspecified } f => TimeZoneInfo.ConvertTimeToUtc(f, tz),
            { } f => f.ToUniversalTime(),
        };
        DateTime? toUtc = to switch
        {
            null => null,
            { Kind: DateTimeKind.Unspecified } t when t.TimeOfDay == TimeSpan.Zero
                => TimeZoneInfo.ConvertTimeToUtc(t.AddDays(1), tz), // exclusive end: next local day
            { Kind: DateTimeKind.Unspecified } t => TimeZoneInfo.ConvertTimeToUtc(t, tz),
            { } t => t.ToUniversalTime(),
        };
        return (fromUtc is { } f2 ? DateTime.SpecifyKind(f2, DateTimeKind.Utc) : null,
                toUtc is { } t2 ? DateTime.SpecifyKind(t2, DateTimeKind.Utc) : null);
    }

    internal static IQueryable<Trade> Filtered(AppDbContext db, ClaimsPrincipal user,
        Guid[] accountIds, DateTime? from, DateTime? to, long? magic, TimeZoneInfo tz)
    {
        var myAccounts = db.Accounts.Where(a => a.UserId == user.UserId()).Select(a => a.Id);
        var q = db.Trades.Where(t => myAccounts.Contains(t.AccountId));
        if (accountIds.Length > 0) q = q.Where(t => accountIds.Contains(t.AccountId));
        var (fromUtc, toUtc) = NormalizeRange(from, to, tz);
        if (fromUtc is not null) q = q.Where(t => t.CloseTimeUtc >= fromUtc);
        if (toUtc is not null) q = q.Where(t => t.CloseTimeUtc < toUtc);
        if (magic is not null) q = q.Where(t => t.MagicNumber == magic);
        return q;
    }

    internal static TimeZoneInfo Tz(ClaimsPrincipal user)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(user.FindFirstValue("tz") ?? "Asia/Bangkok");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
        }
    }

    internal static bool IsValidPeriod(string period) => period is "day" or "week" or "month";

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
        if (!TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var q = Filtered(db, user, ids, from, to, magic, Tz(user)).OrderByDescending(t => t.CloseTimeUtc);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Results.Ok(new { total, items });
    }

    private static async Task<IResult> GetSummary(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day")
    {
        if (!TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        if (!IsValidPeriod(period))
            return Results.BadRequest(new { error = "period must be day, week or month" });
        var tz = Tz(user);
        var trades = await Filtered(db, user, ids, from, to, magic, tz)
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
        if (!TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        var names = await db.EaNames.Where(e => e.UserId == user.UserId())
            .ToDictionaryAsync(e => e.MagicNumber, e => e.Name);
        var trades = await Filtered(db, user, ids, from, to, null, Tz(user))
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
