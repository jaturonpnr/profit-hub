namespace ProfitHub.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsAdmin { get; set; }
    public string TimeZone { get; set; } = "Asia/Bangkok";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Account> Accounts { get; set; } = [];
}

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long AccountNumber { get; set; }          // MT5 login number
    public string Name { get; set; } = "";
    public string Broker { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public required string IngestKey { get; set; }   // per-Account API key (CONTEXT.md)
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastIngestAtUtc { get; set; }   // EA heartbeat for Accounts page
    public List<Trade> Trades { get; set; } = [];
}

public class Trade
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public long DealTicket { get; set; }             // idempotency key with AccountId
    public long PositionId { get; set; }
    public required string Symbol { get; set; }
    public required string Direction { get; set; }   // "buy" | "sell"
    public decimal Lots { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public DateTime OpenTimeUtc { get; set; }
    public DateTime CloseTimeUtc { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal Commission { get; set; }
    public decimal Swap { get; set; }
    public decimal NetProfit { get; set; }           // Gross + Commission + Swap (CONTEXT.md)
    public long MagicNumber { get; set; }
    public string Comment { get; set; } = "";
    public int? ExecutionMs { get; set; }            // closing order fill latency, ms; null = unknown
}

public class BalanceOperation
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public long DealTicket { get; set; }
    public decimal Amount { get; set; }              // + deposit, - withdrawal
    public DateTime TimeUtc { get; set; }
    public string Comment { get; set; } = "";
}

public class EaName
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public long MagicNumber { get; set; }
    public required string Name { get; set; }
}

/// Cached AI Weekly Coach narrative, one row per (user, period). Upserted on
/// POST /api/insights so reopening the dashboard shows the last analysis without
/// re-spending Claude tokens. Period is "week" | "month".
public class Insight
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public required string Period { get; set; }     // "week" | "month"
    public required string Content { get; set; }     // the Thai narrative text
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

/// Single global row holding the USD→THB exchange rate config (not per-user).
/// OverrideRate, when set, wins over the cached live rate.
public class FxConfig
{
    public int Id { get; set; } = 1;          // singleton row
    public decimal? OverrideRate { get; set; } // manual pin; null = use live
    public decimal? LiveRate { get; set; }     // last fetched THB per 1 USD
    public DateTime? LiveRateFetchedAtUtc { get; set; }
}

/// One imported MT5 Strategy Tester run, owned by a User. Hypothetical data — never
/// mixed into live Net Profit/Balance/ROI (see ADR 0004). Summary KPIs are stored as
/// queryable columns; the equity curve and the full raw metric set are stored as JSON.
public class Backtest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Identity & settings (from the report's settings block)
    public required string ExpertName { get; set; } // always present — the Backtest's identity
    public string Symbol { get; set; } = "";
    public string Timeframe { get; set; } = "";     // e.g. "M15"
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public long? MagicNumber { get; set; }          // best-effort; null if not found
    public decimal InitialDeposit { get; set; }
    public string Currency { get; set; } = "USD";

    // Headline KPIs (sortable comparison columns)
    public decimal NetProfit { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal ReturnPct { get; set; }          // NetProfit / InitialDeposit * 100 (Backtest Return)
    public decimal ProfitFactor { get; set; }
    public decimal ExpectedPayoff { get; set; }
    public decimal RecoveryFactor { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal BalanceDrawdownMaxPct { get; set; }
    public decimal EquityDrawdownMaxPct { get; set; } // Max Equity Drawdown — the real risk measure
    public decimal EquityDrawdownMaxAbs { get; set; }
    public int TotalTrades { get; set; }
    public decimal WinRatePct { get; set; }

    // Series + raw, stored denormalised as JSON text
    public string EquityCurveJson { get; set; } = "[]";  // [{ "t": "2026-01-01T00:00:00", "balance": 1500 }, ...]
    public string RawMetricsJson { get; set; } = "{}";   // every parsed label→value, future-proofing

    public string SourceFileName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
