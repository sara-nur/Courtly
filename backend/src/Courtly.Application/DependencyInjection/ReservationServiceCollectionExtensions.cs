using Courtly.Application.Reservations;
using Courtly.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the reservation engine (feature 14). <see cref="IReservationService"/> is <c>Scoped</c> (it injects
/// the scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>). The event publisher is the
/// feature-14 logging stub, registered as a singleton (stateless); feature 17 replaces only this line with the real
/// RabbitMQ publisher. Validators need no per-validator registration — the <c>Program.cs</c> assembly scan picks them
/// up. Called once from <c>Program.cs</c> right after <c>AddCourtlyCourtCatalog()</c>.
/// </summary>
public static class ReservationServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyReservations(this IServiceCollection services)
    {
        services.AddScoped<IReservationService, ReservationService>();
        services.AddSingleton<IReservationEventPublisher, LoggingReservationEventPublisher>();

        return services;
    }
}
