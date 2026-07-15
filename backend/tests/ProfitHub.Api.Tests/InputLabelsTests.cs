using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProfitHub.Api.Tests;

public class InputLabelsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private record Row(string Key, string Label, Dictionary<string, string> ValueMap);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Put_then_get_upserts_label_and_value_map()
    {
        var client = await AuthedClient.Create(_f);
        await client.PutAsJsonAsync("/api/input-labels/Inp_risk_level_auto",
            new { label = "Auto Lot Risk", valueMap = new Dictionary<string, string> { ["3"] = "Medium" } });
        await client.PutAsJsonAsync("/api/input-labels/Inp_risk_level_auto",
            new
            {
                label = "Auto Lot (Risk Level)",
                valueMap = new Dictionary<string, string> { ["3"] = "Medium (1,000 USD)", ["2"] = "Low" },
            });

        var list = await client.GetFromJsonAsync<List<Row>>("/api/input-labels", Json);
        var row = Assert.Single(list!);
        Assert.Equal("Inp_risk_level_auto", row.Key);
        Assert.Equal("Auto Lot (Risk Level)", row.Label);
        Assert.Equal(2, row.ValueMap.Count);
        Assert.Equal("Medium (1,000 USD)", row.ValueMap["3"]);
        Assert.Equal("Low", row.ValueMap["2"]);
    }

    [Fact]
    public async Task Empty_put_deletes()
    {
        var client = await AuthedClient.Create(_f);
        await client.PutAsJsonAsync("/api/input-labels/InpSlUSD",
            new { label = "Stop Loss", valueMap = new Dictionary<string, string>() });

        var res = await client.PutAsJsonAsync("/api/input-labels/InpSlUSD",
            new { label = "", valueMap = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var list = await client.GetFromJsonAsync<List<Row>>("/api/input-labels", Json);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Labels_are_per_user()
    {
        var alice = await AuthedClient.Create(_f);
        var bob = await AuthedClient.Create(_f);
        await alice.PutAsJsonAsync("/api/input-labels/InpTpUSD",
            new { label = "Take Profit", valueMap = new Dictionary<string, string>() });

        var bobList = await bob.GetFromJsonAsync<List<Row>>("/api/input-labels", Json);
        Assert.Empty(bobList!);
    }
}
