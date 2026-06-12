using System.Net;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class AccountsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record AccountDto(Guid Id, long AccountNumber, string Name, string Broker,
        string Currency, string IngestKey, DateTime? LastIngestAtUtc);

    [Fact]
    public async Task Create_account_returns_ingest_key_and_lists_it()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 111, name = "Main", broker = "IC" });
        var created = await res.Content.ReadFromJsonAsync<AccountDto>();
        Assert.False(string.IsNullOrEmpty(created!.IngestKey));
        var list = await client.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_accounts()
    {
        var a = await AuthedClient.Create(_f);
        var b = await AuthedClient.Create(_f);
        await a.PostAsJsonAsync("/api/accounts", new { accountNumber = 111, name = "A", broker = "" });
        var list = await b.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var res = await _f.CreateClient().GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
