using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProfitHub.Api.Domain;
using ProfitHub.Api.Infrastructure;

// QuestPDF Community license — must be set before the first PDF render or it throws.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
    o.SerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
});

var jwtKeyConfig = builder.Configuration["Jwt:Key"];
if (builder.Environment.IsProduction() && jwtKeyConfig is null)
    throw new InvalidOperationException("Jwt:Key must be configured");
var jwtKey = jwtKeyConfig ?? "dev-only-key-change-me-0123456789abcdef";
builder.Services.AddSingleton(new JwtSettings(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false, ValidateAudience = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? ["http://localhost:4200"])
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
if (!app.Environment.IsEnvironment("Testing"))
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await PromoteAdminAsync(db, app.Configuration["Admin:Email"]);
    }

// Idempotent admin bootstrap: if config Admin:Email (env Admin__Email) names an
// existing user, ensure IsAdmin=true. Safe to run every boot; no-op when unset
// or when the user does not (yet) exist.
static async Task PromoteAdminAsync(AppDbContext db, string? adminEmail)
{
    if (string.IsNullOrWhiteSpace(adminEmail)) return;
    var email = adminEmail.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user is null || user.IsAdmin) return;
    user.IsAdmin = true;
    await db.SaveChangesAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");
ProfitHub.Api.Features.Endpoints.MapAll(app);

app.Run();

public record JwtSettings(string Key);
public partial class Program { }
