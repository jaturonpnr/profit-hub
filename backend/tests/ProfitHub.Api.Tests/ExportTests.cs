using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class ExportTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    [Fact]
    public async Task Trades_csv_has_header_and_rows()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 1, name = "A", broker = "" });
        var acc = await res.Content.ReadFromJsonAsync<AccountsTests.AccountDto>();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Trades.Add(new Trade { AccountId = acc!.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = DateTime.UtcNow, NetProfit = 12.69m });
            db.SaveChanges();
        }
        var csv = await client.GetStringAsync("/api/export/trades.csv");
        var lines = csv.Trim().Split('\n');
        Assert.StartsWith("CloseTime(Local)", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains("12.69", lines[1]);
    }

    [Fact]
    public async Task Summary_csv_has_header_and_invariant_formatted_row()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 2, name = "B", broker = "" });
        var acc = await res.Content.ReadFromJsonAsync<AccountsTests.AccountDto>();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Trades.Add(new Trade { AccountId = acc!.Id, DealTicket = 10, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), NetProfit = 10.5m });
            db.Trades.Add(new Trade { AccountId = acc.Id, DealTicket = 11, Symbol = "XAUUSD", Direction = "sell",
                CloseTimeUtc = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc), NetProfit = -2.25m });
            db.SaveChanges();
        }
        var csv = await client.GetStringAsync($"/api/export/summary.csv?period=day&accountIds={acc.Id}");
        var lines = csv.Trim().Split('\n');
        Assert.Equal("PeriodStart,NetProfit,TradeCount,Wins,WinRate", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2026-06-01,8.25,2,1,50.0%", lines[1]);
    }
}
