using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class MeTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Get_me_returns_email_and_default_timezone()
    {
        var client = f.CreateClient();
        var email = Guid.NewGuid() + "@x.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["token"];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal(email, body!["email"]);
        Assert.Equal("Asia/Bangkok", body["timeZone"]);
    }

    [Fact]
    public async Task Put_timezone_updates_and_returns_token_with_new_tz_claim()
    {
        var client = await AuthedClient.Create(f);

        var put = await client.PutAsJsonAsync("/api/me/timezone", new { timeZone = "Europe/Athens" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var token = (await put.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["token"];

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("Europe/Athens", jwt.Claims.First(c => c.Type == "tz").Value);

        // Use the new token for a follow-up GET /api/me.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/me");
        var body = await me.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Europe/Athens", body!["timeZone"]);
    }

    [Fact]
    public async Task Put_invalid_timezone_is_400()
    {
        var client = await AuthedClient.Create(f);
        var put = await client.PutAsJsonAsync("/api/me/timezone", new { timeZone = "Not/AZone" });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }
}
