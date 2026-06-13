# Profit Hub Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Multi-user dashboard that aggregates closed trades from multiple MT5 accounts (pushed by a collector EA), shows Net Profit by day/week/month per account/EA, and exports CSV.

**Architecture:** A custom MQL5 EA on each user's VPS pushes closed deals to a .NET 8 Web API (Render) authenticated by per-Account Ingest Keys; data lands in Neon Postgres; an Angular SPA (Vercel) reads it via JWT-authenticated endpoints. All times stored UTC; reporting grouped in the user's timezone (default Asia/Bangkok). Idempotency key = (AccountId, DealTicket). See `CONTEXT.md` and `docs/adr/0001`, `docs/adr/0002`.

**Tech Stack:** .NET 8 Web API, EF Core + Npgsql (SQLite in tests), xUnit, Angular 18 standalone + Chart.js, MQL5, Docker/Render/Vercel.

**Repo layout:**

```
/backend
  src/ProfitHub.Api/          ← single project: Program.cs, Domain/, Features/
  tests/ProfitHub.Api.Tests/
/frontend                     ← Angular app
/ea/ProfitHubCollector.mq5
/Dockerfile, render.yaml
```

---

### Task 1: Backend scaffold

**Files:** Create `backend/` solution, API project, test project.

- [ ] **Step 1: Scaffold projects**

```bash
cd /Users/pack/Workspace/profit-hub
mkdir -p backend && cd backend
dotnet new webapi -n ProfitHub.Api -o src/ProfitHub.Api --no-openapi
dotnet new xunit -n ProfitHub.Api.Tests -o tests/ProfitHub.Api.Tests
dotnet new sln -n ProfitHub
dotnet sln add src/ProfitHub.Api tests/ProfitHub.Api.Tests
dotnet add tests/ProfitHub.Api.Tests reference src/ProfitHub.Api
cd src/ProfitHub.Api
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package BCrypt.Net-Next
cd ../../tests/ProfitHub.Api.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

Delete the template `WeatherForecast` files. In `ProfitHub.Api.csproj` add `<InternalsVisibleTo Include="ProfitHub.Api.Tests" />`.

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/ProfitHub.sln` — Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend && git commit -m "chore: scaffold .NET solution"
```

---

### Task 2: Domain entities + DbContext

**Files:**
- Create: `backend/src/ProfitHub.Api/Domain/Entities.cs`
- Create: `backend/src/ProfitHub.Api/Domain/AppDbContext.cs`
- Test: `backend/tests/ProfitHub.Api.Tests/DbContextTests.cs`

- [ ] **Step 1: Write entities**

```csharp
// Domain/Entities.cs
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
```

```csharp
// Domain/AppDbContext.cs
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
```

- [ ] **Step 2: Write test that the model materialises on SQLite**

```csharp
// tests/ProfitHub.Api.Tests/DbContextTests.cs
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
```

- [ ] **Step 3: Run tests** — `dotnet test backend/ProfitHub.sln` — Expected: PASS.

- [ ] **Step 4: Commit** — `git add backend && git commit -m "feat: domain entities and DbContext"`

---

### Task 3: App wiring (Program.cs) + test host

**Files:**
- Modify: `backend/src/ProfitHub.Api/Program.cs`
- Create: `backend/tests/ProfitHub.Api.Tests/ApiFactory.cs`

- [ ] **Step 1: Program.cs**

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProfitHub.Api.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-key-change-me-0123456789abcdef";
builder.Services.AddSingleton(new JwtSettings(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, ValidateAudience = false,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:4200"])
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
if (!app.Environment.IsEnvironment("Testing"))
    using (var scope = app.Services.CreateScope())
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");
ProfitHub.Api.Features.Endpoints.MapAll(app);

app.Run();

public record JwtSettings(string Key);
public partial class Program { }
```

Create `Features/Endpoints.cs` with an empty `public static class Endpoints { public static void MapAll(WebApplication app) { } }` — later tasks add `Map*` calls here.

- [ ] **Step 2: ApiFactory for integration tests**

```csharp
// tests/ProfitHub.Api.Tests/ApiFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _conn.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }
    protected override void Dispose(bool disposing) { _conn.Dispose(); base.Dispose(disposing); }
}
```

(Add `using Microsoft.Extensions.DependencyInjection.Extensions;` for `RemoveAll`.)

- [ ] **Step 3: Smoke test `/health` returns ok** — add to a new `HealthTests.cs`:

```csharp
public class HealthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public HealthTests(ApiFactory f) => _client = f.CreateClient();
    [Fact]
    public async Task Health_returns_ok()
        => Assert.Equal("ok", await _client.GetStringAsync("/health"));
}
```

Run: `dotnet test backend/ProfitHub.sln` — Expected: PASS.

- [ ] **Step 4: Create initial EF migration**

```bash
cd backend/src/ProfitHub.Api
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet tool install -g dotnet-ef || true
ConnectionStrings__Default="Host=localhost;Database=stub" dotnet ef migrations add Initial
```

- [ ] **Step 5: Commit** — `git add backend && git commit -m "feat: app wiring, test host, initial migration"`

---

### Task 4: Auth (register/login → JWT)

**Files:**
- Create: `backend/src/ProfitHub.Api/Features/Auth.cs`
- Modify: `backend/src/ProfitHub.Api/Features/Endpoints.cs` (add `Auth.Map(app);`)
- Test: `backend/tests/ProfitHub.Api.Tests/AuthTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
// AuthTests.cs
using System.Net;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class AuthTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = f.CreateClient();

    [Fact]
    public async Task Register_then_login_returns_jwt()
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register", new { email = "j@x.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "j@x.com", password = "secret123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrEmpty(body!["token"]));
    }

    [Fact]
    public async Task Wrong_password_is_401()
    {
        await _client.PostAsJsonAsync("/api/auth/register", new { email = "k@x.com", password = "secret123" });
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "k@x.com", password = "WRONG" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
```

- [ ] **Step 2: Run, expect FAIL (404).**

- [ ] **Step 3: Implement**

```csharp
// Features/Auth.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Auth
{
    public record Creds(string Email, string Password);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/register", async (Creds c, AppDbContext db) =>
        {
            if (c.Password.Length < 8) return Results.BadRequest(new { error = "password too short" });
            if (await db.Users.AnyAsync(u => u.Email == c.Email)) return Results.Conflict();
            db.Users.Add(new User { Email = c.Email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(c.Password) });
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        app.MapPost("/api/auth/login", async (Creds c, AppDbContext db, JwtSettings jwt) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email);
            if (user is null || !BCrypt.Net.BCrypt.Verify(c.Password, user.PasswordHash))
                return Results.Unauthorized();
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                claims: [new Claim("sub", user.Id.ToString()), new Claim("tz", user.TimeZone)],
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256)));
            return Results.Ok(new { token });
        });
    }

    public static Guid UserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue("sub") ?? p.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
```

In `Program.cs` builder add `JwtBearerOptions.MapInboundClaims = false` style fix if `sub` is remapped: set `o.MapInboundClaims = false;` inside `AddJwtBearer`.

- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: register/login with JWT"`

---

### Task 5: Ingest endpoint (idempotent, with backfill support)

**Files:**
- Create: `backend/src/ProfitHub.Api/Features/Ingest.cs` (add `Ingest.Map(app);` to Endpoints)
- Test: `backend/tests/ProfitHub.Api.Tests/IngestTests.cs`

The EA POSTs batches. Deal type `"balance"` goes to `BalanceOperations`; `"buy"`/`"sell"` go to `Trades`. Re-sending the same tickets must not duplicate (ADR 0001).

