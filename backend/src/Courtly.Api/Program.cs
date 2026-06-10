// Courtly API host. Loads .env, binds typed options, registers the EF context, applies migrations + seeds data
// on startup, and exposes a /health endpoint. Controllers, middleware, SignalR hub, and JWT arrive in features 5+.
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Persistence;
using Courtly.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

EnvironmentLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);

var dbConnectionString = builder.Configuration["DB_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING is required.");
builder.Services.AddCourtlyPersistence(dbConnectionString);
builder.Services.AddCourtlySeeding();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply EF migrations (which insert the HasData reference rows) then run the idempotent runtime seeder, so
// `docker compose up` brings the 200067 schema up to date and populates demo data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CourtlyDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<ICourtlyDataSeeder>();
    await seeder.SeedAsync();
}

app.MapGet("/", () => "Courtly API");
app.MapHealthChecks("/health");

app.Run();
