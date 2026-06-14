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

    [Fact]
    public async Task Bare_date_range_is_inclusive_of_the_to_day_in_user_timezone()
    {
        // Three consecutive Bangkok (UTC+7) days:
        var (client, accId) = await SeedAsync(
            (1, "2026-06-01T03:00:00Z", 10m),  // day1: 2026-06-01 10:00 Bangkok
            (2, "2026-06-02T16:30:00Z", 5m),   // day2: 2026-06-02 23:30 Bangkok (late on the `to` day)
            (3, "2026-06-02T17:30:00Z", 7m));  // day3: 2026-06-03 00:30 Bangkok (must be excluded)
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/trades?accountIds={accId}&from=2026-06-01&to=2026-06-02");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        var tickets = page.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("dealTicket").GetInt64()).ToArray();
        Assert.Contains(1L, tickets);
        Assert.Contains(2L, tickets);
        Assert.DoesNotContain(3L, tickets);
    }

    public record AccountRow(Guid AccountId, string Name, long AccountNumber, decimal NetProfit, int TradeCount);

    [Fact]
    public async Task ByAccount_groups_all_magics_under_one_account_row()
    {
        // SeedAsync makes account "A". Add two trades with DIFFERENT magic numbers on it
        // (e.g. EA magic + a manual magic 0) — By-Account must collapse them into ONE row.
        var (client, accId) = await SeedAsync();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Trades.Add(new Trade { AccountId = accId, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = DateTime.Parse("2026-05-10T10:00:00Z").ToUniversalTime(), NetProfit = 10m, MagicNumber = 20231 });
            db.Trades.Add(new Trade { AccountId = accId, DealTicket = 2, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = DateTime.Parse("2026-05-11T10:00:00Z").ToUniversalTime(), NetProfit = 5m, MagicNumber = 0 });
            db.SaveChanges();
        }
        var rows = await client.GetFromJsonAsync<AccountRow[]>($"/api/summary/by-account?accountIds={accId}");
        var row = Assert.Single(rows!); // one account row, not two magic rows
        Assert.Equal("A", row.Name);
        Assert.Equal(15m, row.NetProfit);
        Assert.Equal(2, row.TradeCount);
    }

    [Fact]
    public async Task Invalid_accountIds_returns_400()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.GetAsync("/api/trades?accountIds=not-a-guid");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_period_returns_400()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.GetAsync("/api/summary?period=year");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }
}
