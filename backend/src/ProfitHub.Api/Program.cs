using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProfitHub.Api.Domain;
using ProfitHub.Api.Infrastructure;

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
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");
ProfitHub.Api.Features.Endpoints.MapAll(app);

app.Run();

public record JwtSettings(string Key);
public partial class Program { }
