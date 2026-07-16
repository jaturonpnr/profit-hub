using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// Input Labels (CONTEXT.md): user-defined readable names + per-value texts for raw
/// EA input keys, global per user. PUT with an empty label AND empty valueMap deletes.
public static class InputLabels
{
    public record PutReq(string? Label, Dictionary<string, string>? ValueMap);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/input-labels").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            (await db.InputLabels.Where(l => l.UserId == user.UserId()).ToListAsync())
            .Select(l => new
            {
                key = l.Key,
                label = l.Label,
                valueMap = SafeMap(l.ValueMapJson),
            }));

        g.MapPut("/{key}", async (string key, PutReq req, ClaimsPrincipal user, AppDbContext db) =>
        {
            key = key.Trim();
            if (key.Length == 0) return Results.BadRequest(new { error = "key required" });
            var label = req.Label?.Trim() ?? "";
            var map = (req.ValueMap ?? [])
                .Where(kv => kv.Key.Trim().Length > 0 && kv.Value.Trim().Length > 0)
                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());

            var row = await db.InputLabels.FirstOrDefaultAsync(l => l.UserId == user.UserId() && l.Key == key);
            if (label.Length == 0 && map.Count == 0)
            {
                if (row is not null) { db.InputLabels.Remove(row); await db.SaveChangesAsync(); }
                return Results.NoContent();                      // empty payload = delete
            }
            if (row is null)
            {
                row = new InputLabel { UserId = user.UserId(), Key = key };
                db.InputLabels.Add(row);
            }
            row.Label = label;
            row.ValueMapJson = JsonSerializer.Serialize(map);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Concurrent insert of the same (user, key): retry as an update.
                db.ChangeTracker.Clear();
                var existing = await db.InputLabels.FirstOrDefaultAsync(l => l.UserId == user.UserId() && l.Key == key);
                if (existing is null) return Results.Conflict();
                existing.Label = label;
                existing.ValueMapJson = JsonSerializer.Serialize(map);
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });
    }

    private static Dictionary<string, string> SafeMap(string? json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(string.IsNullOrEmpty(json) ? "{}" : json) ?? []; }
        catch (JsonException) { return []; }
    }
}