- [ ] **Step 1: Failing tests**

```csharp
// IngestTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class IngestTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    private (HttpClient client, Account acc) Setup()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Email = Guid.NewGuid() + "@x.com", PasswordHash = "x" };
        var acc = new Account { UserId = user.Id, AccountNumber = 111, IngestKey = Guid.NewGuid().ToString("N") };
        db.AddRange(user, acc); db.SaveChanges();
        return (_f.CreateClient(), acc);
    }

    private static object Deal(long ticket, decimal profit = 10m, string type = "buy") => new
    {
        dealTicket = ticket, positionId = ticket, symbol = "XAUUSD.PRO", type,
        lots = 0.26m, openPrice = 4499.46m, closePrice = 4500.02m,
        openTimeUtc = "2026-05-28T03:00:00Z", closeTimeUtc = "2026-05-28T03:06:00Z",
        grossProfit = profit, commission = -1.2m, swap = 0m, magicNumber = 20231, comment = "QQ"
    };

    [Fact]
    public async Task Resending_same_deals_does_not_duplicate()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        var payload = new { deals = new[] { Deal(1), Deal(2) } };
        await client.PostAsJsonAsync("/api/ingest/deals", payload);
        var second = await client.PostAsJsonAsync("/api/ingest/deals", payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, db.Trades.Count(t => t.AccountId == acc.Id));
    }

    [Fact]
    public async Task Net_profit_is_gross_plus_commission_plus_swap()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        await client.PostAsJsonAsync("/api/ingest/deals", new { deals = new[] { Deal(10, profit: 12.69m) } });
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(12.69m + -1.2m + 0m, db.Trades.Single(t => t.AccountId == acc.Id).NetProfit);
    }

    [Fact]
    public async Task Balance_deals_go_to_balance_operations()
    {
        var (client, acc) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", acc.IngestKey);
        await client.PostAsJsonAsync("/api/ingest/deals", new { deals = new[] { Deal(20, profit: 500m, type: "balance") } });
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, db.Trades.Count(t => t.AccountId == acc.Id));
        Assert.Equal(500m, db.BalanceOperations.Single(b => b.AccountId == acc.Id).Amount);
    }

    [Fact]
    public async Task Bad_key_is_401()
    {
        var (client, _) = Setup();
        client.DefaultRequestHeaders.Add("X-Ingest-Key", "nope");
        var res = await client.PostAsJsonAsync("/api/ingest/deals", new { deals = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
```

- [ ] **Step 2: Run, expect FAIL.**

- [ ] **Step 3: Implement**

```csharp
// Features/Ingest.cs
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Ingest
{
    public record DealDto(long DealTicket, long PositionId, string Symbol, string Type,
        decimal Lots, decimal OpenPrice, decimal ClosePrice,
        DateTime OpenTimeUtc, DateTime CloseTimeUtc,
        decimal GrossProfit, decimal Commission, decimal Swap, long MagicNumber, string? Comment);
    public record Batch(DealDto[] Deals);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/ingest/deals", async (HttpRequest req, Batch batch, AppDbContext db) =>
        {
            var key = req.Headers["X-Ingest-Key"].ToString();
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.IngestKey == key);
            if (acc is null) return Results.Unauthorized();

            var tickets = batch.Deals.Select(d => d.DealTicket).ToArray();
            var existingTrades = await db.Trades.Where(t => t.AccountId == acc.Id && tickets.Contains(t.DealTicket))
                .Select(t => t.DealTicket).ToHashSetAsync();
            var existingBalOps = await db.BalanceOperations.Where(b => b.AccountId == acc.Id && tickets.Contains(b.DealTicket))
                .Select(b => b.DealTicket).ToHashSetAsync();

            foreach (var d in batch.Deals)
            {
                if (d.Type == "balance")
                {
                    if (existingBalOps.Contains(d.DealTicket)) continue;
                    db.BalanceOperations.Add(new BalanceOperation
                    {
                        AccountId = acc.Id, DealTicket = d.DealTicket, Amount = d.GrossProfit,
                        TimeUtc = d.CloseTimeUtc, Comment = d.Comment ?? ""
                    });
                }
                else if (d.Type is "buy" or "sell")
                {
                    if (existingTrades.Contains(d.DealTicket)) continue;
                    db.Trades.Add(new Trade
                    {
                        AccountId = acc.Id, DealTicket = d.DealTicket, PositionId = d.PositionId,
                        Symbol = d.Symbol, Direction = d.Type, Lots = d.Lots,
                        OpenPrice = d.OpenPrice, ClosePrice = d.ClosePrice,
                        OpenTimeUtc = DateTime.SpecifyKind(d.OpenTimeUtc, DateTimeKind.Utc),
                        CloseTimeUtc = DateTime.SpecifyKind(d.CloseTimeUtc, DateTimeKind.Utc),
                        GrossProfit = d.GrossProfit, Commission = d.Commission, Swap = d.Swap,
                        NetProfit = d.GrossProfit + d.Commission + d.Swap,
                        MagicNumber = d.MagicNumber, Comment = d.Comment ?? ""
                    });
                }
            }
            acc.LastIngestAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { received = batch.Deals.Length });
        });
    }
}
```

- [ ] **Step 4: Run tests** — Expected: all PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: idempotent deal ingestion endpoint"`

---

### Task 6: Accounts endpoints

**Files:**
- Create: `backend/src/ProfitHub.Api/Features/Accounts.cs` (register in Endpoints)
- Test: `backend/tests/ProfitHub.Api.Tests/AccountsTests.cs`
- Create test helper: `backend/tests/ProfitHub.Api.Tests/AuthedClient.cs`

- [ ] **Step 1: Test helper that registers+logs in and returns an authed client**

```csharp
// AuthedClient.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public static class AuthedClient
{
    public static async Task<HttpClient> Create(ApiFactory f)
    {
        var client = f.CreateClient();
        var email = Guid.NewGuid() + "@x.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["token"];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

- [ ] **Step 2: Failing tests**

```csharp
// AccountsTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.json; // remove if unused

namespace ProfitHub.Api.Tests;

