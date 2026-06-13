using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public static class TestDb
{
    public static AppDbContext Create(out SqliteConnection conn)
    {
        conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }
}

public class DbContextTests
{
    [Fact]
    public void Duplicate_deal_ticket_per_account_is_rejected()
    {
        using var ctx = TestDb.Create(out var conn);
        using var _ = conn;
        var user = new User { Email = "a@b.c", PasswordHash = "x" };
        var acc = new Account { UserId = user.Id, AccountNumber = 111, IngestKey = "k1" };
        ctx.AddRange(user, acc);
        ctx.Add(new Trade { AccountId = acc.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy" });
        ctx.SaveChanges();
        ctx.Add(new Trade { AccountId = acc.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy" });
        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }
}
