using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;

namespace ProfitHub.Api.Features;

public static class Me
{
    public record TimeZoneUpdate(string TimeZone);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/me").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId());
            if (user is null) return Results.Unauthorized();
            return Results.Ok(new { email = user.Email, timeZone = user.TimeZone });
        });

        g.MapPut("/timezone", async (TimeZoneUpdate body, ClaimsPrincipal principal, AppDbContext db, JwtSettings jwt) =>
        {
            if (string.IsNullOrWhiteSpace(body.TimeZone))
                return Results.BadRequest(new { error = "timeZone required" });
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(body.TimeZone);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return Results.BadRequest(new { error = $"unknown timezone: {body.TimeZone}" });
            }
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId());
            if (user is null) return Results.Unauthorized();
            user.TimeZone = body.TimeZone;
            await db.SaveChangesAsync();
            // Issue a fresh token carrying the updated tz claim so subsequent report
            // calls bucket day/week/month in the newly chosen timezone.
            return Results.Ok(new { token = Auth.BuildToken(user, jwt) });
        });
    }
}
