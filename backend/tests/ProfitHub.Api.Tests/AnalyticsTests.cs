using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class AnalyticsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private async Task<(HttpClient client, Guid accountId)> SeedAccount()
    {
        var (client, email) = await AuthedClient.CreateWithEmail(_f);
        Guid accountId;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            var acc = new Account { UserId = user.Id, AccountNumber = 555, IngestKey = Guid.NewGuid().ToString() };
            db.Accounts.Add(acc);
            db.SaveChanges();
            accountId = acc.Id;
        }
        return (client, accountId);
    }

    private void AddTrade(Guid accountId, long magic, decimal net, DateTime closeUtc, decimal commission = 0, decimal swap = 0, int? executionMs = null)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Trades.Add(new Trade
        {
            AccountId = accountId, DealTicket = Random.Shared.NextInt64(1, long.MaxValue),
            Symbol = "XAUUSD", Direction = net >= 0 ? "buy" : "sell",
            NetProfit = net, GrossProfit = net, Commission = commission, Swap = swap,
            MagicNumber = magic, CloseTimeUtc = DateTime.SpecifyKind(closeUtc, DateTimeKind.Utc),
            ExecutionMs = executionMs,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Heatmap_buckets_net_profit_by_year_month()
    {
        var (client, acc) = await SeedAccount();
        AddTrade(acc, 1, 100m, new DateTime(2026, 1, 15, 10, 0, 0));
        AddTrade(acc, 1, 50m, new DateTime(2026, 1, 20, 10, 0, 0));
        AddTrade(acc, 1, -30m, new DateTime(2026, 3, 5, 10, 0, 0));

        var cells = await client.GetFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>("/api/heatmap");
        Assert.Equal(2, cells!.Count); // Jan + Mar
        var jan = cells.First(c => c["month"].GetInt32() == 1);
        Assert.Equal(150m, jan["netProfit"].GetDecimal());
    }

    [Fact]
    public async Task Ea_detail_returns_summary_and_404_for_unknown()
    {
        var (client, acc) = await SeedAccount();
        AddTrade(acc, 7, 100m, new DateTime(2026, 1, 1, 9, 0, 0));
        AddTrade(acc, 7, -40m, new DateTime(2026, 1, 2, 14, 0, 0));

        var detail = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/eas/7");
        Assert.Equal(60m, detail!["netProfit"].GetDecimal());
        Assert.Equal(2, detail["tradeCount"].GetInt32());
        Assert.True(detail["equityCurve"].GetArrayLength() == 2);

        var missing = await client.GetAsync("/api/eas/999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Ea_detail_cannot_read_another_users_ea()
    {
        var (alice, acc) = await SeedAccount();
        AddTrade(acc, 42, 10m, new DateTime(2026, 1, 1, 9, 0, 0));
        var bob = await AuthedClient.Create(_f);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await bob.GetAsync("/api/eas/42")).StatusCode);
    }

    [Fact]
    public async Task Eas_endpoint_returns_metrics()
    {
        var (client, acc) = await SeedAccount();
        AddTrade(acc, 100, 100m, new DateTime(2026, 1, 1, 10, 0, 0));
        AddTrade(acc, 100, -40m, new DateTime(2026, 1, 2, 10, 0, 0));
        AddTrade(acc, 100, 60m, new DateTime(2026, 1, 3, 10, 0, 0), commission: -2m, swap: -1m);

        var eas = await client.GetFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>("/api/eas");
        Assert.Single(eas!);
        var ea = eas![0];
        Assert.Equal("100", ea["magicNumber"].ToString());
        Assert.Equal(120m, ea["netProfit"].GetDecimal());
        Assert.Equal(3, ea["tradeCount"].GetInt32());
        Assert.Equal(4.00m, ea["profitFactor"].GetDecimal()); // (100+60)/40
        Assert.Equal(40m, ea["drawdownAmount"].GetDecimal()); // 100 -> 60
    }

    [Fact]
    public async Task Ea_endpoints_expose_execution_metrics()
    {
        var (client, acc) = await SeedAccount();
        AddTrade(acc, 5, 100m, new DateTime(2026, 1, 1, 9, 0, 0), executionMs: 400);
        AddTrade(acc, 5, -50m, new DateTime(2026, 1, 2, 9, 0, 0), executionMs: 600);

        var eas = await client.GetFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>("/api/eas");
        Assert.Equal(500, eas!.Single()["avgExecutionMs"].GetInt32());

        var detail = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/eas/5");
        Assert.Equal(500, detail!["avgExecutionMs"].GetInt32());
        Assert.Equal(600, detail["maxExecutionMs"].GetInt32());
    }
}
