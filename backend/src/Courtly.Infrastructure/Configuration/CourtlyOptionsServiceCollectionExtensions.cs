using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// Binds the six Courtly option groups from flat <c>.env</c> keys to strongly-typed options.
/// Db and Rabbit are required by feature 2, so they are validated on startup; Jwt/Stripe/Smtp/Api
/// are bound now but validated by their own features (5/16/17/18) once those subsystems come online.
/// </summary>
public static class CourtlyOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<DbOptions>()
            .Configure(o => o.ConnectionString = config["DB_CONNECTION_STRING"] ?? string.Empty)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "DB_CONNECTION_STRING is required.")
            .ValidateOnStart();

        services.AddOptions<RabbitOptions>()
            .Configure(o =>
            {
                o.Host = config["RABBITMQ_HOST"] ?? string.Empty;
                o.Port = ParseInt(config["RABBITMQ_PORT"], 5672);
                o.User = config["RABBITMQ_USER"] ?? string.Empty;
                o.Password = config["RABBITMQ_PASSWORD"] ?? string.Empty;
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "RABBITMQ_HOST is required.")
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Configure(o =>
            {
                o.Key = config["JWT_KEY"] ?? string.Empty;
                o.Issuer = config["JWT_ISSUER"] ?? string.Empty;
                o.Audience = config["JWT_AUDIENCE"] ?? string.Empty;
                o.AccessMinutes = ParseInt(config["JWT_ACCESS_MINUTES"], 15);
                o.RefreshDays = ParseInt(config["JWT_REFRESH_DAYS"], 7);
                o.ResetTokenMinutes = ParseInt(config["JWT_RESET_TOKEN_MINUTES"], 60);
            })
            // Feature 5 brings auth online, so the signing key is now required (HMAC-SHA256 needs ≥256 bits).
            .Validate(o => o.Key.Length >= 32, "JWT_KEY must be set and at least 32 characters.")
            .ValidateOnStart();

        services.AddOptions<StripeOptions>()
            .Configure(o =>
            {
                o.SecretKey = config["STRIPE_SECRET_KEY"] ?? string.Empty;
                o.PublishableKey = config["STRIPE_PUBLISHABLE_KEY"] ?? string.Empty;
                o.WebhookSecret = config["STRIPE_WEBHOOK_SECRET"] ?? string.Empty;
            });

        services.AddOptions<SmtpOptions>()
            .Configure(o =>
            {
                o.Host = config["SMTP_HOST"] ?? string.Empty;
                o.Port = ParseInt(config["SMTP_PORT"], 2525);
                o.User = config["SMTP_USER"] ?? string.Empty;
                o.Password = config["SMTP_PASSWORD"] ?? string.Empty;
                o.From = config["SMTP_FROM"] ?? string.Empty;
            });

        services.AddOptions<ApiOptions>()
            .Configure(o =>
            {
                o.BaseUrl = config["API_BASE_URL"] ?? string.Empty;
                o.InternalPushKey = config["INTERNAL_PUSH_KEY"] ?? string.Empty;
                o.AllowedCorsOrigins = ParseCsv(config["CORS_ALLOWED_ORIGINS"]);
            });

        return services;
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string[] ParseCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
