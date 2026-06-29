using Courtly.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for in-app notifications (feature 18). Split into two registrations because the API and the Worker need
/// different halves: the API wires <see cref="AddCourtlyNotificationService"/> (the user-facing inbox), while the
/// Worker wires <see cref="AddCourtlyNotificationWriter"/> (the event-driven persister). Both implementations inject
/// the scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>, so both are <c>Scoped</c>.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    /// <summary>Registers the user-facing <see cref="INotificationService"/> (list/unread-count/read-state) for the
    /// API. Called once from the API's <c>Program.cs</c>.</summary>
    public static IServiceCollection AddCourtlyNotificationService(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    /// <summary>Registers the event-driven <see cref="INotificationWriter"/> (persists a notification per consumed
    /// event) for the Worker. Called once from the Worker's <c>Program.cs</c>.</summary>
    public static IServiceCollection AddCourtlyNotificationWriter(this IServiceCollection services)
    {
        services.AddScoped<INotificationWriter, NotificationWriter>();

        return services;
    }
}
