using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public static class AuthedClient
{
    public static async Task<HttpClient> Create(ApiFactory f)
    {
        var client = f.CreateClient();
        var email = Guid.NewGuid() + "@x.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["token"];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
