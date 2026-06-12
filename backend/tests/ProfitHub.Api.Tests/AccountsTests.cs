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

    [Fact]
    public async Task Delete_own_account_returns_204_and_disappears_from_list()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 222, name = "ToDelete", broker = "" });
        var created = await res.Content.ReadFromJsonAsync<AccountDto>();

        var deleteRes = await client.DeleteAsync($"/api/accounts/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var list = await client.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.DoesNotContain(list!, a => a.Id == created.Id);
    }

    [Fact]
    public async Task Delete_another_users_account_returns_404()
    {
        var userA = await AuthedClient.Create(_f);
        var userB = await AuthedClient.Create(_f);

        var res = await userA.PostAsJsonAsync("/api/accounts", new { accountNumber = 333, name = "UserAAccount", broker = "" });
        var created = await res.Content.ReadFromJsonAsync<AccountDto>();

        var deleteRes = await userB.DeleteAsync($"/api/accounts/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteRes.StatusCode);
    }
}
