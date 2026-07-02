namespace Courtly.Domain.Enums;

/// <summary>Category of a system notification, used for routing/iconography on the client. Stored as int.</summary>
public enum NotificationType
{
    ReservationCreated = 0,
    ReservationConfirmed = 1,
    ReservationCancelled = 2,
    ReservationCompleted = 3,
    PaymentSucceeded = 4,
    PaymentRefunded = 5,
    General = 6,
    ReservationRescheduled = 7,
}
