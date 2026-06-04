// Courtly Worker host. Feature 2: loads .env, binds typed options, opens a RabbitMQ connection.
// The full consumer (queues, DLX, email/notification handlers) arrives in feature 17.
using Courtly.Infrastructure.Configuration;
using Courtly.Worker;

EnvironmentLoader.Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);
builder.Services.AddHostedService<RabbitMqConnectionService>();

var host = builder.Build();

host.Run();