public class AccountsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record AccountDto(Guid Id, long AccountNumber, string Name, string Broker,
        string Currency, string IngestKey, DateTime? LastIngestAtUtc);

    [Fact]
    public async Task Create_account_returns_ingest_key_and_lists_it()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 111, name = "Main", broker = "IC" });
        var created = await res.Content.ReadFromJsonAsync<AccountDto>();
        Assert.False(string.IsNullOrEmpty(created!.IngestKey));
        var list = await client.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_accounts()
    {
        var a = await AuthedClient.Create(_f);
        var b = await AuthedClient.Create(_f);
        await a.PostAsJsonAsync("/api/accounts", new { accountNumber = 111, name = "A", broker = "" });
        var list = await b.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var res = await _f.CreateClient().GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
```

- [ ] **Step 3: Run, expect FAIL. Then implement**

```csharp
// Features/Accounts.cs
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Accounts
{
    public record CreateReq(long AccountNumber, string Name, string Broker);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/accounts").RequireAuthorization();

        g.MapGet("/", async (System.Security.Claims.ClaimsPrincipal user, AppDbContext db) =>
            await db.Accounts.Where(a => a.UserId == user.UserId())
                .Select(a => new { a.Id, a.AccountNumber, a.Name, a.Broker, a.Currency, a.IngestKey, a.LastIngestAtUtc })
                .ToListAsync());

        g.MapPost("/", async (CreateReq req, System.Security.Claims.ClaimsPrincipal user, AppDbContext db) =>
        {
            var acc = new Account
            {
                UserId = user.UserId(), AccountNumber = req.AccountNumber,
                Name = req.Name, Broker = req.Broker,
                IngestKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
            };
            db.Accounts.Add(acc);
            await db.SaveChangesAsync();
            return Results.Ok(new { acc.Id, acc.AccountNumber, acc.Name, acc.Broker, acc.Currency, acc.IngestKey, acc.LastIngestAtUtc });
        });

        g.MapDelete("/{id:guid}", async (Guid id, System.Security.Claims.ClaimsPrincipal user, AppDbContext db) =>
        {
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.UserId());
            if (acc is null) return Results.NotFound();
            db.Remove(acc);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
```

- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: accounts CRUD with per-account ingest keys"`

---

### Task 7: Trades query + summary endpoints (timezone-aware grouping)

**Files:**
- Create: `backend/src/ProfitHub.Api/Features/Reports.cs` (register in Endpoints)
- Test: `backend/tests/ProfitHub.Api.Tests/ReportsTests.cs`

Endpoints:
- `GET /api/trades?accountIds=<csv>&from=&to=&magic=` → paged trade list (newest first, `page`/`pageSize`, default 50)
- `GET /api/summary?period=day|week|month&accountIds=&from=&to=&magic=` → rows `{ periodStart, netProfit, tradeCount, wins }`
- `GET /api/summary/by-ea?accountIds=&from=&to=` → rows `{ magicNumber, name, netProfit, tradeCount }`

Grouping rule (CONTEXT.md / Q7): convert `CloseTimeUtc` to the user's `TimeZone` (claim `tz`), bucket by local calendar day / ISO week (Monday start) / calendar month. Do grouping **in memory** after fetching the filtered rows — volumes are small (thousands of trades) and this keeps it portable across SQLite/Postgres.

- [ ] **Step 1: Failing tests**

```csharp
// ReportsTests.cs
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class ReportsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    public record SummaryRow(DateOnly PeriodStart, decimal NetProfit, int TradeCount, int Wins);

    private async Task<(HttpClient client, Guid accId)> SeedAsync(params (long ticket, string closeUtc, decimal net)[] trades)
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 111, name = "A", broker = "" });
        var acc = await res.Content.ReadFromJsonAsync<AccountsTests.AccountDto>();
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (ticket, closeUtc, net) in trades)
            db.Trades.Add(new Trade
            {
                AccountId = acc!.Id, DealTicket = ticket, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = DateTime.Parse(closeUtc).ToUniversalTime(),
                GrossProfit = net, NetProfit = net, MagicNumber = 1
            });
        await db.SaveChangesAsync();
        return (client, acc!.Id);
    }

    [Fact]
    public async Task Trade_closing_after_midnight_bangkok_lands_on_the_next_day()
    {
        // 2026-05-27 17:30 UTC = 2026-05-28 00:30 Asia/Bangkok
        var (client, accId) = await SeedAsync(
            (1, "2026-05-27T17:30:00Z", 10m),
            (2, "2026-05-27T16:30:00Z", 5m)); // 23:30 Bangkok, still 27th
        var rows = await client.GetFromJsonAsync<SummaryRow[]>($"/api/summary?period=day&accountIds={accId}");
        Assert.Equal(2, rows!.Length);
        Assert.Contains(rows, r => r.PeriodStart == new DateOnly(2026, 5, 28) && r.NetProfit == 10m);
        Assert.Contains(rows, r => r.PeriodStart == new DateOnly(2026, 5, 27) && r.NetProfit == 5m);
    }

    [Fact]
    public async Task Monthly_summary_aggregates_and_counts_wins()
    {
        var (client, accId) = await SeedAsync(
            (1, "2026-05-10T10:00:00Z", 10m), (2, "2026-05-11T10:00:00Z", -4m), (3, "2026-06-01T10:00:00Z", 7m));
        var rows = await client.GetFromJsonAsync<SummaryRow[]>($"/api/summary?period=month&accountIds={accId}");
        var may = rows!.Single(r => r.PeriodStart == new DateOnly(2026, 5, 1));
        Assert.Equal(6m, may.NetProfit);
        Assert.Equal(2, may.TradeCount);
        Assert.Equal(1, may.Wins);
    }

    [Fact]
    public async Task Trades_endpoint_filters_by_account()
    {
        var (client, accId) = await SeedAsync((1, "2026-05-10T10:00:00Z", 10m));
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/trades?accountIds={accId}");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
    }
}
```

- [ ] **Step 2: Run, expect FAIL. Then implement**

```csharp
// Features/Reports.cs
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Reports
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();
        g.MapGet("/trades", GetTrades);
        g.MapGet("/summary", GetSummary);
        g.MapGet("/summary/by-ea", GetByEa);
    }

    internal static IQueryable<Trade> Filtered(AppDbContext db, ClaimsPrincipal user,
        string? accountIds, DateTime? from, DateTime? to, long? magic)
    {
        var myAccounts = db.Accounts.Where(a => a.UserId == user.UserId()).Select(a => a.Id);
        var q = db.Trades.Where(t => myAccounts.Contains(t.AccountId));
        if (!string.IsNullOrEmpty(accountIds))
        {
            var ids = accountIds.Split(',').Select(Guid.Parse).ToArray();
            q = q.Where(t => ids.Contains(t.AccountId));
        }
        if (from is not null) q = q.Where(t => t.CloseTimeUtc >= from);
        if (to is not null) q = q.Where(t => t.CloseTimeUtc < to);
        if (magic is not null) q = q.Where(t => t.MagicNumber == magic);
        return q;
    }

    internal static TimeZoneInfo Tz(ClaimsPrincipal user) =>
        TimeZoneInfo.FindSystemTimeZoneById(user.FindFirstValue("tz") ?? "Asia/Bangkok");

    internal static DateOnly Bucket(DateTime closeUtc, TimeZoneInfo tz, string period)
    {
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(closeUtc, tz));
        return period switch
        {
            "week" => local.AddDays(-(((int)local.DayOfWeek + 6) % 7)), // Monday start
            "month" => new DateOnly(local.Year, local.Month, 1),
            _ => local,
        };
    }

    private static async Task<IResult> GetTrades(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to, long? magic, int page = 1, int pageSize = 50)
    {
        var q = Filtered(db, user, accountIds, from, to, magic).OrderByDescending(t => t.CloseTimeUtc);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Results.Ok(new { total, items });
    }

    private static async Task<IResult> GetSummary(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day")
    {
        var tz = Tz(user);
        var trades = await Filtered(db, user, accountIds, from, to, magic)
            .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
        var rows = trades
            .GroupBy(t => Bucket(t.CloseTimeUtc, tz, period))
            .OrderByDescending(grp => grp.Key)
            .Select(grp => new
            {
                periodStart = grp.Key,
                netProfit = grp.Sum(t => t.NetProfit),
                tradeCount = grp.Count(),
                wins = grp.Count(t => t.NetProfit > 0)
            });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetByEa(ClaimsPrincipal user, AppDbContext db,
        string? accountIds, DateTime? from, DateTime? to)
    {
        var names = await db.EaNames.Where(e => e.UserId == user.UserId())
            .ToDictionaryAsync(e => e.MagicNumber, e => e.Name);
        var trades = await Filtered(db, user, accountIds, from, to, null)
            .Select(t => new { t.MagicNumber, t.NetProfit }).ToListAsync();
        var rows = trades.GroupBy(t => t.MagicNumber)
            .Select(grp => new
            {
                magicNumber = grp.Key,
                name = names.GetValueOrDefault(grp.Key, grp.Key.ToString()),
                netProfit = grp.Sum(t => t.NetProfit),
                tradeCount = grp.Count()
            })
            .OrderByDescending(r => r.netProfit);
        return Results.Ok(rows);
    }
}
```

- [ ] **Step 3: Run tests** — Expected: PASS.
- [ ] **Step 4: Commit** — `git commit -am "feat: trades query and timezone-aware summaries"`

---### Task 8: EA name mapping endpoints

**Files:**
- Create: `backend/src/ProfitHub.Api/Features/EaNames.cs` (register in Endpoints)
- Test: `backend/tests/ProfitHub.Api.Tests/EaNamesTests.cs`

- [ ] **Step 1: Failing test**

```csharp
// EaNamesTests.cs
using System.Net.Http.Json;

namespace ProfitHub.Api.Tests;

public class EaNamesTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    [Fact]
    public async Task Put_then_get_returns_name_and_upserts()
    {
        var client = await AuthedClient.Create(_f);
        await client.PutAsJsonAsync("/api/ea-names/20231", new { name = "Quantum Queen" });
        await client.PutAsJsonAsync("/api/ea-names/20231", new { name = "QQ v2" });
        var list = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/ea-names");
        Assert.Single(list!);
        Assert.Equal("QQ v2", list![0]["name"].ToString());
    }
}
```

- [ ] **Step 2: Implement**

```csharp
// Features/EaNames.cs
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class EaNames
{
    public record NameReq(string Name);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/ea-names").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            await db.EaNames.Where(e => e.UserId == user.UserId())
                .Select(e => new { magicNumber = e.MagicNumber, name = e.Name }).ToListAsync());

        g.MapPut("/{magic:long}", async (long magic, NameReq req, ClaimsPrincipal user, AppDbContext db) =>
        {
            var row = await db.EaNames.FirstOrDefaultAsync(e => e.UserId == user.UserId() && e.MagicNumber == magic);
            if (row is null) db.EaNames.Add(new EaName { UserId = user.UserId(), MagicNumber = magic, Name = req.Name });
            else row.Name = req.Name;
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
```

- [ ] **Step 3: Run tests, expect PASS. Commit** — `git commit -am "feat: EA name mapping"`

---

### Task 9: CSV export endpoints

**Files:**
- Create: `backend/src/ProfitHub.Api/Features/Export.cs` (register in Endpoints)
- Test: `backend/tests/ProfitHub.Api.Tests/ExportTests.cs`

Endpoints (same filters as Task 7):
- `GET /api/export/trades.csv` — columns: `CloseTime(Local),Account,Symbol,Direction,Lots,OpenPrice,ClosePrice,GrossProfit,Commission,Swap,NetProfit,MagicNumber,Comment`
- `GET /api/export/summary.csv?period=day|week|month` — columns: `PeriodStart,NetProfit,TradeCount,Wins,WinRate`

- [ ] **Step 1: Failing test**

```csharp
// ExportTests.cs
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Tests;

public class ExportTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f = f;

    [Fact]
    public async Task Trades_csv_has_header_and_rows()
    {
        var client = await AuthedClient.Create(_f);
        var res = await client.PostAsJsonAsync("/api/accounts", new { accountNumber = 1, name = "A", broker = "" });
        var acc = await res.Content.ReadFromJsonAsync<AccountsTests.AccountDto>();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Trades.Add(new Trade { AccountId = acc!.Id, DealTicket = 1, Symbol = "XAUUSD", Direction = "buy",
                CloseTimeUtc = DateTime.UtcNow, NetProfit = 12.69m });
            db.SaveChanges();
        }
        var csv = await client.GetStringAsync("/api/export/trades.csv");
        var lines = csv.Trim().Split('\n');
        Assert.StartsWith("CloseTime", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains("12.69", lines[1]);
    }
}
```

- [ ] **Step 2: Implement**

```csharp
// Features/Export.cs
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Export
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/export").RequireAuthorization();

        g.MapGet("/trades.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic) =>
        {
            var tz = Reports.Tz(user);
            var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
                .ToDictionaryAsync(a => a.Id, a => a.Name);
            var trades = await Reports.Filtered(db, user, accountIds, from, to, magic)
                .OrderBy(t => t.CloseTimeUtc).ToListAsync();
            var sb = new StringBuilder("CloseTime,Account,Symbol,Direction,Lots,OpenPrice,ClosePrice,GrossProfit,Commission,Swap,NetProfit,MagicNumber,Comment\n");
            foreach (var t in trades)
                sb.AppendLine(string.Join(',',
                    TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz).ToString("yyyy-MM-dd HH:mm:ss"),
                    Csv(accounts.GetValueOrDefault(t.AccountId, "")), Csv(t.Symbol), t.Direction, t.Lots,
                    t.OpenPrice, t.ClosePrice, t.GrossProfit, t.Commission, t.Swap, t.NetProfit,
                    t.MagicNumber, Csv(t.Comment)));
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "trades.csv");
        });

        g.MapGet("/summary.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day") =>
        {
            var tz = Reports.Tz(user);
            var trades = await Reports.Filtered(db, user, accountIds, from, to, magic)
                .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
            var sb = new StringBuilder("PeriodStart,NetProfit,TradeCount,Wins,WinRate\n");
            foreach (var grp in trades.GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, period)).OrderBy(x => x.Key))
            {
                var wins = grp.Count(t => t.NetProfit > 0);
                sb.AppendLine($"{grp.Key:yyyy-MM-dd},{grp.Sum(t => t.NetProfit)},{grp.Count()},{wins},{(double)wins / grp.Count():P1}");
            }
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"summary-{period}.csv");
        });
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
```

- [ ] **Step 3: Run all backend tests** — `dotnet test backend/ProfitHub.sln` — Expected: all PASS.
- [ ] **Step 4: Commit** — `git commit -am "feat: CSV export for trades and summaries"`

---

### Task 10: MQL5 collector EA

**Files:**
- Create: `ea/ProfitHubCollector.mq5`
- Create: `ea/README.md` (install guide, Thai)

No automated tests possible (needs MT5 terminal) — verification is manual: compile in MetaEditor (0 errors), attach to a chart on a demo account, confirm rows appear via `GET /api/trades`.

Safety requirements from ADR 0001 are embedded: `OnTimer` only, read-only, delta fetch via last-pushed time persisted in a global variable, 5 s timeout, no retry loops.

- [ ] **Step 1: Write the EA**

```mql5
//+------------------------------------------------------------------+
//| ProfitHubCollector.mq5                                           |
//| Pushes closed deals to Profit Hub. Read-only; never trades.      |
//| Attach to ONE chart per terminal. Whitelist ApiUrl in            |
//| Tools > Options > Expert Advisors > Allow WebRequest.            |
//+------------------------------------------------------------------+
#property strict

input string ApiUrl        = "https://your-api.onrender.com"; // Profit Hub API base URL
input string IngestKey     = "";                               // per-account Ingest Key
input int    IntervalSec   = 120;                              // push every N seconds (1-5 min)
input int    BatchSize     = 100;                              // deals per HTTP request

string GV_LAST; // global variable name persisting last pushed deal time

int OnInit()
{
   if(StringLen(IngestKey) == 0) { Print("ProfitHub: IngestKey is empty"); return INIT_PARAMETERS_INCORRECT; }
   GV_LAST = "PH_LAST_" + (string)AccountInfoInteger(ACCOUNT_LOGIN);
   EventSetTimer(MathMax(60, IntervalSec));
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason) { EventKillTimer(); }

void OnTimer()
{
   // Delta fetch: from last successfully pushed deal time (0 on first run = full backfill)
   datetime from = (datetime)(GlobalVariableCheck(GV_LAST) ? GlobalVariableGet(GV_LAST) : 0);
   if(!HistorySelect(from + 1, TimeCurrent())) return;

   int total = HistoryDealsTotal();
   string json = "";
   int count = 0;
   datetime maxTime = from;

   for(int i = 0; i < total; i++)
   {
      ulong ticket = HistoryDealGetTicket(i);
      long entry = HistoryDealGetInteger(ticket, DEAL_ENTRY);
      long type  = HistoryDealGetInteger(ticket, DEAL_TYPE);
      datetime t = (datetime)HistoryDealGetInteger(ticket, DEAL_TIME);

      string typeStr = "";
      if(type == DEAL_TYPE_BALANCE) typeStr = "balance";
      else if(entry == DEAL_ENTRY_OUT || entry == DEAL_ENTRY_INOUT || entry == DEAL_ENTRY_OUT_BY)
         typeStr = (type == DEAL_TYPE_BUY) ? "sell" : "buy"; // closing deal direction is opposite of position
      else continue; // skip entry-in deals; we record positions when they close

      // open info from the position's entry deal
      long posId = HistoryDealGetInteger(ticket, DEAL_POSITION_ID);
      double openPrice = 0; datetime openTime = t;
      if(typeStr != "balance" && HistorySelectByPosition(posId))
      {
         for(int k = 0; k < HistoryDealsTotal(); k++)
         {
            ulong tk = HistoryDealGetTicket(k);
            if(HistoryDealGetInteger(tk, DEAL_ENTRY) == DEAL_ENTRY_IN)
            { openPrice = HistoryDealGetDouble(tk, DEAL_PRICE);
              openTime = (datetime)HistoryDealGetInteger(tk, DEAL_TIME); break; }
         }
         HistorySelect(from + 1, TimeCurrent()); // restore outer selection
      }

      if(count > 0) json += ",";
      json += StringFormat(
        "{\"dealTicket\":%I64u,\"positionId\":%I64d,\"symbol\":\"%s\",\"type\":\"%s\","
        "\"lots\":%.2f,\"openPrice\":%.5f,\"closePrice\":%.5f,"
        "\"openTimeUtc\":\"%s\",\"closeTimeUtc\":\"%s\","
        "\"grossProfit\":%.2f,\"commission\":%.2f,\"swap\":%.2f,"
        "\"magicNumber\":%I64d,\"comment\":\"%s\"}",
        ticket, posId, HistoryDealGetString(ticket, DEAL_SYMBOL), typeStr,
        HistoryDealGetDouble(ticket, DEAL_VOLUME), openPrice, HistoryDealGetDouble(ticket, DEAL_PRICE),
        ToIsoUtc(openTime), ToIsoUtc(t),
        HistoryDealGetDouble(ticket, DEAL_PROFIT), HistoryDealGetDouble(ticket, DEAL_COMMISSION),
        HistoryDealGetDouble(ticket, DEAL_SWAP),
        HistoryDealGetInteger(ticket, DEAL_MAGIC), HistoryDealGetString(ticket, DEAL_COMMENT));

      if(t > maxTime) maxTime = t;
      count++;
      if(count >= BatchSize) break; // remaining deals go next timer tick
   }

   if(count == 0) return;
   if(Push("[" + json + "]"))
      GlobalVariableSet(GV_LAST, (double)maxTime); // advance watermark only on success
}

string ToIsoUtc(datetime serverTime)
{
   // Server time -> UTC using the broker's current GMT offset
   datetime utc = serverTime - (TimeTradeServer() - TimeGMT());
   return TimeToString(utc, TIME_DATE|TIME_SECONDS); // backend parses "yyyy.MM.dd HH:mm:ss" as UTC
}

bool Push(string dealsJson)
{
   string body = "{\"deals\":" + dealsJson + "}";
   char data[], result[];
   StringToCharArray(body, data, 0, StringLen(body));
   string headers = "Content-Type: application/json\r\nX-Ingest-Key: " + IngestKey + "\r\n";
   string respHeaders;
   ResetLastError();
   int status = WebRequest("POST", ApiUrl + "/api/ingest/deals", headers, 5000, data, result, respHeaders);
   if(status == 200) return true;
   PrintFormat("ProfitHub: push failed status=%d err=%d (will retry next cycle)", status, GetLastError());
   return false; // no retry loop; idempotency makes the next cycle safe (ADR 0001)
}
```

> **Backend note:** MQL5 `TimeToString` produces `2026.05.28 03:06:00`. Add a small normalisation in `Ingest.DealDto` binding **or** simpler: in the EA replace `TimeToString` output dots/space — append to `ToIsoUtc`:
> ```mql5
>    string s = TimeToString(utc, TIME_DATE|TIME_SECONDS);
>    StringReplace(s, ".", "-"); StringReplace(s, " ", "T");
>    return s + "Z";
> ```
> Use this version (ISO 8601) — no backend change needed.

- [ ] **Step 2: Write `ea/README.md`** — Thai install guide: copy `.mq5` to `MQL5/Experts`, compile, whitelist API URL in Tools→Options→Expert Advisors, open a spare chart, attach EA, paste Ingest Key from the Accounts page, enable Algo Trading. Note: runs alongside trading EAs on other charts safely (read-only, own thread).

- [ ] **Step 3: Manual verification checklist** (do at the end, with a demo account): compiles with 0 errors; first attach backfills full history; `Accounts` page shows `LastIngestAt` updating; killing the network mid-push loses nothing (next cycle re-sends).

- [ ] **Step 4: Commit** — `git add ea && git commit -m "feat: MQL5 collector EA with delta fetch and backfill"`

---

### Task 11: Angular scaffold + auth

**Files:** Create `frontend/` (Angular 18, standalone, routing, SCSS).

- [ ] **Step 1: Scaffold**

```bash
cd /Users/pack/Workspace/profit-hub
npx -y @angular/cli@18 new frontend --routing --style=scss --ssr=false --skip-git
cd frontend && npm i chart.js
```

- [ ] **Step 2: Core auth plumbing**

```typescript
// src/app/core/api.service.ts
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(private http: HttpClient) {}
  get<T>(path: string, params?: Record<string, string>) {
    return this.http.get<T>(`${environment.apiUrl}${path}`, { params });
  }
  post<T>(path: string, body: unknown) {
    return this.http.post<T>(`${environment.apiUrl}${path}`, body);
  }
  put<T>(path: string, body: unknown) {
    return this.http.put<T>(`${environment.apiUrl}${path}`, body);
  }
  delete<T>(path: string) {
    return this.http.delete<T>(`${environment.apiUrl}${path}`);
  }
  downloadUrl(path: string, params: URLSearchParams) {
    return `${environment.apiUrl}${path}?${params}`;
  }
}
```

```typescript
// src/app/core/auth.service.ts
import { Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  token = signal<string | null>(localStorage.getItem('ph_token'));
  constructor(private api: ApiService) {}
  async login(email: string, password: string) {
    const res = await firstValueFrom(this.api.post<{ token: string }>('/api/auth/login', { email, password }));
    localStorage.setItem('ph_token', res.token);
    this.token.set(res.token);
  }
  async register(email: string, password: string) {
    await firstValueFrom(this.api.post('/api/auth/register', { email, password }));
    await this.login(email, password);
  }
  logout() { localStorage.removeItem('ph_token'); this.token.set(null); }
}
```

```typescript
// src/app/core/auth.interceptor.ts
import { HttpInterceptorFn } from '@angular/common/http';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('ph_token');
  return next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);
};
```

```typescript
// src/app/core/auth.guard.ts
import { inject } from '@angular/core';
import { Router } from '@angular/router';

export const authGuard = () => {
  if (localStorage.getItem('ph_token')) return true;
  return inject(Router).createUrlTree(['/login']);
};
```

```typescript
// src/app/app.config.ts
import { ApplicationConfig } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [provideRouter(routes), provideHttpClient(withInterceptors([authInterceptor]))],
};
```

```typescript
// src/app/app.routes.ts
import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent) },
  {
    path: '', canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'trades', loadComponent: () => import('./pages/trades/trades.component').then(m => m.TradesComponent) },
      { path: 'accounts', loadComponent: () => import('./pages/accounts/accounts.component').then(m => m.AccountsComponent) },
    ],
  },
];
```

Set `environment.apiUrl` to `http://localhost:5000` (dev) / Render URL (prod) via `src/environments/environment.ts` and `environment.development.ts` (create the folder; wire `fileReplacements` in `angular.json` if the CLI didn't).

- [ ] **Step 3: Login page**

```typescript
// src/app/pages/login/login.component.ts
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'ph-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="login-card">
      <h1>Profit Hub</h1>
      <input [(ngModel)]="email" placeholder="Email" type="email" />
      <input [(ngModel)]="password" placeholder="Password" type="password" />
      @if (error()) { <p class="error">{{ error() }}</p> }
      <button (click)="submit(false)">Login</button>
      <button class="secondary" (click)="submit(true)">Register</button>
    </div>
  `,
  styles: [`.login-card{max-width:320px;margin:15vh auto;display:flex;flex-direction:column;gap:.75rem}
            .error{color:#e5484d;margin:0}`],
})
export class LoginComponent {
  email = ''; password = '';
  error = signal('');
  constructor(private auth: AuthService, private router: Router) {}
  async submit(register: boolean) {
    try {
      register ? await this.auth.register(this.email, this.password)
               : await this.auth.login(this.email, this.password);
      this.router.navigate(['/']);
    } catch { this.error.set(register ? 'Registration failed' : 'Wrong email or password'); }
  }
}
```

- [ ] **Step 4: Shell layout** — `src/app/layout/shell.component.ts`: sidebar with links Dashboard / Trades / Accounts + logout button + `<router-outlet/>`. Dark theme base styles in `styles.scss` (dark background `#0f1217`, green `#30a46c` for profit, red `#e5484d` for loss).

```typescript
// src/app/layout/shell.component.ts
import { Component } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'ph-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <nav>
        <h2>Profit Hub</h2>
        <a routerLink="/" [routerLinkActiveOptions]="{exact:true}" routerLinkActive="active">Dashboard</a>
        <a routerLink="/trades" routerLinkActive="active">Trades</a>
        <a routerLink="/accounts" routerLinkActive="active">Accounts</a>
        <button (click)="logout()">Sign out</button>
      </nav>
      <main><router-outlet /></main>
    </div>
  `,
  styles: [`.shell{display:grid;grid-template-columns:200px 1fr;min-height:100vh}
            nav{display:flex;flex-direction:column;gap:.5rem;padding:1rem;border-right:1px solid #2a2f3a}
            nav a.active{color:#30a46c} main{padding:1.5rem}`],
})
export class ShellComponent {
  constructor(private auth: AuthService, private router: Router) {}
  logout() { this.auth.logout(); this.router.navigate(['/login']); }
}
```

- [ ] **Step 5: Verify** — `npm run build` in `frontend/` — Expected: build succeeds. Run `ng serve`, log in against locally running API (`dotnet run` with a local Postgres or Neon dev branch), see empty shell.

- [ ] **Step 6: Commit** — `git add frontend && git commit -m "feat: Angular scaffold with auth and shell"`

---

### Task 12: Shared filter state + Accounts page

**Files:**
- Create: `frontend/src/app/core/filter.service.ts`
- Create: `frontend/src/app/pages/accounts/accounts.component.ts`

- [ ] **Step 1: Filter service** — the multi-select account filter shared by all pages (Q5 decision):

```typescript
// src/app/core/filter.service.ts
import { Injectable, computed, signal } from '@angular/core';

