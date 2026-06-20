using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// Third ingestion path (ADR 0005): the Execution Sidecar posts journal execution
/// times parsed from the MT5 log. Authenticated with the account's Ingest Key.
/// Matches each item by ClosingOrderTicket within that account and sets ExecutionMs.
/// Idempotent — re-posting the same order just re-sets the same value.
public static class Executions
{
    public record Item(long OrderTicket, decimal ExecutionMs);
    public record Batch(Item[] Items);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/ingest/executions", async (HttpRequest req, Batch batch, AppDbContext db) =>
        {
            if (batch?.Items is null) return Results.BadRequest(new { error = "items required" });
            if (batch.Items.Length > 5000) return Results.BadRequest(new { error = "too many items (max 5000)" });

            var key = req.Headers["X-Ingest-Key"].ToString();
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.IngestKey == key);
            if (acc is null) return Results.Unauthorized();

            // Accept only sane values: a real fill is > 0 ms, and must fit numeric(9,3)
            // (< 1,000,000 ms ≈ 16 min) — a log-parse glitch that produces 0 or a huge
            // number is dropped rather than skewing averages or overflowing the column.
            var byOrder = new Dictionary<long, decimal>();
            foreach (var it in batch.Items)
                if (it.OrderTicket > 0 && it.ExecutionMs > 0 && it.ExecutionMs < 1_000_000m)
                    byOrder[it.OrderTicket] = it.ExecutionMs;

            var orderIds = byOrder.Keys.ToArray();
            var trades = await db.Trades
                .Where(t => t.AccountId == acc.Id && t.ClosingOrderTicket != null
                            && orderIds.Contains(t.ClosingOrderTicket!.Value))
                .ToListAsync();

            var matched = 0;
            foreach (var t in trades)
                if (byOrder.TryGetValue(t.ClosingOrderTicket!.Value, out var ms))
                {
                    t.ExecutionMs = ms;
                    matched++;
                }

            if (matched > 0) await db.SaveChangesAsync();
            return Results.Ok(new { received = batch.Items.Length, matched });
        }).DisableAntiforgery();
    }
}
