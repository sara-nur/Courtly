using System.Globalization;
using Courtly.Contracts.Messaging;
using Courtly.Domain.Enums;

namespace Courtly.Application.Notifications;

/// <summary>
/// Pure mapping from a consumed reservation/payment event (feature 17) to the <see cref="NotificationType"/> + the
/// human title/text persisted on the in-app notification (feature 18). No I/O — the Worker calls this, then hands the
/// result to <see cref="INotificationWriter"/>. Numbers/dates are formatted with <see cref="CultureInfo.InvariantCulture"/>
/// so the stored copy is stable regardless of the Worker's locale.
/// </summary>
public static class NotificationFactory
{
    private const string CourtFallback = "the court";

    /// <summary>
    /// Builds the notification category + title + body for <paramref name="routingKey"/>. Exactly one of
    /// <paramref name="reservation"/>/<paramref name="payment"/> carries the data for that key (reservation.* uses
    /// the reservation event, payment.* the payment event); <paramref name="courtName"/> may be null (a generic
    /// fallback is used). An unrecognized routing key throws <see cref="ArgumentOutOfRangeException"/> so the Worker
    /// can treat it as a poison message.
    /// </summary>
    public static (NotificationType Type, string Title, string Text) Build(
        string routingKey, ReservationEvent? reservation, PaymentEvent? payment, string? courtName)
    {
        var court = string.IsNullOrWhiteSpace(courtName) ? CourtFallback : courtName.Trim();

        return routingKey switch
        {
            ReservationRoutingKeys.Created => (
                NotificationType.ReservationCreated,
                "Booking created",
                $"Your booking for {court} at {SlotStart(reservation)} has been created. "
                    + "Pay the hold to confirm it before it expires."),

            ReservationRoutingKeys.Confirmed => (
                NotificationType.ReservationConfirmed,
                "Booking confirmed",
                $"Your booking for {court} at {SlotStart(reservation)} is confirmed."),

            ReservationRoutingKeys.Cancelled => (
                NotificationType.ReservationCancelled,
                "Booking cancelled",
                CancelledText(reservation, court)),

            ReservationRoutingKeys.Rescheduled => (
                NotificationType.ReservationRescheduled,
                "Booking rescheduled",
                RescheduledText(reservation, court)),

            ReservationRoutingKeys.Completed => (
                NotificationType.ReservationCompleted,
                "Booking completed",
                $"Your booking for {court} at {SlotStart(reservation)} has been completed."),

            PaymentRoutingKeys.Succeeded => (
                NotificationType.PaymentSucceeded,
                "Payment received",
                $"We received your payment of {Money(SucceededAmount(payment))} for {court}."),

            PaymentRoutingKeys.Refunded => (
                NotificationType.PaymentRefunded,
                "Refund issued",
                $"A refund of {Money(RefundedAmount(payment))} for {court} has been issued."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(routingKey), routingKey, "Unknown notification routing key."),
        };
    }

    /// <summary>Formats the reservation's slot start as <c>yyyy-MM-dd HH:mm UTC</c> (invariant).</summary>
    private static string SlotStart(ReservationEvent? reservation) =>
        reservation is null
            ? "the scheduled time"
            : reservation.SlotStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Cancellation body, appending the reason only when the event carries one.</summary>
    private static string CancelledText(ReservationEvent? reservation, string court)
    {
        var text = $"Your booking for {court} at {SlotStart(reservation)} has been cancelled.";
        var reason = reservation?.Reason?.Trim();
        return string.IsNullOrEmpty(reason) ? text : $"{text} Reason: {reason}";
    }

    /// <summary>Reschedule body, using the new slot start and appending the reason ("Rescheduled from {old} to {new}") when present.</summary>
    private static string RescheduledText(ReservationEvent? reservation, string court)
    {
        var text = $"Your booking for {court} has been rescheduled to {SlotStart(reservation)}.";
        var reason = reservation?.Reason?.Trim();
        return string.IsNullOrEmpty(reason) ? text : $"{text} {reason}";
    }

    /// <summary>The amount on a <c>payment.succeeded</c> event (the catalog amount).</summary>
    private static decimal SucceededAmount(PaymentEvent? payment) => payment?.Amount ?? 0m;

    /// <summary>The refunded amount: prefer the actually-charged cents when present, else the catalog amount.</summary>
    private static decimal RefundedAmount(PaymentEvent? payment) =>
        payment is null
            ? 0m
            : payment.AmountChargedCents.HasValue
                ? payment.AmountChargedCents.Value / 100m
                : payment.Amount;

    /// <summary>Formats a money amount as <c>0.00</c> (invariant), so the stored text never shifts with locale.</summary>
    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
