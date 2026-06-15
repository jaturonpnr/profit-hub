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
        app.MapPost("/api/auth/login", async (Creds c, AppDbContext db, JwtSettings jwt) =>
        {
            if (string.IsNullOrEmpty(c.Email) || string.IsNullOrEmpty(c.Password))
                return Results.BadRequest(new { error = "email and password required" });
            var email = c.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || !BCrypt.Net.BCrypt.Verify(c.Password, user.PasswordHash))
                return Results.Unauthorized();
            return Results.Ok(new { token = BuildToken(user, jwt) });
        });
    }

    /// <summary>
    /// Builds a signed JWT carrying sub/tz/email/isAdmin claims with a 30-day expiry.
    /// Shared by login and the timezone-update endpoint so the issued token always
    /// reflects the user's current TimeZone (read by Reports.Tz via the "tz" claim)
    /// and admin status (read by ClaimsPrincipal.IsAdmin via the "isAdmin" claim).
    /// </summary>
    public static string BuildToken(User user, JwtSettings jwt) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims:
            [
                new Claim("sub", user.Id.ToString()),
                new Claim("tz", user.TimeZone),
                new Claim("email", user.Email),
                new Claim("isAdmin", user.IsAdmin ? "true" : "false"),
            ],
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256)));

    public static Guid UserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue("sub") ?? p.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool IsAdmin(this ClaimsPrincipal p) =>
        string.Equals(p.FindFirstValue("isAdmin"), "true", StringComparison.OrdinalIgnoreCase);
}
