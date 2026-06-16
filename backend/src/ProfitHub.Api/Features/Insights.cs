using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// AI Weekly Coach. On demand, generates a Thai-language narrative comparing the
/// CURRENT period vs the PREVIOUS one (week or month), scoped to the user's
/// accounts, by calling Claude via the documented REST endpoint. The result is
/// cached per (user, period) so reopening the dashboard shows the last analysis
/// without re-spending tokens.
///
/// LLM call uses raw HTTP (IHttpClientFactory, same pattern as Fx.cs) rather
/// than an SDK: a single server-side message, no extra dependency to align.
public static class Insights
{
    private const string ClaudeUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-opus-4-8";

    private const string SystemPrompt =
        "คุณเป็นนักวิเคราะห์ผลการเทรดที่พูดภาษาไทย กระชับและตรงประเด็น " +
        "เขียนบทวิเคราะห์เชิงเล่าเรื่องสั้น ๆ (ไม่กี่ย่อหน้าสั้น ๆ หรือเป็นบูลเล็ต) " +
        "เปรียบเทียบช่วงเวลาปัจจุบันกับช่วงก่อนหน้า โดยชี้ให้เห็นว่าบัญชี/EA ใดดีขึ้นหรือแย่ลง " +
        "การเปลี่ยนแปลงของอัตราชนะ (win rate) และกำไร/ขาดทุนสุทธิ รวมถึงวันที่ดีที่สุดและแย่ที่สุด " +
        "ห้ามให้คำแนะนำการลงทุน ห้ามบอกให้ผู้ใช้เพิ่ม/ถอนเงินทุน หรือเปลี่ยนกลยุทธ์ " +
        "ให้บรรยายเฉพาะสิ่งที่ตัวเลขแสดงเท่านั้น ตอบเป็นภาษาไทย";

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/insights").RequireAuthorization();
        g.MapGet("/", GetLatest);
        g.MapPost("/", Generate);
    }

    private static bool IsValidPeriod(string? period) => period is "week" or "month";

    private static string NormalizePeriod(string? period) => period == "month" ? "month" : "week";

    private static bool KeyConfigured(IConfiguration cfg) =>
        !string.IsNullOrWhiteSpace(cfg["Anthropic:ApiKey"]);

    // GET /api/insights?period=&accountIds= — return the cached latest insight for
    // (user, period). enabled reflects whether the Anthropic API key is configured.
    private static async Task<IResult> GetLatest(
        ClaimsPrincipal user, AppDbContext db, IConfiguration cfg, string? period)
    {
        var p = NormalizePeriod(period);
        var enabled = KeyConfigured(cfg);
        var row = await db.Insights
            .FirstOrDefaultAsync(i => i.UserId == user.UserId() && i.Period == p);
        return Results.Ok(new
        {
            enabled,
            text = row?.Content,
            generatedAtUtc = row?.GeneratedAtUtc,
            period = p,
        });
    }

    // POST /api/insights?period=&accountIds= — compute stats, call Claude, upsert cache.
    private static async Task<IResult> Generate(
        ClaimsPrincipal user, AppDbContext db, IConfiguration cfg, IHttpClientFactory http,
        string? period, string? accountIds)
    {
        if (!IsValidPeriod(period))
            return Results.BadRequest(new { error = "period must be week or month" });
        if (!Reports.TryParseAccountIds(accountIds, out var ids))
            return Results.BadRequest(new { error = "invalid accountIds" });

        var apiKey = cfg["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Results.Json(new { error = "AI not configured" }, statusCode: 503);

        var tz = Reports.Tz(user);
        var stats = await BuildStats(db, user, ids, period!, tz);

        string text;
        try
        {
            text = await CallClaude(http, apiKey, stats, period!);
        }
        catch (Exception e)
        {
            return Results.Json(new { error = "AI request failed: " + e.Message }, statusCode: 502);
        }

        var now = DateTime.UtcNow;
        var row = await db.Insights
            .FirstOrDefaultAsync(i => i.UserId == user.UserId() && i.Period == period);
        if (row is null)
        {
            row = new Insight { UserId = user.UserId(), Period = period!, Content = text, GeneratedAtUtc = now };
            db.Insights.Add(row);
        }
        else
        {
            row.Content = text;
            row.GeneratedAtUtc = now;
        }
        await db.SaveChangesAsync();

        return Results.Ok(new { text, generatedAtUtc = now, period });
    }

    // ── Stats ────────────────────────────────────────────────────────────────

    // Current and previous period boundaries (local dates in the user's tz):
    //   week  = ISO Mon–Sun this week vs last week
    //   month = this calendar month vs last
    private static (DateOnly curStart, DateOnly curEnd, DateOnly prevStart, DateOnly prevEnd)
        Bounds(string period, TimeZoneInfo tz)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz));
        if (period == "month")
        {
            var curStart = new DateOnly(today.Year, today.Month, 1);
            var curEnd = curStart.AddMonths(1);          // exclusive
            var prevStart = curStart.AddMonths(-1);
            return (curStart, curEnd, prevStart, curStart);
        }
        // week: Monday start
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var nextMonday = monday.AddDays(7);              // exclusive
        var lastMonday = monday.AddDays(-7);
        return (monday, nextMonday, lastMonday, monday);
    }

    private static async Task<object> BuildStats(
        AppDbContext db, ClaimsPrincipal user, Guid[] ids, string period, TimeZoneInfo tz)
    {
        var (curStart, curEnd, prevStart, prevEnd) = Bounds(period, tz);

        // Pull all trades across both periods in one query, then bucket in memory.
        // Reuse Reports.Filtered for ownership + account scoping; dates are bare local
        // dates (Kind.Unspecified) so NormalizeRange interprets them in the user's tz.
        var earliest = prevStart < curStart ? prevStart : curStart;  // earliest start
        var latest = curEnd > prevEnd ? curEnd : prevEnd;            // latest exclusive end

        var trades = await Reports
            .Filtered(db, user, ids,
                DateTime.SpecifyKind(earliest.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
                DateTime.SpecifyKind(latest.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
                null, tz)
            .Select(t => new { t.CloseTimeUtc, t.NetProfit, t.AccountId, t.MagicNumber })
            .ToListAsync();

        var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
            .ToDictionaryAsync(a => a.Id, a => new { a.Name, a.AccountNumber });
        var eaNames = await db.EaNames.Where(e => e.UserId == user.UserId())
            .ToDictionaryAsync(e => e.MagicNumber, e => e.Name);

        // Local date of each trade in the user's tz.
        var withDate = trades.Select(t => new TradeRow(
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz)),
            t.NetProfit, t.AccountId, t.MagicNumber)).ToList();

        var cur = withDate.Where(t => t.Date >= curStart && t.Date < curEnd).ToList();
        var prev = withDate.Where(t => t.Date >= prevStart && t.Date < prevEnd).ToList();

        static object PeriodStats(List<TradeRow> list)
        {
            var count = list.Count;
            var net = list.Sum(r => r.NetProfit);
            var wins = list.Count(r => r.NetProfit > 0);
            var byDay = list.GroupBy(r => r.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.NetProfit));
            object? best = null, worst = null;
            if (byDay.Count > 0)
            {
                var b = byDay.OrderByDescending(kv => kv.Value).First();
                var w = byDay.OrderBy(kv => kv.Value).First();
                best = new { date = b.Key.ToString("yyyy-MM-dd"), net = Math.Round(b.Value, 2) };
                worst = new { date = w.Key.ToString("yyyy-MM-dd"), net = Math.Round(w.Value, 2) };
            }
            return new
            {
                netProfit = Math.Round(net, 2),
                tradeCount = count,
                winRate = count > 0 ? Math.Round(100m * wins / count, 1) : 0m,
                bestDay = best,
                worstDay = worst,
            };
        }

        string AccountName(Guid id)
        {
            var a = accounts.GetValueOrDefault(id);
            return a is not null && !string.IsNullOrWhiteSpace(a.Name) ? a.Name
                : a?.AccountNumber.ToString() ?? id.ToString();
        }

        var curNetByAcc = cur.GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.NetProfit));
        var curCountByAcc = cur.GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Count());
        var prevNetByAcc = prev.GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.NetProfit));

        var perAccount = curNetByAcc.Keys.Union(prevNetByAcc.Keys)
            .Select(id => new
            {
                name = AccountName(id),
                currentNet = Math.Round(curNetByAcc.GetValueOrDefault(id, 0m), 2),
                previousNet = Math.Round(prevNetByAcc.GetValueOrDefault(id, 0m), 2),
                currentTrades = curCountByAcc.GetValueOrDefault(id, 0),
            })
            .OrderByDescending(r => r.currentNet)
            .ToList();

        // EA naming: EaName if set, else owning account name (or "Multiple"), else magic.
        var perEa = cur.GroupBy(t => t.MagicNumber)
            .Select(grp =>
            {
                var rows = grp.ToList();
                string name;
                if (eaNames.TryGetValue(grp.Key, out var named) && !string.IsNullOrWhiteSpace(named))
                    name = named;
                else
                {
                    var distinctAcc = rows.Select(r => r.AccountId).Distinct().ToList();
                    name = distinctAcc.Count > 1 ? "Multiple" : AccountName(distinctAcc[0]);
                    if (string.IsNullOrWhiteSpace(name)) name = grp.Key.ToString();
                }
                return new
                {
                    name,
                    currentNet = Math.Round(rows.Sum(r => r.NetProfit), 2),
                    currentTrades = rows.Count,
                };
            })
            .OrderByDescending(r => r.currentNet)
            .ToList();

        return new
        {
            current = PeriodStats(cur),
            previous = PeriodStats(prev),
            perAccount,
            perEa,
        };
    }

    private readonly record struct TradeRow(DateOnly Date, decimal NetProfit, Guid AccountId, long MagicNumber);

    // ── Claude REST call (raw HTTP) ────────────────────────────────────────────

    private static async Task<string> CallClaude(
        IHttpClientFactory http, string apiKey, object stats, string period)
    {
        var label = period == "month" ? "เดือนนี้เทียบกับเดือนที่แล้ว" : "สัปดาห์นี้เทียบกับสัปดาห์ที่แล้ว";
        var statsJson = JsonSerializer.Serialize(stats);
        var userContent =
            $"ช่วงเวลา: {label}. กรุณาวิเคราะห์ผลปัจจุบันเทียบกับช่วงก่อนหน้าจากข้อมูลสรุปต่อไปนี้:\n{statsJson}";

        var body = new
        {
            model = Model,
            max_tokens = 1500,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = userContent } },
        };

        var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var req = new HttpRequestMessage(HttpMethod.Post, ClaudeUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await client.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API returned {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.TryGetProperty("stop_reason", out var sr)
            && sr.ValueKind == JsonValueKind.String
            && sr.GetString() == "refusal")
            return "ไม่สามารถสร้างบทวิเคราะห์ได้ในขณะนี้ กรุณาลองใหม่อีกครั้ง";

        var sb = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var ty)
                    && ty.GetString() == "text"
                    && block.TryGetProperty("text", out var txt))
                    sb.Append(txt.GetString());
            }
        }

        var text = sb.ToString().Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("Claude API returned no text content");
        return text;
    }
}
