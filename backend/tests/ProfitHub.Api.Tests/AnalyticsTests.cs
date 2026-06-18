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

    private void AddTrade(Guid accountId, long magic, decimal net, DateTime closeUtc, decimal commission = 0, decimal swap = 0)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Trades.Add(new Trade
        {
            AccountId = accountId, DealTicket = Random.Shared.NextInt64(1, long.MaxValue),
            Symbol = "XAUUSD", Direction = net >= 0 ? "buy" : "sell",
            NetProfit = net, GrossProfit = net, Commission = commission, Swap = swap,
            MagicNumber = magic, CloseTimeUtc = DateTime.SpecifyKind(closeUtc, DateTimeKind.Utc),
        });
        db.SaveChanges();
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
}
