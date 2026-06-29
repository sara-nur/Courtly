using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Notifications;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Application.Notifications;

/// <summary>
/// The user-facing notification inbox (feature 18). The owner is always the caller from <see cref="ICurrentUser"/>
/// (never the route/body), reads are <c>AsNoTracking</c> + projected to DTOs (never entities), every timestamp comes
/// from <see cref="IClock"/> (UTC), and the list is paged with the shared <see cref="QueryableExtensions"/> helper.
/// Scoped (it injects the scoped <see cref="CourtlyDbContext"/>).
/// </summary>
/// <remarks>
/// <para><b>Owner-scoped.</b> Every query is filtered to the caller's id, so a client can never read or mutate another
/// user's notifications (rubric §5: ownership from the token).</para>
/// <para><b>Single SaveChanges.</b> <see cref="MarkAllAsReadAsync"/> loads the caller's tracked unread rows and flips
/// them in one <c>SaveChangesAsync</c> — it deliberately avoids <c>ExecuteUpdateAsync</c> so the read path stays
/// provider-agnostic (Npgsql in prod, EF InMemory in tests).</para>
/// </remarks>
public sealed class NotificationService : INotificationService
{
    private readonly CourtlyDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public NotificationService(CourtlyDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PagedResult<NotificationDto>> ListMineAsync(
        PaginationQuery pagination, CancellationToken ct = default)
    {
        var me = CurrentUserId();

        // Newest-first (rubric §6: latest record on top); the projection preserves the order.
        return await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == me)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Select(n => new NotificationDto(
                n.Id,
                n.Type,
                n.Type.ToString(),
                n.Title,
                n.Text,
                n.IsRead,
                n.CreatedAtUtc,
                n.ReadAtUtc))
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<UnreadCountDto> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var me = CurrentUserId();
        var count = await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == me && !n.IsRead, ct);

        return new UnreadCountDto(count);
    }

    public async Task<NotificationDto> MarkAsReadAsync(long id, CancellationToken ct = default)
    {
        var me = CurrentUserId();

        // Tracked load scoped to the owner: a missing row or one belonging to someone else is an indistinguishable 404
        // (we never reveal that another user's notification exists).
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == me, ct)
            ?? throw new NotFoundException($"Notification {id} was not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ToDto(notification);
    }

    public async Task MarkAllAsReadAsync(CancellationToken ct = default)
    {
        var me = CurrentUserId();
        var now = _clock.UtcNow;

        var unread = await _db.Notifications
            .Where(n => n.UserId == me && !n.IsRead)
            .ToListAsync(ct);

        if (unread.Count == 0)
        {
            return;
        }

        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private Guid CurrentUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("You must be signed in to view notifications.");

    private static NotificationDto ToDto(Domain.Entities.Notification n) =>
        new(
            n.Id,
            n.Type,
            n.Type.ToString(),
            n.Title,
            n.Text,
            n.IsRead,
            n.CreatedAtUtc,
            n.ReadAtUtc);
}