export interface AccountInfo {
  id: string; accountNumber: number; name: string; broker: string;
  currency: string; ingestKey: string; lastIngestAtUtc: string | null;
}

@Injectable({ providedIn: 'root' })
export class FilterService {
  accounts = signal<AccountInfo[]>([]);
  selectedIds = signal<string[]>([]);          // empty = all accounts
  magic = signal<number | null>(null);          // EA filter
  from = signal<string | null>(null);           // ISO date
  to = signal<string | null>(null);

  queryParams = computed(() => {
    const p: Record<string, string> = {};
    if (this.selectedIds().length) p['accountIds'] = this.selectedIds().join(',');
    if (this.magic() !== null) p['magic'] = String(this.magic());
    if (this.from()) p['from'] = this.from()!;
    if (this.to()) p['to'] = this.to()!;
    return p;
  });
}
```

- [ ] **Step 2: Accounts page** — list accounts, add account (number/name/broker), reveal Ingest Key with copy button, show `lastIngestAtUtc` with a stale warning (red if > 15 min ago), delete with confirm.

```typescript
// src/app/pages/accounts/accounts.component.ts
import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { FilterService, AccountInfo } from '../../core/filter.service';

@Component({
  selector: 'ph-accounts',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <h1>Accounts</h1>
    <table>
      <tr><th>Number</th><th>Name</th><th>Broker</th><th>Ingest Key</th><th>Last data</th><th></th></tr>
      @for (a of filter.accounts(); track a.id) {
        <tr>
          <td>{{ a.accountNumber }}</td><td>{{ a.name }}</td><td>{{ a.broker }}</td>
          <td><code (click)="copy(a.ingestKey)" title="Click to copy">{{ a.ingestKey.slice(0, 8) }}…</code></td>
          <td [class.stale]="isStale(a)">{{ a.lastIngestAtUtc ? (a.lastIngestAtUtc + 'Z' | date:'short') : 'never' }}</td>
          <td><button (click)="remove(a)">Delete</button></td>
        </tr>
      }
    </table>
    <h2>Add account</h2>
    <input [(ngModel)]="num" placeholder="MT5 account number" type="number" />
    <input [(ngModel)]="name" placeholder="Name" />
    <input [(ngModel)]="broker" placeholder="Broker" />
    <button (click)="add()">Add</button>
    @if (copied()) { <span>Copied!</span> }
  `,
  styles: ['.stale{color:#e5484d}'],
})
export class AccountsComponent implements OnInit {
  num = 0; name = ''; broker = '';
  copied = signal(false);
  constructor(private api: ApiService, public filter: FilterService) {}

  async ngOnInit() { await this.reload(); }
  async reload() {
    this.filter.accounts.set(await firstValueFrom(this.api.get<AccountInfo[]>('/api/accounts')));
  }
  async add() {
    await firstValueFrom(this.api.post('/api/accounts', { accountNumber: this.num, name: this.name, broker: this.broker }));
    this.num = 0; this.name = ''; this.broker = '';
    await this.reload();
  }
  async remove(a: AccountInfo) {
    if (!confirm(`Delete account ${a.accountNumber} and all its trades?`)) return;
    await firstValueFrom(this.api.delete(`/api/accounts/${a.id}`));
    await this.reload();
  }
  copy(key: string) { navigator.clipboard.writeText(key); this.copied.set(true); setTimeout(() => this.copied.set(false), 1500); }
  isStale(a: AccountInfo) { return !a.lastIngestAtUtc || Date.now() - Date.parse(a.lastIngestAtUtc + 'Z') > 15 * 60_000; }
}
```

- [ ] **Step 3: Verify** — `ng serve`, add an account, copy key. Expected: row appears, key copies.
- [ ] **Step 4: Commit** — `git commit -am "feat: accounts page and shared account filter"`

---

### Task 13: Account/EA filter bar component

**Files:** Create `frontend/src/app/shared/filter-bar.component.ts`

- [ ] **Step 1: Implement** — checkbox multi-select of accounts + EA dropdown + date range; writes into `FilterService`; emits `(changed)` so pages reload.

```typescript
// src/app/shared/filter-bar.component.ts
import { Component, EventEmitter, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { FilterService, AccountInfo } from '../core/filter.service';

@Component({
  selector: 'ph-filter-bar',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="bar">
      <details>
        <summary>{{ label() }}</summary>
        @for (a of filter.accounts(); track a.id) {
          <label><input type="checkbox" [checked]="isSelected(a.id)" (change)="toggle(a.id)" />
            {{ a.name || a.accountNumber }}</label>
        }
      </details>
      <select [ngModel]="filter.magic()" (ngModelChange)="setMagic($event)">
        <option [ngValue]="null">All EAs</option>
        @for (ea of eas; track ea.magicNumber) { <option [ngValue]="ea.magicNumber">{{ ea.name }}</option> }
      </select>
      <input type="date" [ngModel]="filter.from()" (ngModelChange)="filter.from.set($event); changed.emit()" />
      <input type="date" [ngModel]="filter.to()" (ngModelChange)="filter.to.set($event); changed.emit()" />
    </div>
  `,
  styles: ['.bar{display:flex;gap:1rem;align-items:center;margin-bottom:1rem}'],
})
export class FilterBarComponent implements OnInit {
  @Output() changed = new EventEmitter<void>();
  eas: { magicNumber: number; name: string }[] = [];
  constructor(public filter: FilterService, private api: ApiService) {}

