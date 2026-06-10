// Courtly API host. Loads .env, binds typed options, registers the EF context, applies migrations on startup,
// and exposes a /health endpoint. Controllers, middleware, SignalR hub, JWT, and seeding arrive in features 4+.
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

EnvironmentLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);

var dbConnectionString = builder.Configuration["DB_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING is required.");
builder.Services.AddCourtlyPersistence(dbConnectionString);

builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply EF migrations on startup so `docker compose up` brings the 200067 schema up to date (seeding: feature 4).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CourtlyDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => "Courtly API");
app.MapHealthChecks("/health");

app.Run();
