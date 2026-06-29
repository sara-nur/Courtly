using Courtly.Application.Common.Exceptions;
using Courtly.Application.Notifications;
using Courtly.Contracts.Common;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Notifications;

/// <summary>
/// Feature 18 (auto): the notification inbox is strictly owner-scoped from <see cref="ICurrentUser"/> (never the
/// route/body). <see cref="NotificationService.ListMineAsync"/> returns only the caller's rows newest-first and respects
/// the paginated request (size clamp + paging); <see cref="NotificationService.GetUnreadCountAsync"/> counts only the
/// caller's unread; <see cref="NotificationService.MarkAsReadAsync"/> 404s on another user's row and stamps the read
/// time on the caller's own; <see cref="NotificationService.MarkAllAsReadAsync"/> flips only the caller's unread rows.
/// EF in-memory, fixed clock.
/// </summary>
public class NotificationServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static NotificationService NewService(CourtlyDbContext db, Guid? userId) =>
        new(db, new TestCurrentUser { UserId = userId }, new TestClock { UtcNow = Now });

    /// <summary>Inserts a notification and returns its generated id (created relative to <see cref="Now"/>).</summary>
    private static async Task<long> SeedAsync(
        CourtlyDbContext db, Guid userId, int minutesAgo, bool isRead = false)
    {
        var n = new Notification
        {
            UserId = userId,
            Type = NotificationType.General,
            Title = $"n-{minutesAgo}",
            Text = "body",
            IsRead = isRead,
            CreatedAtUtc = Now.AddMinutes(-minutesAgo),
            ReadAtUtc = isRead ? Now.AddMinutes(-minutesAgo) : null,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    [Fact]
    public async Task ListMineAsync_ReturnsOnlyCallersRows_NewestFirst()
    {
        await using var db = NotificationTestDb.New();
        // Mine: created 10 min ago (older) and 1 min ago (newer). Other user's row must not appear.
        await SeedAsync(db, Me, minutesAgo: 10);
        await SeedAsync(db, Me, minutesAgo: 1);
        await SeedAsync(db, Other, minutesAgo: 5);

        var svc = NewService(db, Me);
        var page = await svc.ListMineAsync(new PaginationQuery());

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        // Newest CreatedAtUtc first.
        Assert.True(page.Items[0].CreatedAtUtc > page.Items[1].CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(-1), page.Items[0].CreatedAtUtc);
        Assert.Equal("General", page.Items[0].TypeName);
    }

    [Fact]
    public async Task ListMineAsync_RespectsPagingAndClampsPageSizeToMaximum()
    {
        await using var db = NotificationTestDb.New();
        // 5 of my rows, created 1..5 minutes ago (so minute-1 is newest).
        for (var i = 1; i <= 5; i++)
        {
            await SeedAsync(db, Me, minutesAgo: i);
        }

        var svc = NewService(db, Me);

        // Page 1, size 2 → the two newest (1 and 2 minutes ago).
        var p1 = await svc.ListMineAsync(new PaginationQuery { Page = 1, PageSize = 2 });
        Assert.Equal(5, p1.TotalCount);
        Assert.Equal(2, p1.Items.Count);
        Assert.Equal(Now.AddMinutes(-1), p1.Items[0].CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(-2), p1.Items[1].CreatedAtUtc);

        // Page 2, size 2 → the next two (3 and 4 minutes ago).
        var p2 = await svc.ListMineAsync(new PaginationQuery { Page = 2, PageSize = 2 });
        Assert.Equal(2, p2.Items.Count);
        Assert.Equal(Now.AddMinutes(-3), p2.Items[0].CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(-4), p2.Items[1].CreatedAtUtc);

        // Oversized PageSize is clamped to the server maximum (rubric §8.2: hard page-size cap).
        var clamped = await svc.ListMineAsync(new PaginationQuery { Page = 1, PageSize = 10_000 });
        Assert.Equal(PaginationQuery.MaxPageSize, clamped.PageSize);
        Assert.Equal(5, clamped.Items.Count); // all five fit under the cap
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyCallersUnread()
    {
        await using var db = NotificationTestDb.New();
        await SeedAsync(db, Me, minutesAgo: 1, isRead: false);
        await SeedAsync(db, Me, minutesAgo: 2, isRead: false);
        await SeedAsync(db, Me, minutesAgo: 3, isRead: true);   // read → not counted
        await SeedAsync(db, Other, minutesAgo: 1, isRead: false); // other user → not counted

        var svc = NewService(db, Me);
        var unread = await svc.GetUnreadCountAsync();

        Assert.Equal(2, unread.Count);
    }

    [Fact]
    public async Task MarkAsReadAsync_AnotherUsersRow_ThrowsNotFound()
    {
        await using var db = NotificationTestDb.New();
        var othersId = await SeedAsync(db, Other, minutesAgo: 1, isRead: false);

        var svc = NewService(db, Me);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.MarkAsReadAsync(othersId));

        // The other user's row is untouched (still unread).
        var row = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == othersId);
        Assert.False(row.IsRead);
        Assert.Null(row.ReadAtUtc);
    }

    [Fact]
    public async Task MarkAsReadAsync_OwnRow_SetsReadAndStampsReadAt()
    {
        await using var db = NotificationTestDb.New();
        var id = await SeedAsync(db, Me, minutesAgo: 1, isRead: false);

        var svc = NewService(db, Me);
        var dto = await svc.MarkAsReadAsync(id);

        Assert.True(dto.IsRead);
        Assert.Equal(Now, dto.ReadAtUtc);

        // Persisted.
        var row = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.True(row.IsRead);
        Assert.Equal(Now, row.ReadAtUtc);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_FlipsOnlyCallersUnread_LeavesOtherUserUntouched()
    {
        await using var db = NotificationTestDb.New();
        var mine1 = await SeedAsync(db, Me, minutesAgo: 1, isRead: false);
        var mine2 = await SeedAsync(db, Me, minutesAgo: 2, isRead: false);
        var othersUnread = await SeedAsync(db, Other, minutesAgo: 1, isRead: false);

        var svc = NewService(db, Me);
        await svc.MarkAllAsReadAsync();

        var rows = await db.Notifications.AsNoTracking().ToListAsync();
        // Both of mine are now read + stamped.
        foreach (var id in new[] { mine1, mine2 })
        {
            var r = rows.Single(n => n.Id == id);
            Assert.True(r.IsRead);
            Assert.Equal(Now, r.ReadAtUtc);
        }

        // The other user's unread row is untouched.
        var other = rows.Single(n => n.Id == othersUnread);
        Assert.False(other.IsRead);
        Assert.Null(other.ReadAtUtc);

        // The caller now has zero unread; the other user still has one.
        Assert.Equal(0, (await svc.GetUnreadCountAsync()).Count);
    }
}
