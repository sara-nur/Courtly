using Courtly.Application.Notifications;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Notifications;

/// <summary>
/// Feature 18 (auto): <see cref="NotificationWriter.CreateAsync"/> inserts exactly one unread <c>Notification</c> owned
/// by the supplied recipient (the event's user, not a JWT), stamped with the clock's UTC now, and returns the persisted
/// DTO (generated id + <c>TypeName == Type.ToString()</c> + <c>ReadAtUtc == null</c>). EF in-memory, fixed clock.
/// </summary>
public class NotificationWriterTests
{
    private static readonly DateTime Now = new(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_PersistsExactlyOneUnreadRowWithGivenFields()
    {
        await using var db = NotificationTestDb.New();
        var clock = new TestClock { UtcNow = Now };
        var writer = new NotificationWriter(db, clock);
        var userId = Guid.NewGuid();

        var dto = await writer.CreateAsync(
            userId, NotificationType.ReservationConfirmed, "Booking confirmed", "Your booking is confirmed.");

        // Exactly one row, with the given owner/type/title/text, unread, stamped from the clock.
        var row = Assert.Single(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(row.Id, dto.Id); // the returned DTO describes the row that was persisted
        Assert.Equal(userId, row.UserId);
        Assert.Equal(NotificationType.ReservationConfirmed, row.Type);
        Assert.Equal("Booking confirmed", row.Title);
        Assert.Equal("Your booking is confirmed.", row.Text);
        Assert.False(row.IsRead);
        Assert.Null(row.ReadAtUtc);
        Assert.Equal(Now, row.CreatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_ReturnsDtoWithGeneratedIdAndTypeNameMatchingEnum()
    {
        await using var db = NotificationTestDb.New();
        var writer = new NotificationWriter(db, new TestClock { UtcNow = Now });
        var userId = Guid.NewGuid();

        var dto = await writer.CreateAsync(
            userId, NotificationType.PaymentSucceeded, "Payment received", "We received your payment.");

        var row = Assert.Single(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.NotEqual(0, dto.Id);               // generated (non-zero) id
        Assert.Equal(row.Id, dto.Id);             // the persisted row's id
        Assert.Equal(NotificationType.PaymentSucceeded, dto.Type);
        Assert.Equal(NotificationType.PaymentSucceeded.ToString(), dto.TypeName); // TypeName == Type.ToString()
        Assert.Equal("Payment received", dto.Title);
        Assert.Equal("We received your payment.", dto.Text);
        Assert.False(dto.IsRead);
        Assert.Null(dto.ReadAtUtc);
        Assert.Equal(Now, dto.CreatedAtUtc);
    }
}
