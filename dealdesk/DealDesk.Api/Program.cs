using System.Text.Json;
using System.Text.Json.Serialization;
using DealDesk.Application.Abstractions;
using DealDesk.Application.Services;
using DealDesk.Infrastructure.Persistence;
using DealDesk.Infrastructure.Seeding;
using DealDesk.Infrastructure.Time;

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
builder.Services.AddSwaggerGen();

// Composition root. The store is in-memory (allowed by the brief); swapping in a
// real database means replacing these registrations with EF-backed ones.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IDealRepository, InMemoryDealRepository>();
builder.Services.AddSingleton<IReferenceGenerator, SequentialReferenceGenerator>();
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddScoped<IDealService, DealService>();

var app = builder.Build();

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

app.MapControllers();

app.Run();

// Exposed so integration tests can bootstrap the app via WebApplicationFactory.
public partial class Program
{
}
