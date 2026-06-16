using Microsoft.EntityFrameworkCore;

namespace ProfitHub.Api.Domain;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<BalanceOperation> BalanceOperations => Set<BalanceOperation>();
    public DbSet<EaName> EaNames => Set<EaName>();
    public DbSet<FxConfig> FxConfigs => Set<FxConfig>();
    public DbSet<Insight> Insights => Set<Insight>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Account>().HasIndex(x => x.IngestKey).IsUnique();
        b.Entity<Account>().HasOne<User>().WithMany(u => u.Accounts).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Trade>().HasIndex(x => new { x.AccountId, x.DealTicket }).IsUnique();
        b.Entity<Trade>().HasIndex(x => x.CloseTimeUtc);
        b.Entity<Trade>().HasOne<Account>().WithMany(a => a.Trades).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BalanceOperation>().HasIndex(x => new { x.AccountId, x.DealTicket }).IsUnique();
        b.Entity<BalanceOperation>().HasOne<Account>().WithMany().HasForeignKey(bo => bo.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EaName>().HasIndex(x => new { x.UserId, x.MagicNumber }).IsUnique();
        b.Entity<EaName>().HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Insight>().HasIndex(x => new { x.UserId, x.Period }).IsUnique();
        b.Entity<Insight>().HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
