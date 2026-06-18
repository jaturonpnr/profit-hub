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

    [Fact]
    public async Task Can_persist_and_read_a_backtest()
    {
        using var ctx = TestDb.Create(out var conn);
        using var _ = conn;
        var user = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x" };
        ctx.Users.Add(user);
        ctx.Backtests.Add(new Backtest
        {
            UserId = user.Id, ExpertName = "Quantum Athena", InitialDeposit = 1500m,
            NetProfit = 3950.68m, ReturnPct = 263.38m, SharpeRatio = 63.516736m,
            EquityDrawdownMaxPct = 19.94m, EquityCurveJson = "[]",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 6, 11),
        });
        await ctx.SaveChangesAsync();

        var read = await ctx.Backtests.SingleAsync(x => x.UserId == user.Id);
        Assert.Equal("Quantum Athena", read.ExpertName);
        Assert.Equal(63.516736m, read.SharpeRatio); // precision preserved, not truncated to 63.52
        Assert.Equal(new DateOnly(2026, 1, 1), read.PeriodFrom); // DateOnly round-trips on SQLite
    }
}
