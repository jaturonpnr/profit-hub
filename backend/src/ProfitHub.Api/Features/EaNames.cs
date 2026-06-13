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
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Concurrent insert of the same (user, magic) pair: retry as an update.
                db.ChangeTracker.Clear();
                var existing = await db.EaNames.FirstOrDefaultAsync(e => e.UserId == user.UserId() && e.MagicNumber == magic);
                if (existing is null) return Results.Conflict();
                existing.Name = req.Name;
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });
    }
}
