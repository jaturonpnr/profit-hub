using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class ExecutionsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    [Fact]
    public async Task Posting_execution_matches_trade_by_closing_order()
    {
        var key = Guid.NewGuid().ToString();
        Guid accId;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x" };
            var acc = new Account { UserId = user.Id, AccountNumber = 27505156, IngestKey = key };
            db.AddRange(user, acc);
            db.Trades.Add(new Trade
            {
                AccountId = acc.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy",
                MagicNumber = 1, ClosingOrderTicket = 61730462,
                CloseTimeUtc = DateTime.SpecifyKind(new DateTime(2026, 6, 19, 1, 35, 0), DateTimeKind.Utc),
            });
            db.SaveChanges();
            accId = acc.Id;
        }

        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", key);
        var resp = await client.PostAsJsonAsync("/api/ingest/executions", new
        {
            items = new[] { new { orderTicket = 61730462L, executionMs = 513.694m },
                            new { orderTicket = 999L, executionMs = 100m } }
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["matched"]);

        using var s2 = _f.Services.CreateScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(513.694m, db2.Trades.Single(t => t.AccountId == accId).ExecutionMs);
    }

    [Fact]
    public async Task Bad_key_is_unauthorized()
    {
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", "nope");
        var resp = await client.PostAsJsonAsync("/api/ingest/executions",
            new { items = new[] { new { orderTicket = 1L, executionMs = 1m } } });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // Security: posting with account A's key must never touch account B's trade,
    // even when both have the same ClosingOrderTicket.
    [Fact]
    public async Task Cannot_update_another_accounts_trade_with_same_order_ticket()
    {
        var keyA = Guid.NewGuid().ToString();
        var keyB = Guid.NewGuid().ToString();
        Guid bTradeAcc;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userA = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x" };
            var userB = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x" };
            var accA = new Account { UserId = userA.Id, AccountNumber = 111, IngestKey = keyA };
            var accB = new Account { UserId = userB.Id, AccountNumber = 222, IngestKey = keyB };
            db.AddRange(userA, userB, accA, accB);
            db.Trades.Add(new Trade { AccountId = accA.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy", ClosingOrderTicket = 5000 });
            db.Trades.Add(new Trade { AccountId = accB.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy", ClosingOrderTicket = 5000 });
            db.SaveChanges();
            bTradeAcc = accB.Id;
        }

        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", keyA);
        var resp = await client.PostAsJsonAsync("/api/ingest/executions",
            new { items = new[] { new { orderTicket = 5000L, executionMs = 250m } } });
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["matched"]); // only A's trade

        using var s2 = _f.Services.CreateScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(db2.Trades.Single(t => t.AccountId == bTradeAcc).ExecutionMs); // B untouched
    }
}
