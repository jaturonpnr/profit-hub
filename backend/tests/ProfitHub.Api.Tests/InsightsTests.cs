using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class InsightsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record InsightRow(bool Enabled, string? Text, DateTime? GeneratedAtUtc, string Period);

    // The test environment configures no Anthropic:ApiKey, so the AI Coach is
    // disabled: GET reports enabled:false and returns no cached text. Deterministic,
    // no network — the live Claude call is intentionally not exercised here.
    [Fact]
    public async Task Get_returns_disabled_when_no_api_key_configured()
    {
        var client = await AuthedClient.Create(_f);
        var row = await client.GetFromJsonAsync<InsightRow>("/api/insights?period=week");
        Assert.NotNull(row);
        Assert.False(row!.Enabled);
        Assert.Null(row.Text);
        Assert.Null(row.GeneratedAtUtc);
        Assert.Equal("week", row.Period);
    }
}
