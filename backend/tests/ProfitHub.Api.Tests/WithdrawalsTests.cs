using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class WithdrawalsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private async Task<(HttpClient client, Guid accountId)> SeedAccount()
    {
        var (client, email) = await AuthedClient.CreateWithEmail(_f);
        Guid accId;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            var acc = new Account { UserId = user.Id, AccountNumber = 555, IngestKey = Guid.NewGuid().ToString() };
            db.Accounts.Add(acc);
            // capital: a +5000 deposit
            db.BalanceOperations.Add(new BalanceOperation { AccountId = acc.Id, DealTicket = 1, Amount = 5000m, TimeUtc = DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Utc) });
            db.SaveChanges();
            accId = acc.Id;
        }
        return (client, accId);
    }

    private void AddTrade(Guid accountId, decimal net, DateTime closeUtc)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Trades.Add(new Trade
        {
            AccountId = accountId, DealTicket = Random.Shared.NextInt64(1, long.MaxValue),
            Symbol = "XAUUSD", Direction = "buy", NetProfit = net, GrossProfit = net, MagicNumber = 1,
            CloseTimeUtc = DateTime.SpecifyKind(closeUtc, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Plan_suggests_period_net_profit_and_capital()
    {
        var (client, acc) = await SeedAccount();
        AddTrade(acc, 300m, new DateTime(2026, 6, 10, 10, 0, 0));
        AddTrade(acc, -50m, new DateTime(2026, 6, 12, 10, 0, 0));

        var rows = await client.GetFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>(
            "/api/withdrawals/plan?from=2026-06-01&to=2026-06-30");
        var r = rows!.Single();
        Assert.Equal(5000m, r["capital"].GetDecimal());
        Assert.Equal(250m, r["suggestedAmount"].GetDecimal());   // 300 - 50
    }

    [Fact]
    public async Task Create_list_delete_roundtrip()
    {
        var (client, acc) = await SeedAccount();
        var create = await client.PostAsJsonAsync("/api/withdrawals", new
        {
            accountId = acc, amount = 900m, withdrawnOn = "2026-06-26",
            suggestedAmount = 1000m, periodFrom = "2026-06-01", periodTo = "2026-06-26",
            capital = 5000m, note = "june"
        });
        create.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>("/api/withdrawals");
        Assert.Single(list!);
        var id = list![0]["id"].GetInt64();
        Assert.Equal(900m, list[0]["amount"].GetDecimal());

        var del = await client.DeleteAsync($"/api/withdrawals/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<object>>("/api/withdrawals"))!);
    }

    [Fact]
    public async Task Create_rejects_non_positive_amount()
    {
        var (client, acc) = await SeedAccount();
        var resp = await client.PostAsJsonAsync("/api/withdrawals", new
        {
            accountId = acc, amount = 0m, suggestedAmount = 0m,
            periodFrom = "2026-06-01", periodTo = "2026-06-26", capital = 5000m
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cannot_delete_another_users_withdrawal()
    {
        var (alice, acc) = await SeedAccount();
        var c = await alice.PostAsJsonAsync("/api/withdrawals", new
        {
            accountId = acc, amount = 100m, suggestedAmount = 100m,
            periodFrom = "2026-06-01", periodTo = "2026-06-26", capital = 5000m
        });
        var id = (await c.Content.ReadFromJsonAsync<Dictionary<string, long>>())!["id"];
        var bob = await AuthedClient.Create(_f);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/withdrawals/{id}")).StatusCode);
    }
}
