using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class IngestTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private (HttpClient client, Account acc) Setup()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x" };
        var acc = new Account { UserId = user.Id, AccountNumber = 111, IngestKey = Guid.NewGuid().ToString("N") };
        db.AddRange(user, acc); db.SaveChanges();
        return (_f.CreateClient(), acc);
    }

    private static object Deal(long ticket, decimal profit = 10m, string type = "buy") => new
    {
        dealTicket = ticket, positionId = ticket, symbol = "XAUUSD.PRO", type,
        lots = 0.26m, openPrice = 4499.46m, closePrice = 4500.02m,
        openTimeUtc = "2026-05-28T03:00:00Z", closeTimeUtc = "2026-05-28T03:06:00Z",
        grossProfit = profit, commission = -1.2m, swap = 0m, magicNumber = 20231, comment = "QQ"
    };

    [Fact]
    public async Task Resending_same_deals_does_not_duplicate()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        var payload = new { deals = new[] { Deal(1), Deal(2) } };
        await client.PostAsJsonAsync("/api/ingest/deals", payload);
        var second = await client.PostAsJsonAsync("/api/ingest/deals", payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, db.Trades.Count(t => t.AccountId == acc.Id));
    }

    [Fact]
    public async Task Net_profit_is_gross_plus_commission_plus_swap()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        await client.PostAsJsonAsync("/api/ingest/deals", new { deals = new[] { Deal(10, profit: 12.69m) } });
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(12.69m + -1.2m + 0m, db.Trades.Single(t => t.AccountId == acc.Id).NetProfit);
    }

    [Fact]
    public async Task Balance_deals_go_to_balance_operations()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        await client.PostAsJsonAsync("/api/ingest/deals", new { deals = new[] { Deal(20, profit: 500m, type: "balance") } });
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, db.Trades.Count(t => t.AccountId == acc.Id));
        Assert.Equal(500m, db.BalanceOperations.Single(b => b.AccountId == acc.Id).Amount);
    }

    [Fact]
    public async Task Duplicate_ticket_within_one_batch_stores_one_trade()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        var res = await client.PostAsJsonAsync("/api/ingest/deals", new { deals = new[] { Deal(30), Deal(30) } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.Trades.Count(t => t.AccountId == acc.Id));
    }

    [Fact]
    public async Task Unknown_type_is_skipped_and_reported()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        var res = await client.PostAsJsonAsync("/api/ingest/deals", new { deals = new[] { Deal(40, type: "credit") } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["skipped"]);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, db.Trades.Count(t => t.AccountId == acc.Id));
        Assert.Equal(0, db.BalanceOperations.Count(b => b.AccountId == acc.Id));
    }

    [Fact]
    public async Task Bad_key_is_401()
    {
        var (client, _) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", "nope");
        var res = await client.PostAsJsonAsync("/api/ingest/deals", new { deals = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
