using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Ingest
{
    public record DealDto(long DealTicket, long PositionId, string Symbol, string Type,
        decimal Lots, decimal OpenPrice, decimal ClosePrice,
        DateTime OpenTimeUtc, DateTime CloseTimeUtc,
        decimal GrossProfit, decimal Commission, decimal Swap, long MagicNumber, string? Comment);
    public record Batch(DealDto[] Deals);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/ingest/deals", async (HttpRequest req, Batch batch, AppDbContext db) =>
        {
            if (batch?.Deals is null) return Results.BadRequest(new { error = "deals required" });
            if (batch.Deals.Length > 1000) return Results.BadRequest(new { error = "too many deals (max 1000)" });

            var key = req.Headers["X-Ingest-Key"].ToString();
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.IngestKey == key);
            if (acc is null) return Results.Unauthorized();

            var tickets = batch.Deals.Select(d => d.DealTicket).ToArray();
            // Load existing rows as entities so re-sent deals UPDATE their financial fields
            // (e.g. a corrected commission after an EA fix) instead of being skipped. This
            // makes a ForceBackfill self-healing rather than a no-op for already-stored deals.
            var existingTrades = await db.Trades.Where(t => t.AccountId == acc.Id && tickets.Contains(t.DealTicket))
                .ToDictionaryAsync(t => t.DealTicket);
            var existingBalOps = await db.BalanceOperations.Where(b => b.AccountId == acc.Id && tickets.Contains(b.DealTicket))
                .ToDictionaryAsync(b => b.DealTicket);

            var skipped = 0;
            foreach (var d in batch.Deals)
            {
                if (d.Type == "balance")
                {
                    if (!existingBalOps.TryGetValue(d.DealTicket, out var bal))
                    {
                        bal = new BalanceOperation { AccountId = acc.Id, DealTicket = d.DealTicket };
                        db.BalanceOperations.Add(bal);
                        existingBalOps[d.DealTicket] = bal;
                    }
                    bal.Amount = d.GrossProfit;
                    bal.TimeUtc = DateTime.SpecifyKind(d.CloseTimeUtc, DateTimeKind.Utc);
                    bal.Comment = d.Comment ?? "";
                }
                else if (d.Type is "buy" or "sell")
                {
                    if (!existingTrades.TryGetValue(d.DealTicket, out var trade))
                    {
                        trade = new Trade { AccountId = acc.Id, DealTicket = d.DealTicket, Symbol = d.Symbol, Direction = d.Type };
                        db.Trades.Add(trade);
                        existingTrades[d.DealTicket] = trade;
                    }
                    trade.PositionId = d.PositionId;
                    trade.Symbol = d.Symbol; trade.Direction = d.Type; trade.Lots = d.Lots;
                    trade.OpenPrice = d.OpenPrice; trade.ClosePrice = d.ClosePrice;
                    trade.OpenTimeUtc = DateTime.SpecifyKind(d.OpenTimeUtc, DateTimeKind.Utc);
                    trade.CloseTimeUtc = DateTime.SpecifyKind(d.CloseTimeUtc, DateTimeKind.Utc);
                    trade.GrossProfit = d.GrossProfit; trade.Commission = d.Commission; trade.Swap = d.Swap;
                    trade.NetProfit = d.GrossProfit + d.Commission + d.Swap;
                    trade.MagicNumber = d.MagicNumber; trade.Comment = d.Comment ?? "";
                }
                else
                {
                    skipped++;
                }
            }
            acc.LastIngestAtUtc = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict();
            }
            return Results.Ok(new { received = batch.Deals.Length, skipped });
        });
    }
}
