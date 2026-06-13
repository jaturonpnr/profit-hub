using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

/// <summary>
/// Guards the test/prod timezone-serialization divergence: with SQLite (this harness) DateTimes
/// read back as Kind=Unspecified, so without the UtcDateTimeConverter they'd serialize WITHOUT a
/// trailing 'Z' — diverging from Npgsql production which yields 'Z'. These tests prove the converter
/// normalizes even Unspecified-kind values to a single trailing 'Z'.
/// </summary>
public class SerializationTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record AccountDto(Guid Id, long AccountNumber, string Name, string Broker,
        string Currency, string IngestKey, DateTime? LastIngestAtUtc);

    [Fact]
    public async Task Trade_closeTimeUtc_serializes_with_single_trailing_Z()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts",
            new { accountNumber = 111, name = "Main", broker = "IC" });
        var acc = await res.Content.ReadFromJsonAsync<AccountDto>();

        // Seed a Trade directly via the DbContext. On SQLite this read-back is Kind=Unspecified,
        // exactly the case the converter must normalize.
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Trades.Add(new Trade
            {
                AccountId = acc!.Id,
                DealTicket = 1, PositionId = 1,
                Symbol = "XAUUSD.PRO", Direction = "buy",
                Lots = 0.26m, OpenPrice = 4499.46m, ClosePrice = 4500.02m,
                OpenTimeUtc = new DateTime(2026, 5, 28, 3, 0, 0, DateTimeKind.Utc),
                CloseTimeUtc = new DateTime(2026, 5, 28, 3, 6, 0, DateTimeKind.Utc),
                GrossProfit = 10m, Commission = -1.2m, Swap = 0m, NetProfit = 8.8m,
                MagicNumber = 20231, Comment = "QQ",
            });
            db.SaveChanges();
        }

        var json = await client.GetStringAsync("/api/trades");

        Assert.Contains("\"2026-05-28T03:06:00", json);
        // The serialized closeTimeUtc must end in a single Z, never "...ZZ".
        Assert.Contains("2026-05-28T03:06:00", json);
        Assert.DoesNotContain("ZZ", json);
        var idx = json.IndexOf("2026-05-28T03:06:00", StringComparison.Ordinal);
        var field = json[idx..json.IndexOf('"', idx)];
        Assert.EndsWith("Z", field);
    }

    [Fact]
    public async Task Account_lastIngestAtUtc_serializes_with_Z_after_ingest()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts",
            new { accountNumber = 222, name = "Ingested", broker = "IC" });
        var acc = await res.Content.ReadFromJsonAsync<AccountDto>();

        var ingest = _f.CreateClient();
        ingest.DefaultRequestHeaders.Add("X-Ingest-Key", acc!.IngestKey);
        await ingest.PostAsJsonAsync("/api/ingest/deals", new
        {
            deals = new[]
            {
                new
                {
                    dealTicket = 1L, positionId = 1L, symbol = "XAUUSD.PRO", type = "buy",
                    lots = 0.26m, openPrice = 4499.46m, closePrice = 4500.02m,
                    openTimeUtc = "2026-05-28T03:00:00Z", closeTimeUtc = "2026-05-28T03:06:00Z",
                    grossProfit = 10m, commission = -1.2m, swap = 0m, magicNumber = 20231L, comment = "QQ",
                },
            },
        });

        var json = await client.GetStringAsync("/api/accounts");

        Assert.DoesNotContain("ZZ", json);
        var marker = "\"lastIngestAtUtc\":\"";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, "lastIngestAtUtc should be present and non-null");
        var start = idx + marker.Length;
        var value = json[start..json.IndexOf('"', start)];
        Assert.EndsWith("Z", value);
    }
}
