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
        g.MapGet("/summary/by-account", GetByAccount);
        g.MapGet("/balances", GetBalances);
        g.MapGet("/eas", GetEas);
    }

    // Lifetime Balance & ROI per account derived from deposits/withdrawals + realized P/L.
    // NOT date-filtered: respects only the account filter and user ownership.
    //   netDeposits = Σ BalanceOperations.Amount (deposits +, withdrawals −)
    //   netProfit   = Σ Trades.NetProfit
    //   balance     = netDeposits + netProfit (mirrors MT5 Balance)
    //   roi         = netDeposits > 0 ? netProfit/netDeposits*100 : null ("—" in UI)
    private static async Task<IResult> GetBalances(ClaimsPrincipal user, AppDbContext db, string? accountIds)
    {
        if (!TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        var accountsQ = db.Accounts.Where(a => a.UserId == user.UserId());
        if (ids.Length > 0) accountsQ = accountsQ.Where(a => ids.Contains(a.Id));
        var accounts = await accountsQ
            .Select(a => new { a.Id, a.Name, a.AccountNumber }).ToListAsync();
        var myIds = accounts.Select(a => a.Id).ToHashSet();
        var deposits = await db.BalanceOperations.Where(b => myIds.Contains(b.AccountId))
            .Select(b => new { b.AccountId, b.Amount }).ToListAsync();
        var trades = await db.Trades.Where(t => myIds.Contains(t.AccountId))
            .Select(t => new { t.AccountId, t.NetProfit }).ToListAsync();
        var depByAcc = deposits.GroupBy(d => d.AccountId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var profByAcc = trades.GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => g.Sum(x => x.NetProfit));
        var rows = accounts
            .Select(a =>
            {
                var netDeposits = depByAcc.GetValueOrDefault(a.Id, 0m);
                var netProfit = profByAcc.GetValueOrDefault(a.Id, 0m);
                var name = !string.IsNullOrWhiteSpace(a.Name) ? a.Name : a.AccountNumber.ToString();
                return new
                {
                    accountId = a.Id,
                    name,
                    accountNumber = a.AccountNumber,
                    netDeposits,
                    netProfit,
                    balance = netDeposits + netProfit,
                    roi = netDeposits > 0 ? (decimal?)Math.Round(netProfit / netDeposits * 100, 2) : null
                };
            })
            .OrderByDescending(r => r.balance)
            .ToList();
        return Results.Ok(rows);
    }

    // Lists every EA (magic number) found in the user's trades, with its owning account
    // and lifetime stats. Used by the EAs management page (rename via PUT /api/ea-names).
    private static async Task<IResult> GetEas(ClaimsPrincipal user, AppDbContext db)
    {
        var names = await db.EaNames.Where(e => e.UserId == user.UserId())
            .ToDictionaryAsync(e => e.MagicNumber, e => e.Name);
        var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
            .ToDictionaryAsync(a => a.Id, a => new { a.Name, a.AccountNumber });
        var myAccountIds = accounts.Keys.ToHashSet();
        var trades = await db.Trades.Where(t => myAccountIds.Contains(t.AccountId))
            .Select(t => new { t.MagicNumber, t.AccountId, t.NetProfit }).ToListAsync();
        var rows = trades.GroupBy(t => t.MagicNumber)
            .Select(grp =>
            {
                var distinctAccounts = grp.Select(t => t.AccountId).Distinct().Count();
                var acc = accounts.GetValueOrDefault(grp.First().AccountId);
                var accountName = distinctAccounts > 1
                    ? "Multiple"
                    : (acc is not null && !string.IsNullOrWhiteSpace(acc.Name) ? acc.Name : acc?.AccountNumber.ToString() ?? "");
                return new
                {
                    magicNumber = grp.Key,
                    name = names.GetValueOrDefault(grp.Key, ""), // empty = unnamed; frontend shows magic as placeholder
                    accountName,
                    netProfit = grp.Sum(t => t.NetProfit),
                    tradeCount = grp.Count()
                };
            })
            .OrderByDescending(r => r.netProfit);
        return Results.Ok(rows);
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

    private static async Task<IResult> GetByAccount(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to)
    {
        if (!TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });
        var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
            .ToDictionaryAsync(a => a.Id, a => new { a.Name, a.AccountNumber });
        var trades = await Filtered(db, user, ids, from, to, null, Tz(user))
            .Select(t => new { t.AccountId, t.NetProfit }).ToListAsync();
        var rows = trades.GroupBy(t => t.AccountId)
            .Select(grp =>
            {
                var acc = accounts.GetValueOrDefault(grp.Key);
                var name = acc is not null && !string.IsNullOrWhiteSpace(acc.Name)
                    ? acc.Name : acc?.AccountNumber.ToString() ?? grp.Key.ToString();
                return new
                {
                    accountId = grp.Key,
                    name,
                    accountNumber = acc?.AccountNumber ?? 0,
                    netProfit = grp.Sum(t => t.NetProfit),
                    tradeCount = grp.Count()
                };
            })
            .OrderByDescending(r => r.netProfit);
        return Results.Ok(rows);
    }
}