  async ngOnInit() {
    if (!this.filter.accounts().length)
      this.filter.accounts.set(await firstValueFrom(this.api.get<AccountInfo[]>('/api/accounts')));
    this.eas = await firstValueFrom(this.api.get<{ magicNumber: number; name: string }[]>('/api/summary/by-ea'));
  }
  isSelected(id: string) { return this.filter.selectedIds().includes(id); }
  toggle(id: string) {
    const ids = this.filter.selectedIds();
    this.filter.selectedIds.set(ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]);
    this.changed.emit();
  }
  setMagic(m: number | null) { this.filter.magic.set(m); this.changed.emit(); }
  label() {
    const n = this.filter.selectedIds().length;
    return n === 0 ? 'All accounts' : `${n} account(s)`;
  }
}
```

- [ ] **Step 2: Commit** — `git add frontend && git commit -m "feat: account/EA/date filter bar"`

---

### Task 14: Dashboard page

**Files:** Create `frontend/src/app/pages/dashboard/dashboard.component.ts`

Content (Q11): summary cards (today / this week / this month / all time), cumulative daily profit line chart (Chart.js), daily summary table, per-EA profit table.

- [ ] **Step 1: Implement**

```typescript
// src/app/pages/dashboard/dashboard.component.ts
import { AfterViewInit, Component, ElementRef, OnInit, ViewChild, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { Chart, registerables } from 'chart.js';
import { ApiService } from '../../core/api.service';
import { FilterService } from '../../core/filter.service';
import { FilterBarComponent } from '../../shared/filter-bar.component';

Chart.register(...registerables);

interface SummaryRow { periodStart: string; netProfit: number; tradeCount: number; wins: number; }
interface EaRow { magicNumber: number; name: string; netProfit: number; tradeCount: number; }

@Component({
  selector: 'ph-dashboard',
  standalone: true,
  imports: [FilterBarComponent, DecimalPipe],
  template: `
    <h1>Dashboard</h1>
    <ph-filter-bar (changed)="reload()" />
    <div class="cards">
      <div class="card"><span>Today</span><b [class.neg]="today() < 0">{{ today() | number:'1.2-2' }}</b></div>
      <div class="card"><span>This week</span><b [class.neg]="week() < 0">{{ week() | number:'1.2-2' }}</b></div>
      <div class="card"><span>This month</span><b [class.neg]="month() < 0">{{ month() | number:'1.2-2' }}</b></div>
      <div class="card"><span>All time</span><b [class.neg]="allTime() < 0">{{ allTime() | number:'1.2-2' }}</b></div>
    </div>
    <canvas #chart height="80"></canvas>
    <div class="tables">
      <table>
        <caption>Daily P/L</caption>
        <tr><th>Day</th><th>Net</th><th>Trades</th><th>Win %</th></tr>
        @for (r of days(); track r.periodStart) {
          <tr><td>{{ r.periodStart }}</td>
              <td [class.neg]="r.netProfit < 0">{{ r.netProfit | number:'1.2-2' }}</td>
              <td>{{ r.tradeCount }}</td>
              <td>{{ r.tradeCount ? (100 * r.wins / r.tradeCount).toFixed(0) : 0 }}%</td></tr>
        }
      </table>
      <table>
        <caption>By EA</caption>
        <tr><th>EA</th><th>Net</th><th>Trades</th></tr>
        @for (r of eas(); track r.magicNumber) {
          <tr><td>{{ r.name }}</td>
              <td [class.neg]="r.netProfit < 0">{{ r.netProfit | number:'1.2-2' }}</td>
              <td>{{ r.tradeCount }}</td></tr>
        }
      </table>
    </div>
  `,
  styles: [`.cards{display:flex;gap:1rem;margin:1rem 0}
            .card{padding:1rem;border:1px solid #2a2f3a;border-radius:8px;min-width:140px}
            .card b{display:block;font-size:1.4rem;color:#30a46c}
            b.neg,td.neg{color:#e5484d!important}
            .tables{display:flex;gap:2rem;margin-top:1.5rem;align-items:flex-start}`],
})
export class DashboardComponent implements OnInit, AfterViewInit {
  @ViewChild('chart') chartRef!: ElementRef<HTMLCanvasElement>;
  days = signal<SummaryRow[]>([]);
  eas = signal<EaRow[]>([]);
  today = signal(0); week = signal(0); month = signal(0); allTime = signal(0);
  private chart?: Chart;

  constructor(private api: ApiService, private filter: FilterService) {}
  async ngOnInit() { await this.reload(); }
  ngAfterViewInit() { this.draw(); }

  async reload() {
    const p = this.filter.queryParams();
    const [days, weeks, months, eas] = await Promise.all([
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'day' })),
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'week' })),
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'month' })),
      firstValueFrom(this.api.get<EaRow[]>('/api/summary/by-ea', p)),
    ]);
    this.days.set(days); this.eas.set(eas);
    const todayStr = new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Bangkok' });
    this.today.set(days.find(d => d.periodStart === todayStr)?.netProfit ?? 0);
    this.week.set(weeks[0]?.netProfit ?? 0);
    this.month.set(months[0]?.netProfit ?? 0);
    this.allTime.set(days.reduce((s, d) => s + d.netProfit, 0));
    this.draw();
  }

  private draw() {
    if (!this.chartRef) return;
    const asc = [...this.days()].reverse();
    let cum = 0;
    const data = asc.map(d => (cum += d.netProfit));
    this.chart?.destroy();
    this.chart = new Chart(this.chartRef.nativeElement, {
      type: 'line',
      data: { labels: asc.map(d => d.periodStart),
              datasets: [{ label: 'Cumulative Net Profit', data, borderColor: '#30a46c', tension: 0.2, pointRadius: 0 }] },
      options: { plugins: { legend: { display: false } } },
    });
  }
}
```

- [ ] **Step 2: Verify** — seed a few trades via the ingest endpoint (curl with an Ingest Key), reload dashboard. Expected: cards, chart, and tables show numbers; switching account filter changes them.
- [ ] **Step 3: Commit** — `git commit -am "feat: dashboard with summary cards, cumulative chart, daily and per-EA tables"`

---

### Task 15: Trades page + CSV export buttons

**Files:** Create `frontend/src/app/pages/trades/trades.component.ts`

- [ ] **Step 1: Implement** — paged trade table styled like the reference screenshot (green/red profit), export buttons that hit the CSV endpoints with current filters (token can't go in an `<a href>`, so fetch as blob):

```typescript
// src/app/pages/trades/trades.component.ts
import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api.service';
import { FilterService } from '../../core/filter.service';
import { FilterBarComponent } from '../../shared/filter-bar.component';

