// Courtly Worker host. Feature 2: loads .env, binds typed options, opens a RabbitMQ connection.
// The full consumer (queues, DLX, email/notification handlers) arrives in feature 17.
using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
using Courtly.Application.DependencyInjection;
using Courtly.Application.Reservations;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.DependencyInjection;
using Courtly.Infrastructure.Persistence;
using Courtly.Worker;

EnvironmentLoader.Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);
builder.Services.AddHostedService<RabbitMqConnectionService>();

var db = builder.Configuration["DB_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING is required.");
builder.Services.AddCourtlyPersistence(db);
builder.Services.AddSingleton<IClock, SystemClock>();   // Worker needs the clock without pulling in all of auth
builder.Services.AddRabbitMqConnection();
builder.Services.AddRabbitMqMessaging();                // real publisher (hold-expiry publishes reservation.cancelled)
builder.Services.AddCourtlySmtpEmail();                 // IEmailSender -> SmtpEmailSender
builder.Services.AddScoped<ReservationHoldExpiryService>();
builder.Services.AddHostedService<EmailConsumerBackgroundService>();
builder.Services.AddHostedService<HoldExpiryBackgroundService>();

var host = builder.Build();

host.Run();
