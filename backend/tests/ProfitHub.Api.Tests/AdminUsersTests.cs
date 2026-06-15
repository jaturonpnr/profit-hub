using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class AdminUsersTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private record UserRow(Guid Id, string Email, bool IsAdmin, DateTime CreatedAtUtc, int AccountCount);

    [Fact]
    public async Task Admin_can_create_list_and_reset_password()
    {
        var admin = await AuthedClient.Create(_f, admin: true);
        var email = Guid.NewGuid() + "@x.com";

        var create = await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "secret123", isAdmin = false });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<UserRow>();
        Assert.Equal(email, created!.Email);
        Assert.False(created.IsAdmin);

        var list = await admin.GetFromJsonAsync<List<UserRow>>("/api/admin/users");
        Assert.Contains(list!, u => u.Email == email);

        // The new user can log in with the original password.
        var anon = _f.CreateClient();
        var login1 = await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login1.StatusCode);

        // Reset the password; old fails, new works.
        var reset = await admin.PutAsJsonAsync($"/api/admin/users/{created.Id}/password", new { password = "newsecret9" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var loginOld = await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginOld.StatusCode);
        var loginNew = await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "newsecret9" });
        Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_short_password_and_duplicate()
    {
        var admin = await AuthedClient.Create(_f, admin: true);
        var email = Guid.NewGuid() + "@x.com";
        var shortPw = await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "short", isAdmin = false });
        Assert.Equal(HttpStatusCode.BadRequest, shortPw.StatusCode);

        await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "secret123", isAdmin = false });
        var dup = await admin.PostAsJsonAsync("/api/admin/users", new { email = email.ToUpperInvariant(), password = "secret123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Non_admin_gets_403()
    {
        var user = await AuthedClient.Create(_f, admin: false);
        var list = await user.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        var create = await user.PostAsJsonAsync("/api/admin/users", new { email = "x@x.com", password = "secret123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Cannot_delete_self()
    {
        var (admin, email) = await AuthedClient.CreateWithEmail(_f, admin: true);
        Guid myId;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            myId = (await db.Users.FirstAsync(u => u.Email == email)).Id;
        }
        var del = await admin.DeleteAsync($"/api/admin/users/{myId}");
        Assert.Equal(HttpStatusCode.BadRequest, del.StatusCode);
    }

    [Fact]
    public async Task Can_delete_an_admin_when_another_admin_exists()
    {
        var admin = await AuthedClient.Create(_f, admin: true);
        var otherAdminEmail = Guid.NewGuid() + "@x.com";
        var create = await admin.PostAsJsonAsync("/api/admin/users", new { email = otherAdminEmail, password = "secret123", isAdmin = true });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var otherId = (await create.Content.ReadFromJsonAsync<UserRow>())!.Id;

        // Two admins exist → deleting one is allowed.
        var del = await admin.DeleteAsync($"/api/admin/users/{otherId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Cannot_delete_the_last_admin()
    {
        // Acting admin authenticates via its JWT (isAdmin claim). The DB is then arranged
        // so the ONLY admin row is a separate "lone" user — deleting it must be blocked.
        var actor = await AuthedClient.Create(_f, admin: true);
        Guid loneAdminId;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lone = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x", IsAdmin = true };
            db.Users.Add(lone);
            await db.SaveChangesAsync();
            loneAdminId = lone.Id;
            // Demote every other user so `lone` is the single remaining admin.
            await db.Users.Where(u => u.Id != loneAdminId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, false));
        }
        var blocked = await actor.DeleteAsync($"/api/admin/users/{loneAdminId}");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_user_cascades_their_trades()
    {
        var admin = await AuthedClient.Create(_f, admin: true);
        var email = Guid.NewGuid() + "@x.com";
        var create = await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "secret123", isAdmin = false });
        var victimId = (await create.Content.ReadFromJsonAsync<UserRow>())!.Id;

        Guid accId;
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var acc = new Account { UserId = victimId, AccountNumber = 999, IngestKey = Guid.NewGuid().ToString("N") };
            db.Accounts.Add(acc);
            db.Trades.Add(new Trade
            {
                AccountId = acc.Id, DealTicket = 1, PositionId = 1, Symbol = "XAUUSD",
                Direction = "buy", OpenTimeUtc = DateTime.UtcNow, CloseTimeUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
            accId = acc.Id;
        }

        var del = await admin.DeleteAsync($"/api/admin/users/{victimId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Accounts.AnyAsync(a => a.Id == accId));
            Assert.False(await db.Trades.AnyAsync(t => t.AccountId == accId));
        }
    }
}
