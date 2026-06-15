using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// USD→THB exchange rate (global, not per-user). Effective rate priority:
/// manual override > cached live rate > none (frontend then hides THB).
/// Live rate is fetched daily from a free, key-less FX API and cached in the
/// single FxConfig row.
public static class Fx
{
    private const string RatesUrl = "https://open.er-api.com/v6/latest/USD";
    private static readonly TimeSpan LiveTtl = TimeSpan.FromHours(12);

    public record OverrideReq(decimal? OverrideRate);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/fx").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, IHttpClientFactory http) =>
            Results.Ok(await Resolve(db, http)));

        g.MapPut("/", async (OverrideReq req, AppDbContext db, IHttpClientFactory http) =>
        {
            if (req.OverrideRate is <= 0)
                return Results.BadRequest(new { error = "rate must be positive" });
            var cfg = await GetRow(db);
            cfg.OverrideRate = req.OverrideRate; // null clears the override
            await db.SaveChangesAsync();
            return Results.Ok(await Resolve(db, http));
        });
    }

    private static async Task<FxConfig> GetRow(AppDbContext db)
    {
        var cfg = await db.FxConfigs.FirstOrDefaultAsync();
        if (cfg is null) { cfg = new FxConfig(); db.FxConfigs.Add(cfg); await db.SaveChangesAsync(); }
        return cfg;
    }

    private static async Task<object> Resolve(AppDbContext db, IHttpClientFactory http)
    {
        var cfg = await GetRow(db);

        if (cfg.OverrideRate is { } ov)
            return Shape(ov, "override", cfg);

        var stale = cfg.LiveRateFetchedAtUtc is null
            || DateTime.UtcNow - cfg.LiveRateFetchedAtUtc > LiveTtl;
        if (cfg.LiveRate is null || stale)
            await TryRefreshLive(db, http, cfg);

        return cfg.LiveRate is { } live
            ? Shape(live, "live", cfg)
            : Shape(null, "none", cfg);
    }

    private static async Task TryRefreshLive(AppDbContext db, IHttpClientFactory http, FxConfig cfg)
    {
        try
        {
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            using var doc = JsonDocument.Parse(await client.GetStringAsync(RatesUrl));
            if (doc.RootElement.TryGetProperty("rates", out var rates)
                && rates.TryGetProperty("THB", out var thb)
                && thb.TryGetDecimal(out var rate) && rate > 0)
            {
                cfg.LiveRate = rate;
                cfg.LiveRateFetchedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch
        {
            // Network/parse failure: keep any previously cached rate; frontend hides THB if none.
        }
    }

    private static object Shape(decimal? rate, string source, FxConfig cfg) => new
    {
        rate,
        source, // "override" | "live" | "none"
        overrideRate = cfg.OverrideRate,
        liveRate = cfg.LiveRate,
        fetchedAtUtc = cfg.LiveRateFetchedAtUtc,
    };
}
