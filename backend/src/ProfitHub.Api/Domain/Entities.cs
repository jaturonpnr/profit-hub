namespace ProfitHub.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
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
