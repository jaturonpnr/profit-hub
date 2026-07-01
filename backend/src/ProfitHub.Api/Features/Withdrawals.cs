using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// Withdrawal calculator + log (see CONTEXT.md: Withdrawal Record). The plan endpoint
/// suggests each account's Net Profit over a period as the amount to withdraw; the CRUD
/// endpoints persist user-edited Withdrawal Records. Never affects Balance/ROI.
public static class Withdrawals
{
    public record CreateReq(Guid AccountId, decimal Amount, DateOnly? WithdrawnOn,
        decimal SuggestedAmount, DateOnly PeriodFrom, DateOnly PeriodTo, decimal Capital, string? Note);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/withdrawals").RequireAuthorization();

        g.MapGet("/plan", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to) =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            var tz = Reports.Tz(user);
            var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            var fromD = from is { } f ? DateOnly.FromDateTime(f) : new DateOnly(todayLocal.Year, todayLocal.Month, 1);
            var toD = to is { } t ? DateOnly.FromDateTime(t) : todayLocal;

            var accountsQ = db.Accounts.Where(a => a.UserId == user.UserId());
            if (ids.Length > 0) accountsQ = accountsQ.Where(a => ids.Contains(a.Id));
            var accs = await accountsQ.Select(a => new { a.Id, a.Name, a.AccountNumber }).ToListAsync();
            var myIds = accs.Select(a => a.Id).ToHashSet();

            var fromDt = DateTime.SpecifyKind(fromD.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var toDt = DateTime.SpecifyKind(toD.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var trades = await Reports.Filtered(db, user, ids, fromDt, toDt, null, tz)
                .Select(t => new { t.AccountId, t.NetProfit }).ToListAsync();
            var profitByAcc = trades.GroupBy(t => t.AccountId).ToDictionary(x => x.Key, x => x.Sum(v => v.NetProfit));

            var deposits = await db.BalanceOperations.Where(bo => myIds.Contains(bo.AccountId))
                .Select(bo => new { bo.AccountId, bo.Amount }).ToListAsync();
            var depByAcc = deposits.GroupBy(d => d.AccountId).ToDictionary(x => x.Key, x => x.Sum(v => v.Amount));

            var rows = accs.Select(a =>
            {
                var profit = profitByAcc.GetValueOrDefault(a.Id, 0m);
                return new
                {
                    accountId = a.Id,
                    name = !string.IsNullOrWhiteSpace(a.Name) ? a.Name : a.AccountNumber.ToString(),
                    capital = depByAcc.GetValueOrDefault(a.Id, 0m),
                    netProfit = profit,
                    suggestedAmount = Math.Max(0m, profit),
                    periodFrom = fromD,
                    periodTo = toD,
                };
            }).OrderBy(r => r.name).ToList();
            return Results.Ok(rows);
        });

        g.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var names = await db.Accounts.Where(a => a.UserId == user.UserId())
                .ToDictionaryAsync(a => a.Id, a => !string.IsNullOrWhiteSpace(a.Name) ? a.Name : a.AccountNumber.ToString());
            var ids = names.Keys.ToHashSet();
            var rows = await db.Withdrawals.Where(w => ids.Contains(w.AccountId))
                .OrderByDescending(w => w.WithdrawnOn).ThenByDescending(w => w.Id)
                .ToListAsync();
            return Results.Ok(rows.Select(w => new
            {
                id = w.Id, accountId = w.AccountId, accountName = names.GetValueOrDefault(w.AccountId, ""),
                amount = w.Amount, withdrawnOn = w.WithdrawnOn, suggestedAmount = w.SuggestedAmount,
                periodFrom = w.PeriodFrom, periodTo = w.PeriodTo, capital = w.Capital, note = w.Note,
            }));
        });

        g.MapPost("/", async (CreateReq req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (req.Amount <= 0) return Results.BadRequest(new { error = "amount must be > 0" });
            if (req.PeriodFrom > req.PeriodTo) return Results.BadRequest(new { error = "period from must be on or before to" });
            var owns = await db.Accounts.AnyAsync(a => a.Id == req.AccountId && a.UserId == user.UserId());
            if (!owns) return Results.BadRequest(new { error = "unknown account" });
            var w = new Withdrawal
            {
                AccountId = req.AccountId, Amount = req.Amount,
                WithdrawnOn = req.WithdrawnOn ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Reports.Tz(user))),
                SuggestedAmount = req.SuggestedAmount, PeriodFrom = req.PeriodFrom, PeriodTo = req.PeriodTo,
                Capital = req.Capital, Note = req.Note ?? "",
            };
            db.Withdrawals.Add(w);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = w.Id });
        });

        g.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var myIds = db.Accounts.Where(a => a.UserId == user.UserId()).Select(a => a.Id);
            var w = await db.Withdrawals.FirstOrDefaultAsync(x => x.Id == id && myIds.Contains(x.AccountId));
            if (w is null) return Results.NotFound();
            db.Withdrawals.Remove(w);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
