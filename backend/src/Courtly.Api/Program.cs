// Courtly API host. Scaffold stub — health endpoint, typed options, DI and migrations arrive in feature 2+.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Courtly API");

app.Run();
