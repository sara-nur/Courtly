using Courtly.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Infrastructure.DependencyInjection;

/// <summary>
/// DI wiring for feature 17 RabbitMQ messaging. The connection and the real publisher are singletons (one shared
/// connection per process). The real SMTP email sender lives in the Application layer (beside <c>IEmailSender</c>)
/// and is wired via <c>AddCourtlySmtpEmail</c>.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>Registers the single shared RabbitMQ connection (lazy, with reconnect backoff).</summary>
    public static IServiceCollection AddRabbitMqConnection(this IServiceCollection services)
    {
        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();

        return services;
    }

    /// <summary>
    /// Registers the real RabbitMQ publisher and maps BOTH publisher interfaces to the same singleton instance
    /// (rubric §3.2: events are published to RabbitMQ rather than logged). Call this AFTER
    /// <c>AddCourtlyReservations()</c>/<c>AddCourtlyPayments()</c> — last registration wins, so this replaces the
    /// feature-14/16 logging stubs. Assumes <see cref="AddRabbitMqConnection"/> was also called.
    /// </summary>
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services)
    {
        services.AddSingleton<RabbitMqEventPublisher>();
        services.AddSingleton<IReservationEventPublisher>(sp => sp.GetRequiredService<RabbitMqEventPublisher>());
        services.AddSingleton<IPaymentEventPublisher>(sp => sp.GetRequiredService<RabbitMqEventPublisher>());

        return services;
    }
}