interface Trade {
  symbol: string; direction: string; lots: number; openPrice: number; closePrice: number;
  closeTimeUtc: string; netProfit: number; magicNumber: number;
}

@Component({
  selector: 'ph-trades',
  standalone: true,
  imports: [FilterBarComponent, FormsModule, DatePipe, DecimalPipe],
  template: `
    <h1>Trades</h1>
    <ph-filter-bar (changed)="load(1)" />
    <div class="actions">
      <button (click)="exportCsv('trades.csv', {})">Export trades CSV</button>
      <select [(ngModel)]="summaryPeriod">
        <option value="day">Daily</option><option value="week">Weekly</option><option value="month">Monthly</option>
      </select>
      <button (click)="exportCsv('summary.csv', { period: summaryPeriod })">Export summary CSV</button>
    </div>
    <table>
      <tr><th>Symbol</th><th>Type</th><th>Lots</th><th>Open</th><th>Close</th><th>Profit</th><th>EA</th><th>Closed</th></tr>
      @for (t of trades(); track $index) {
        <tr>
          <td>{{ t.symbol }}</td>
          <td [class.buy]="t.direction === 'buy'" [class.sell]="t.direction === 'sell'">{{ t.direction.toUpperCase() }}</td>
          <td>{{ t.lots }}</td><td>{{ t.openPrice }}</td><td>{{ t.closePrice }}</td>
          <td [class.neg]="t.netProfit < 0" class="profit">{{ t.netProfit | number:'1.2-2' }}</td>
          <td>{{ t.magicNumber }}</td>
          <td>{{ t.closeTimeUtc + 'Z' | date:'short' }}</td>
        </tr>
      }
    </table>
    <div class="pager">
      <button [disabled]="page() <= 1" (click)="load(page() - 1)">Prev</button>
      <span>{{ page() }} / {{ pages() }}</span>
      <button [disabled]="page() >= pages()" (click)="load(page() + 1)">Next</button>
    </div>
  `,
  styles: [`.buy{color:#30a46c}.sell{color:#e5484d}.profit{color:#30a46c}.neg{color:#e5484d!important}
            .actions{display:flex;gap:.5rem;margin-bottom:1rem}
            .pager{display:flex;gap:1rem;margin-top:1rem;align-items:center}`],
})
export class TradesComponent implements OnInit {
  trades = signal<Trade[]>([]);
  page = signal(1); total = signal(0);
  summaryPeriod = 'day';
  constructor(private api: ApiService, private filter: FilterService, private http: HttpClient) {}

