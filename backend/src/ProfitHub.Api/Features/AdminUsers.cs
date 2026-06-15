using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

/// <summary>
/// Admin-only user management under /api/admin/users. RequireAuthorization handles 401;
/// every handler additionally enforces ClaimsPrincipal.IsAdmin() (403 via Results.Forbid).
/// Deleting a user cascades to all their accounts/trades/balance-ops/ea-names (see AppDbContext).
/// </summary>
public static class AdminUsers
{
    public record CreateReq(string Email, string Password, bool IsAdmin);
    public record PasswordReq(string Password);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/admin/users").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal me, AppDbContext db) =>
        {
            if (!me.IsAdmin()) return Results.Forbid();
            var rows = await db.Users
                .OrderBy(u => u.Email)
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    isAdmin = u.IsAdmin,
                    createdAtUtc = u.CreatedAtUtc,
                    accountCount = u.Accounts.Count,
                })
                .ToListAsync();
            return Results.Ok(rows);
        });

        g.MapPost("", async (CreateReq req, ClaimsPrincipal me, AppDbContext db) =>
        {
            if (!me.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new { error = "email and password required" });
            if (req.Password.Length < 8) return Results.BadRequest(new { error = "password too short" });
            var email = req.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email)) return Results.Conflict();
            var user = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                IsAdmin = req.IsAdmin,
            };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict();
            }
            return Results.Ok(new
            {
                id = user.Id,
                email = user.Email,
                isAdmin = user.IsAdmin,
                createdAtUtc = user.CreatedAtUtc,
                accountCount = 0,
            });
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal me, AppDbContext db) =>
        {
            if (!me.IsAdmin()) return Results.Forbid();
            if (id == me.UserId()) return Results.BadRequest(new { error = "cannot delete yourself" });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            if (user.IsAdmin && await db.Users.CountAsync(u => u.IsAdmin) <= 1)
                return Results.BadRequest(new { error = "cannot delete the last admin" });
            db.Users.Remove(user); // cascades to accounts → trades/balance-ops, and ea-names
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPut("/{id:guid}/password", async (Guid id, PasswordReq req, ClaimsPrincipal me, AppDbContext db) =>
        {
            if (!me.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8)
                return Results.BadRequest(new { error = "password too short" });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
