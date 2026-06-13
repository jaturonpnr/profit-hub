using System.Net;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class AuthTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = f.CreateClient();

    [Fact]
    public async Task Register_then_login_returns_jwt()
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register", new { email = "j@x.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "j@x.com", password = "secret123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrEmpty(body!["token"]));
    }

    [Fact]
    public async Task Register_same_email_different_case_is_409()
    {
        var first = await _client.PostAsJsonAsync("/api/auth/register", new { email = "dup@x.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await _client.PostAsJsonAsync("/api/auth/register", new { email = "DUP@X.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_401()
    {
        await _client.PostAsJsonAsync("/api/auth/register", new { email = "k@x.com", password = "secret123" });
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "k@x.com", password = "WRONG" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
