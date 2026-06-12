using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class ReportsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record SummaryRow(DateOnly PeriodStart, decimal NetProfit, int TradeCount, int Wins);

    private async Task<(HttpClient client, Guid accId)> SeedAsync(params (long ticket, string closeUtc, decimal net)[] trades)
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 111, name = "A", broker = "" });
        var acc = await res.Content.ReadFromJsonAsync<AccountsTests.AccountDto>();
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (ticket, closeUtc, net) in trades)
            db.Trades.Add(new Trade
            {
                AccountId = acc!.Id, DealTicket = ticket, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = DateTime.Parse(closeUtc).ToUniversalTime(),
                GrossProfit = net, NetProfit = net, MagicNumber = 1
            });
        await db.SaveChangesAsync();
        return (client, acc!.Id);
    }

    [Fact]
    public async Task Trade_closing_after_midnight_bangkok_lands_on_the_next_day()
    {
        // 2026-05-27 17:30 UTC = 2026-05-28 00:30 Asia/Bangkok
        var (client, accId) = await SeedAsync(
            (1, "2026-05-27T17:30:00Z", 10m),
            (2, "2026-05-27T16:30:00Z", 5m)); // 23:30 Bangkok, still 27th
        var rows = await client.GetFromJsonAsync<SummaryRow[]>($"/api/summary?period=day&accountIds={accId}");
        Assert.Equal(2, rows!.Length);
        Assert.Contains(rows, r => r.PeriodStart == new DateOnly(2026, 5, 28) && r.NetProfit == 10m);
        Assert.Contains(rows, r => r.PeriodStart == new DateOnly(2026, 5, 27) && r.NetProfit == 5m);
    }

    [Fact]
    public async Task Monthly_summary_aggregates_and_counts_wins()
    {
        var (client, accId) = await SeedAsync(
            (1, "2026-05-10T10:00:00Z", 10m), (2, "2026-05-11T10:00:00Z", -4m), (3, "2026-06-01T10:00:00Z", 7m));
        var rows = await client.GetFromJsonAsync<SummaryRow[]>($"/api/summary?period=month&accountIds={accId}");
        var may = rows!.Single(r => r.PeriodStart == new DateOnly(2026, 5, 1));
        Assert.Equal(6m, may.NetProfit);
        Assert.Equal(2, may.TradeCount);
        Assert.Equal(1, may.Wins);
    }

    [Fact]
    public async Task Trades_endpoint_filters_by_account()
    {
        var (client, accId) = await SeedAsync((1, "2026-05-10T10:00:00Z", 10m));
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/trades?accountIds={accId}");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
    }
}
