using Microsoft.EntityFrameworkCore;

namespace ProfitHub.Api.Domain;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<BalanceOperation> BalanceOperations => Set<BalanceOperation>();
    public DbSet<EaName> EaNames => Set<EaName>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Account>().HasIndex(x => x.IngestKey).IsUnique();
        b.Entity<Account>().HasOne<User>().WithMany(u => u.Accounts).HasForeignKey(a => a.UserId);
        b.Entity<Trade>().HasIndex(x => new { x.AccountId, x.DealTicket }).IsUnique();
        b.Entity<Trade>().HasIndex(x => x.CloseTimeUtc);
        b.Entity<Trade>().HasOne<Account>().WithMany(a => a.Trades).HasForeignKey(t => t.AccountId);
        b.Entity<BalanceOperation>().HasIndex(x => new { x.AccountId, x.DealTicket }).IsUnique();
        b.Entity<EaName>().HasIndex(x => new { x.UserId, x.MagicNumber }).IsUnique();
    }
}
