using System.Net;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class FxTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record FxRow(decimal? Rate, string Source, decimal? OverrideRate, decimal? LiveRate);

    [Fact]
    public async Task Override_is_persisted_and_wins_over_live()
    {
        var client = await AuthedClient.Create(_f);
        var put = await client.PutAsJsonAsync("/api/fx", new { overrideRate = 36.5m });
        var body = await put.Content.ReadFromJsonAsync<FxRow>();
        Assert.Equal(36.5m, body!.Rate);
        Assert.Equal("override", body.Source); // short-circuits before any live fetch

        var get = await client.GetFromJsonAsync<FxRow>("/api/fx");
        Assert.Equal(36.5m, get!.Rate);
        Assert.Equal("override", get.Source);
        Assert.Equal(36.5m, get.OverrideRate);
    }

    [Fact]
    public async Task Non_positive_override_is_rejected()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PutAsJsonAsync("/api/fx", new { overrideRate = -1m });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
