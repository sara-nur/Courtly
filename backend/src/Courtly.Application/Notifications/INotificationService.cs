using Courtly.Contracts.Common;
using Courtly.Contracts.Notifications;

namespace Courtly.Application.Notifications;

/// <summary>
/// The caller's in-app notification inbox (feature 18): list their own notifications (newest first, paged), read the
/// unread badge count, and transition read-state. Ownership always comes from the JWT via <c>ICurrentUser</c> — a
/// caller can only ever see or mutate their own notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>The caller's notifications, newest first, as one page (rubric §8.2: always paged).</summary>
    Task<PagedResult<NotificationDto>> ListMineAsync(PaginationQuery pagination, CancellationToken ct = default);

    /// <summary>The caller's number of unread notifications (for a badge count).</summary>
    Task<UnreadCountDto> GetUnreadCountAsync(CancellationToken ct = default);

    /// <summary>Marks the caller's notification <paramref name="id"/> read (idempotent — an already-read notification
    /// is returned unchanged). 404 if it does not exist or belongs to someone else.</summary>
    Task<NotificationDto> MarkAsReadAsync(long id, CancellationToken ct = default);

    /// <summary>Marks every one of the caller's unread notifications read in a single save.</summary>
    Task MarkAllAsReadAsync(CancellationToken ct = default);
}