  pages() { return Math.max(1, Math.ceil(this.total() / 50)); }
  async ngOnInit() { await this.load(1); }
  async load(page: number) {
    this.page.set(page);
    const res = await firstValueFrom(this.api.get<{ total: number; items: Trade[] }>(
      '/api/trades', { ...this.filter.queryParams(), page: String(page) }));
    this.trades.set(res.items); this.total.set(res.total);
  }
  async exportCsv(file: string, extra: Record<string, string>) {
    const params = new URLSearchParams({ ...this.filter.queryParams(), ...extra });
    const blob = await firstValueFrom(this.http.get(
      `${environment.apiUrl}/api/export/${file}?${params}`, { responseType: 'blob' }));
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob); a.download = file; a.click();
    URL.revokeObjectURL(a.href);
  }
}
```

- [ ] **Step 2: Verify** — open Trades, page through, export both CSVs, open in Excel. Expected: rows match dashboard numbers; CSV opens cleanly.
- [ ] **Step 3: Commit** — `git commit -am "feat: trades page with pagination and CSV export"`

---

### Task 16: Deployment (Render + Vercel + Neon)

**Files:**
- Create: `Dockerfile` (repo root)
- Create: `render.yaml`
- Create: `frontend/vercel.json`

- [ ] **Step 1: Dockerfile**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/ProfitHub.Api/ ProfitHub.Api/
RUN dotnet publish ProfitHub.Api -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProfitHub.Api.dll"]
```

