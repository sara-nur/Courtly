// Courtly Worker host: loads .env, binds typed options, opens the shared RabbitMQ connection, and runs the
// background consumers — the email consumer + hold-expiry scan (feature 17) and the notification consumer that
// persists a notification per event then pushes it to the API's internal SignalR endpoint (feature 18).
using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
using Courtly.Application.DependencyInjection;
using Courtly.Application.Reservations;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.DependencyInjection;
using Courtly.Infrastructure.Persistence;
using Courtly.Worker;
using Courtly.Worker.Internal;

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

// Feature 18: persist a notification per consumed event, then push it to the API's internal SignalR endpoint.
builder.Services.AddCourtlyNotificationWriter();

// The typed push client targets the API's internal endpoint, authenticated by the shared internal key. BaseAddress +
// the X-Internal-Key header are read here from the same flat .env keys ApiOptions binds (config isn't resolvable as
// bound options at registration time, so read it directly — mirroring how this host reads DB_CONNECTION_STRING above).
var apiBaseUrl = builder.Configuration["API_BASE_URL"]
    ?? throw new InvalidOperationException("API_BASE_URL is required for the internal push client.");
var internalPushKey = builder.Configuration["INTERNAL_PUSH_KEY"]
    ?? throw new InvalidOperationException("INTERNAL_PUSH_KEY is required for the internal push client.");
builder.Services.AddHttpClient<IInternalPushClient, InternalPushClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.DefaultRequestHeaders.Add("X-Internal-Key", internalPushKey);
});

builder.Services.AddHostedService<NotificationConsumerBackgroundService>();

var host = builder.Build();

host.Run();
