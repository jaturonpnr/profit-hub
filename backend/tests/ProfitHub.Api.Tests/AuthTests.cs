using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class AuthTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private string SeedUser(string email, string password)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) });
        db.SaveChanges();
        return email;
    }

    [Fact]
    public async Task Login_returns_jwt()
    {
        var email = SeedUser(Guid.NewGuid() + "@x.com", "secret123");
        var login = await _f.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrEmpty(body!["token"]));
    }

    [Fact]
    public async Task Wrong_password_is_401()
    {
        var email = SeedUser(Guid.NewGuid() + "@x.com", "secret123");
        var login = await _f.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "WRONG" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
