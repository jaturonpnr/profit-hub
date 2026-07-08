using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProfitHub.Api.Tests;

public class BacktestsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private static MultipartFormDataContent Upload(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fixture);
        var content = new ByteArrayContent(File.ReadAllBytes(path));
        content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { content, "file", fixture } };
    }

    [Fact]
    public async Task Upload_then_list_and_detail()
    {
        var client = await AuthedClient.Create(_f);

        var post = await client.PostAsync("/api/backtests", Upload("qa.xlsx"));
        post.EnsureSuccessStatusCode();
        var created = await post.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("Quantum Athena", created!["expertName"].ToString());

        var list = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/backtests");
        Assert.Single(list!);

        var id = created["id"].ToString();
        var detail = await client.GetFromJsonAsync<JsonDetail>($"/api/backtests/{id}");
        Assert.NotEmpty(detail!.equityCurve);
        Assert.Equal("Quantum Athena", detail.summary.expertName);
    }

    [Fact]
    public async Task Detail_includes_ea_inputs()
    {
        var client = await AuthedClient.Create(_f);

        var post = await client.PostAsync("/api/backtests", Upload("omg.xlsx"));
        post.EnsureSuccessStatusCode();
        var created = await post.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"].ToString();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}");
        var inputs = detail.GetProperty("inputs");
        Assert.True(inputs.GetArrayLength() > 0);
        // Entries serialize as camelCase { section, key, value }
        Assert.Contains(inputs.EnumerateArray(), i =>
            i.GetProperty("key").GetString() == "MAGIC" && i.GetProperty("value").GetString() == "7337");
    }

    [Fact]
    public async Task Detail_includes_trade_analytics()
    {
        var client = await AuthedClient.Create(_f);

        var post = await client.PostAsync("/api/backtests", Upload("omg.xlsx"));
        post.EnsureSuccessStatusCode();
        var created = await post.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"].ToString();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}");
        Assert.True(detail.GetProperty("heatmap").GetArrayLength() > 0);
        Assert.True(detail.GetProperty("monthly").GetArrayLength() > 0);
        Assert.Equal("384.6", detail.GetProperty("tradeStats").GetProperty("largestWin").GetString());
    }

    [Fact]
    public async Task Upload_garbage_returns_400()
    {
        var client = await AuthedClient.Create(_f);
        var content = new ByteArrayContent("not an xlsx"u8.ToArray());
        var form = new MultipartFormDataContent { { content, "file", "bad.xlsx" } };
        var resp = await client.PostAsync("/api/backtests", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cannot_see_or_delete_another_users_backtest()
    {
        var alice = await AuthedClient.Create(_f);
        var post = await alice.PostAsync("/api/backtests", Upload("qq.xlsx"));
        var created = await post.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"].ToString();

        var bob = await AuthedClient.Create(_f);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/backtests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/backtests/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/backtests/{id}")).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/backtests"))!);
    }

    private record JsonDetail(JsonSummary summary, List<object> equityCurve);
    private record JsonSummary(string expertName);
}
