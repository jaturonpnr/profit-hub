using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class EaNamesTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    [Fact]
    public async Task Put_then_get_returns_name_and_upserts()
    {
        var client = await AuthedClient.Create(_f);
        await client.PutAsJsonAsync("/api/ea-names/20231", new { name = "Quantum Queen" });
        await client.PutAsJsonAsync("/api/ea-names/20231", new { name = "QQ v2" });
        var list = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/ea-names");
        Assert.Single(list!);
        Assert.Equal("QQ v2", list![0]["name"].ToString());
    }
}
