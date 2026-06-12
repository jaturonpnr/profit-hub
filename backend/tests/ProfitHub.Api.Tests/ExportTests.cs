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
        Assert.StartsWith("CloseTime", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains("12.69", lines[1]);
    }
}
