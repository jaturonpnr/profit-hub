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
            var existingTrades = (await db.Trades.Where(t => t.AccountId == acc.Id && tickets.Contains(t.DealTicket))
                .Select(t => t.DealTicket).ToListAsync()).ToHashSet();
            var existingBalOps = (await db.BalanceOperations.Where(b => b.AccountId == acc.Id && tickets.Contains(b.DealTicket))
                .Select(b => b.DealTicket).ToListAsync()).ToHashSet();

            var skipped = 0;
            foreach (var d in batch.Deals)
            {
                if (d.Type == "balance")
                {
                    if (existingBalOps.Contains(d.DealTicket)) continue;
                    db.BalanceOperations.Add(new BalanceOperation
                    {
                        AccountId = acc.Id, DealTicket = d.DealTicket, Amount = d.GrossProfit,
                        TimeUtc = DateTime.SpecifyKind(d.CloseTimeUtc, DateTimeKind.Utc), Comment = d.Comment ?? ""
                    });
                    existingBalOps.Add(d.DealTicket);
                }
                else if (d.Type is "buy" or "sell")
                {
                    if (existingTrades.Contains(d.DealTicket)) continue;
                    db.Trades.Add(new Trade
                    {
                        AccountId = acc.Id, DealTicket = d.DealTicket, PositionId = d.PositionId,
                        Symbol = d.Symbol, Direction = d.Type, Lots = d.Lots,
                        OpenPrice = d.OpenPrice, ClosePrice = d.ClosePrice,
                        OpenTimeUtc = DateTime.SpecifyKind(d.OpenTimeUtc, DateTimeKind.Utc),
                        CloseTimeUtc = DateTime.SpecifyKind(d.CloseTimeUtc, DateTimeKind.Utc),
                        GrossProfit = d.GrossProfit, Commission = d.Commission, Swap = d.Swap,
                        NetProfit = d.GrossProfit + d.Commission + d.Swap,
                        MagicNumber = d.MagicNumber, Comment = d.Comment ?? ""
                    });
                    existingTrades.Add(d.DealTicket);
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
