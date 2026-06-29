using System.Globalization;
using Courtly.Application.Notifications;
using Courtly.Contracts.Messaging;
using Courtly.Domain.Enums;
using Xunit;

namespace Courtly.Tests.Notifications;

/// <summary>
/// Feature 18 (auto): <see cref="NotificationFactory.Build"/> is the pure mapping from a consumed reservation/payment
/// event to the persisted notification's category + title + body. Each of the six routing keys must produce its exact
/// <see cref="NotificationType"/> and fixed title; the body is asserted by substring (court name, formatted amount,
/// cancellation reason) so the tests stay robust to wording. An unknown routing key is a poison message
/// (<see cref="ArgumentOutOfRangeException"/>). Money is formatted <c>0.00</c> invariant; the slot start uses the
/// agreed <c>yyyy-MM-dd HH:mm 'UTC'</c> invariant format.
/// </summary>
public class NotificationFactoryTests
{
    private const string CourtName = "Center Court";
    private static readonly DateTime SlotStart = new(2026, 7, 1, 14, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime SlotEnd = SlotStart.AddHours(1);
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly string ExpectedSlot =
        SlotStart.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static ReservationEvent Reservation(string routingKey, string? reason = null) =>
        new(
            RoutingKey: routingKey,
            ReservationId: 100,
            UserId: UserId,
            CourtId: 7,
            TimeSlotId: 9,
            Status: ReservationStatus.Pending,
            TotalPrice: 30m,
            SlotStartUtc: SlotStart,
            SlotEndUtc: SlotEnd,
            Reason: reason,
            OccurredAtUtc: SlotStart);

    private static PaymentEvent Payment(string routingKey, decimal amount, long? chargedCents = null) =>
        new(
            RoutingKey: routingKey,
            PaymentId: 55,
            ReservationId: 100,
            UserId: UserId,
            Amount: amount,
            AmountChargedCents: chargedCents,
            OccurredAtUtc: SlotStart);

    [Fact]
    public void Build_ReservationCreated_MapsTypeTitleAndMentionsCourtSlotAndHold()
    {
        var (type, title, text) = NotificationFactory.Build(
            ReservationRoutingKeys.Created, Reservation(ReservationRoutingKeys.Created), payment: null, CourtName);

        Assert.Equal(NotificationType.ReservationCreated, type);
        Assert.Equal("Booking created", title);
        Assert.Contains(CourtName, text);
        Assert.Contains(ExpectedSlot, text);
        Assert.Contains("Pay", text); // mentions paying the hold to confirm
    }

    [Fact]
    public void Build_ReservationConfirmed_MapsTypeTitleAndMentionsCourtAndSlot()
    {
        var (type, title, text) = NotificationFactory.Build(
            ReservationRoutingKeys.Confirmed, Reservation(ReservationRoutingKeys.Confirmed), payment: null, CourtName);

        Assert.Equal(NotificationType.ReservationConfirmed, type);
        Assert.Equal("Booking confirmed", title);
        Assert.Contains(CourtName, text);
        Assert.Contains(ExpectedSlot, text);
    }

    [Fact]
    public void Build_ReservationCancelled_MapsTypeTitleAndIncludesReasonWhenPresent()
    {
        const string reason = "Court flooded";
        var (type, title, text) = NotificationFactory.Build(
            ReservationRoutingKeys.Cancelled,
            Reservation(ReservationRoutingKeys.Cancelled, reason),
            payment: null,
            CourtName);

        Assert.Equal(NotificationType.ReservationCancelled, type);
        Assert.Equal("Booking cancelled", title);
        Assert.Contains(CourtName, text);
        Assert.Contains(ExpectedSlot, text);
        Assert.Contains(reason, text);
    }

    [Fact]
    public void Build_ReservationCompleted_MapsTypeTitleAndMentionsCourtAndSlot()
    {
        var (type, title, text) = NotificationFactory.Build(
            ReservationRoutingKeys.Completed, Reservation(ReservationRoutingKeys.Completed), payment: null, CourtName);

        Assert.Equal(NotificationType.ReservationCompleted, type);
        Assert.Equal("Booking completed", title);
        Assert.Contains(CourtName, text);
        Assert.Contains(ExpectedSlot, text);
    }

    [Fact]
    public void Build_PaymentSucceeded_MapsTypeTitleAndMentionsFormattedAmountAndCourt()
    {
        var (type, title, text) = NotificationFactory.Build(
            PaymentRoutingKeys.Succeeded,
            reservation: null,
            Payment(PaymentRoutingKeys.Succeeded, amount: 30m),
            CourtName);

        Assert.Equal(NotificationType.PaymentSucceeded, type);
        Assert.Equal("Payment received", title);
        Assert.Contains("30.00", text); // 0.00 invariant
        Assert.Contains(CourtName, text);
    }

    [Fact]
    public void Build_PaymentRefunded_PrefersChargedCentsForTheAmountAndMentionsCourt()
    {
        // Catalog Amount is 30.00 but the actually-charged cents are 2550 → the refund text uses 25.50.
        var (type, title, text) = NotificationFactory.Build(
            PaymentRoutingKeys.Refunded,
            reservation: null,
            Payment(PaymentRoutingKeys.Refunded, amount: 30m, chargedCents: 2550),
            CourtName);

        Assert.Equal(NotificationType.PaymentRefunded, type);
        Assert.Equal("Refund issued", title);
        Assert.Contains("25.50", text);
        Assert.Contains(CourtName, text);
    }

    [Fact]
    public void Build_PaymentRefunded_FallsBackToCatalogAmountWhenChargedCentsAbsent()
    {
        var (_, _, text) = NotificationFactory.Build(
            PaymentRoutingKeys.Refunded,
            reservation: null,
            Payment(PaymentRoutingKeys.Refunded, amount: 30m, chargedCents: null),
            CourtName);

        Assert.Contains("30.00", text);
    }

    [Fact]
    public void Build_NullCourtName_UsesAFallbackInsteadOfThrowing()
    {
        var (_, _, text) = NotificationFactory.Build(
            ReservationRoutingKeys.Confirmed,
            Reservation(ReservationRoutingKeys.Confirmed),
            payment: null,
            courtName: null);

        Assert.Contains("court", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_UnknownRoutingKey_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NotificationFactory.Build("reservation.rescheduled", reservation: null, payment: null, CourtName));
    }
}
