using Courtly.Application.Abstractions;
using Courtly.Contracts.Notifications;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;

namespace Courtly.Application.Notifications;

/// <summary>
/// Default <see cref="INotificationWriter"/>: inserts a <see cref="Notification"/> in one <c>SaveChangesAsync</c> and
/// returns the persisted <see cref="NotificationDto"/> (feature 18). Scoped (it injects the scoped
/// <see cref="CourtlyDbContext"/>); the timestamp comes from <see cref="IClock"/> (UTC) so it is deterministic in
/// tests. No <c>ICurrentUser</c> — the owner is the event's recipient, supplied by the caller (the Worker).
/// </summary>
public sealed class NotificationWriter : INotificationWriter
{
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;

    public NotificationWriter(CourtlyDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<NotificationDto> CreateAsync(
        Guid userId, NotificationType type, string title, string text, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Text = text,
            IsRead = false,
            CreatedAtUtc = now,
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        return new NotificationDto(
            notification.Id,
            notification.Type,
            notification.Type.ToString(),
            notification.Title,
            notification.Text,
            notification.IsRead,
            notification.CreatedAtUtc,
            notification.ReadAtUtc);
    }
}