- [ ] **Step 2: render.yaml**

```yaml
services:
  - type: web
    name: profit-hub-api
    runtime: docker
    plan: free
    healthCheckPath: /health
    envVars:
      - key: ConnectionStrings__Default
        sync: false   # paste Neon connection string in Render dashboard
      - key: Jwt__Key
        generateValue: true
      - key: Cors__Origins
        sync: false   # set to the Vercel URL, e.g. https://profit-hub.vercel.app
```

- [ ] **Step 3: frontend/vercel.json** (SPA rewrite)

```json
{
  "rewrites": [{ "source": "/(.*)", "destination": "/index.html" }]
}
```

Set `environment.ts` (prod) `apiUrl` to the Render URL.

- [ ] **Step 4: Manual deploy checklist**
  1. Create Neon project → copy pooled connection string.
  2. Push repo to GitHub; create Render Blueprint from `render.yaml`; paste connection string + CORS origin.
  3. `cd frontend && npx vercel --prod` (root directory = `frontend`).
  4. Smoke test: register → add account → curl ingest with the key → see trade on dashboard → export CSV.
  5. Update `ea/README.md` and the EA's default `ApiUrl` with the real Render URL.

- [ ] **Step 5: Commit** — `git add Dockerfile render.yaml frontend/vercel.json && git commit -m "feat: deployment config for Render and Vercel"`

---

## Self-review notes

- **Spec coverage:** ingestion+idempotency+backfill (T5, T10), auth/multi-user isolation (T4, T6), account filter (T12–13), day/week/month timezone-aware summaries (T7), per-EA breakdown + naming (T7, T8), CSV export (T9, T15), accounts page with Ingest Key + EA heartbeat (T6, T12), free-tier deploy (T16). Balance Operations stored but not surfaced in UI — deliberate: phase 1 only needs them excluded from profit, which storage separation already guarantees.
- **Type consistency:** `SummaryRow`/`EaRow` field names match the anonymous objects in `Reports.cs`; `AccountDto` matches the `Accounts.cs` projection; EA JSON fields match `Ingest.DealDto` (camelCase via default JSON binding).
- **Known seams:** `sub` claim mapping (explicitly handled via `MapInboundClaims = false` note in T4); MQL5 time format normalised to ISO 8601 in the EA itself (T10 note).
