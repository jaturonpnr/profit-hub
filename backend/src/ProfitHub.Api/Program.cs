using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProfitHub.Api.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-key-change-me-0123456789abcdef";
builder.Services.AddSingleton(new JwtSettings(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, ValidateAudience = false,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:4200"])
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
