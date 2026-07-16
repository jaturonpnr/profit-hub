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
    public DbSet<Backtest> Backtests => Set<Backtest>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<InputLabel> InputLabels => Set<InputLabel>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Account>().HasIndex(x => x.IngestKey).IsUnique();
        b.Entity<Account>().HasOne<User>().WithMany(u => u.Accounts).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Trade>().HasIndex(x => new { x.AccountId, x.DealTicket }).IsUnique();
        b.Entity<Trade>().HasIndex(x => x.CloseTimeUtc);
        b.Entity<Trade>().HasOne<Account>().WithMany(a => a.Trades).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Trade>().Property(t => t.ExecutionMs).HasPrecision(9, 3);
        b.Entity<Trade>().HasIndex(x => x.ClosingOrderTicket); // sidecar matches executions by this
        b.Entity<BalanceOperation>().HasIndex(x => new { x.AccountId, x.DealTicket }).IsUnique();
        b.Entity<BalanceOperation>().HasOne<Account>().WithMany().HasForeignKey(bo => bo.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EaName>().HasIndex(x => new { x.UserId, x.MagicNumber }).IsUnique();
        b.Entity<EaName>().HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Insight>().HasIndex(x => new { x.UserId, x.Period }).IsUnique();
        b.Entity<Insight>().HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Backtest>().HasIndex(x => x.UserId);
        b.Entity<Backtest>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // Money columns: 2 dp is enough. Ratio/percent columns need more precision
        // (e.g. Sharpe 63.516736, Profit Factor 9.850288) — default decimal(18,2) would truncate them.
        b.Entity<Backtest>().Property(x => x.InitialDeposit).HasPrecision(18, 2);
        b.Entity<Backtest>().Property(x => x.NetProfit).HasPrecision(18, 2);
        b.Entity<Backtest>().Property(x => x.GrossProfit).HasPrecision(18, 2);
        b.Entity<Backtest>().Property(x => x.GrossLoss).HasPrecision(18, 2);
        b.Entity<Backtest>().Property(x => x.EquityDrawdownMaxAbs).HasPrecision(18, 2);
        b.Entity<Backtest>().Property(x => x.ReturnPct).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.ProfitFactor).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.ExpectedPayoff).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.RecoveryFactor).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.SharpeRatio).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.BalanceDrawdownMaxPct).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.EquityDrawdownMaxPct).HasPrecision(18, 6);
        b.Entity<Backtest>().Property(x => x.WinRatePct).HasPrecision(18, 6);
        b.Entity<Withdrawal>().HasIndex(x => x.AccountId);
        b.Entity<Withdrawal>().HasOne<Account>().WithMany().HasForeignKey(w => w.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Withdrawal>().Property(w => w.Amount).HasPrecision(18, 2);
        b.Entity<Withdrawal>().Property(w => w.SuggestedAmount).HasPrecision(18, 2);
        b.Entity<Withdrawal>().Property(w => w.Capital).HasPrecision(18, 2);
        b.Entity<InputLabel>().HasIndex(x => new { x.UserId, x.Key }).IsUnique();
        b.Entity<InputLabel>().HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
