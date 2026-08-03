using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DealDesk.Application.Abstractions;
using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.Services;
using DealDesk.Infrastructure.Auth;
using DealDesk.Infrastructure.Persistence;
using DealDesk.Infrastructure.Seeding;
using DealDesk.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // The whole API speaks snake_case, matching the assessment contract.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access_token returned by POST /api/auth/login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// JWT bearer authentication. The signing key in appsettings.json is a dev-only
// default; production overrides it via configuration (e.g. Jwt__SigningKey).
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (jwtOptions.SigningKey.Length < JwtOptions.MinimumSigningKeyLength)
    throw new InvalidOperationException(
        $"Jwt:SigningKey must be configured with at least {JwtOptions.MinimumSigningKeyLength} characters.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Token validation is configured against IClock (not the machine clock) so
// issuing and validating always agree on "now" — including under test, where
// the clock is pinned for deterministic date rules.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IClock>((options, clock) =>
    {
        // Keep the compact claim names ("role", "unique_name") instead of the
        // legacy mapped URIs, so RoleClaimType below matches what we issue.
        options.MapInboundClaims = false;

        var clockSkew = TimeSpan.FromSeconds(30);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
                (notBefore is null || notBefore <= clock.UtcNow.UtcDateTime + clockSkew) &&
                expires is not null && expires >= clock.UtcNow.UtcDateTime - clockSkew,
            RoleClaimType = "role",
            NameClaimType = "unique_name"
        };
    });
builder.Services.AddAuthorization();

// Composition root. The store is in-memory (allowed by the brief); swapping in a
// real database means replacing these registrations with EF-backed ones.
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IDealRepository, InMemoryDealRepository>();
builder.Services.AddSingleton<IReferenceGenerator, SequentialReferenceGenerator>();
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IDealService, DealService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

// Seed demo users from Auth:SeedUsers so the API is usable out of the box.
var seedUsers = app.Configuration.GetSection("Auth:SeedUsers").Get<List<RegisterUserRequest>>();
if (seedUsers is { Count: > 0 })
{
    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
    foreach (var seedUser in seedUsers)
    {
        var result = await authService.RegisterAsync(seedUser);
        if (result.IsFailure)
            app.Logger.LogWarning("Skipped seed user {Username}: {Message}",
                seedUser.Username, result.Error!.Message);
    }
}

// Seed the store from seed-deals.json (assessment sample data) when present.
// SeedDataPath can override the location; the default is the copy shipped next
// to the binaries, falling back to the content root.
var seedPath = app.Configuration["SeedDataPath"];
if (string.IsNullOrWhiteSpace(seedPath))
{
    seedPath = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "seed-deals.json"),
        Path.Combine(app.Environment.ContentRootPath, "seed-deals.json")
    }.FirstOrDefault(File.Exists);
}

if (!string.IsNullOrWhiteSpace(seedPath) && File.Exists(seedPath))
{
    await SeedDataLoader.LoadAsync(
        seedPath,
        app.Services.GetRequiredService<IDealRepository>(),
        app.Services.GetRequiredService<IReferenceGenerator>(),
        app.Services.GetRequiredService<IClock>(),
        app.Services.GetRequiredService<ILogger<Program>>());
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so integration tests can bootstrap the app via WebApplicationFactory.
public partial class Program
{
}
