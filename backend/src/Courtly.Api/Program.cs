// Courtly API host. Feature 2: loads .env, binds typed options, exposes a /health endpoint.
// Controllers, middleware, SignalR hub, JWT, and migrate+seed-on-startup arrive in features 3+.
using Courtly.Infrastructure.Configuration;

EnvironmentLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => "Courtly API");
app.MapHealthChecks("/health");

app.Run();
