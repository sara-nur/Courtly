using Courtly.Domain.Enums;

namespace Courtly.Contracts.Notifications;

/// <summary>
/// In-app notification contracts (feature 18). The Worker (feature 17) persists a notification per consumed
/// reservation/payment event and pushes it over SignalR; the API exposes the user's notification list, unread count,
/// and read-state transitions. DTOs only on the wire — the <c>Notification</c> entity never leaves the service.
/// </summary>

/// <summary>One persisted notification as seen by its owner. <see cref="TypeName"/> is <see cref="Type"/>'s enum name
/// (so the client can switch on a stable string), and <see cref="ReadAtUtc"/> is null until the user marks it read.</summary>
public sealed record NotificationDto(
    long Id,
    NotificationType Type,
    string TypeName,
    string Title,
    string Text,
    bool IsRead,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

/// <summary>The Worker -> API internal push payload: a just-persisted <see cref="Notification"/> and the
/// <see cref="UserId"/> whose SignalR group it must be delivered to. Sent to <c>POST /api/internal/push</c>, guarded by
/// the shared internal key — never reachable by clients.</summary>
public sealed record InternalPushRequest(Guid UserId, NotificationDto Notification);

/// <summary>The caller's number of unread notifications, for a badge count.</summary>
public sealed record UnreadCountDto(int Count);
