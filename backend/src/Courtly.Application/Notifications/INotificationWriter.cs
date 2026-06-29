using Courtly.Contracts.Notifications;
using Courtly.Domain.Enums;

namespace Courtly.Application.Notifications;

/// <summary>
/// Persists an in-app notification for a user (feature 18). Used by the Worker when it consumes a reservation/payment
/// event (feature 17): it builds the title/text via <see cref="NotificationFactory"/>, writes the row here, then pushes
/// the returned DTO over SignalR. Has <b>no</b> <c>ICurrentUser</c> dependency — the recipient comes from the event, not
/// the JWT — so it is safe to use from the background worker where there is no request principal.
/// </summary>
public interface INotificationWriter
{
    /// <summary>Inserts an unread notification owned by <paramref name="userId"/> (stamped with the clock's UTC now)
    /// and returns its persisted shape (id + <c>ReadAtUtc = null</c>) for the SignalR push.</summary>
    Task<NotificationDto> CreateAsync(
        Guid userId, NotificationType type, string title, string text, CancellationToken ct = default);
}
