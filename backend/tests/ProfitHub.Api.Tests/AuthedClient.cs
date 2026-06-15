using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public static class AuthedClient
{
    /// <summary>
    /// Seeds a user directly in the DB (BCrypt-hashed password) — public registration
    /// no longer exists — then logs in to obtain a Bearer token. Pass admin: true to
    /// seed an IsAdmin user so /api/admin/* tests can authenticate as an admin.
    /// </summary>
    public static async Task<HttpClient> Create(ApiFactory f, bool admin = false)
    {
        var (client, _) = await CreateWithEmail(f, admin);
        return client;
    }

    public static async Task<(HttpClient client, string email)> CreateWithEmail(ApiFactory f, bool admin = false)
    {
        var email = Guid.NewGuid() + "@x.com";
        const string password = "secret123";
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsAdmin = admin,
            });
            db.SaveChanges();
        }
        var client = f.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["token"];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, email);
    }
}
