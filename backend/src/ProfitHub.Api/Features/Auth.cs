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
            if (string.IsNullOrEmpty(c.Email) || string.IsNullOrEmpty(c.Password))
                return Results.BadRequest(new { error = "email and password required" });
            if (c.Password.Length < 8) return Results.BadRequest(new { error = "password too short" });
            var email = c.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email)) return Results.Conflict();
            db.Users.Add(new User { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(c.Password) });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict();
            }
            return Results.Ok();
        });

        app.MapPost("/api/auth/login", async (Creds c, AppDbContext db, JwtSettings jwt) =>
        {
            if (string.IsNullOrEmpty(c.Email) || string.IsNullOrEmpty(c.Password))
                return Results.BadRequest(new { error = "email and password required" });
            var email = c.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
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
