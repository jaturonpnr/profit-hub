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
