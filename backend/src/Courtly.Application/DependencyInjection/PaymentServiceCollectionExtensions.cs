using Courtly.Application.Payments;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the payments engine (feature 16). <see cref="IPaymentService"/> is <c>Scoped</c> (it injects the
/// scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>). The Stripe gateway is a stateless
/// singleton (one <c>StripeClient</c>, options read once); the payment event publisher is the feature-16 logging stub,
/// also a singleton — feature 17 replaces only that line with the real RabbitMQ publisher. Validators need no
/// per-validator registration — the <c>Program.cs</c> assembly scan picks them up. Called once from <c>Program.cs</c>
/// right after <c>AddCourtlyUsers()</c>.
/// </summary>
public static class PaymentServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyPayments(this IServiceCollection services)
    {
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddSingleton<IStripeGateway, StripeGateway>();
        services.AddSingleton<IPaymentEventPublisher, LoggingPaymentEventPublisher>();

        return services;
    }
}
