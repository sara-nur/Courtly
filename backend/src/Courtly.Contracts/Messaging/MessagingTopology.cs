using System.Text.Json;
using System.Text.Json.Serialization;

namespace Courtly.Contracts.Messaging;

/// <summary>
/// The single source of truth for the RabbitMQ topology shared by the API (publisher) and the Worker (consumer,
/// feature 17), kept in <c>Courtly.Contracts</c> so neither side can drift (rubric §3.4: no duplicated magic strings).
/// <para>
/// Layout: a durable topic exchange <see cref="EventsExchange"/> carries every reservation/payment event keyed by the
/// constants in <see cref="ReservationRoutingKeys"/> / <see cref="PaymentRoutingKeys"/>. The durable
/// <see cref="EmailQueue"/> binds the keys in <see cref="EmailBindingKeys"/> and is configured with a dead-letter
/// exchange so a message that exhausts its retries (see <c>RetryPolicy</c>) is routed to the fanout
/// <see cref="DeadLetterExchange"/>, which delivers it to <see cref="EmailDeadLetterQueue"/> for manual inspection.
/// </para>
/// <para>
/// <see cref="PaymentRoutingKeys.Succeeded"/> is deliberately NOT in <see cref="EmailBindingKeys"/>: the payments engine
/// (feature 16) publishes <c>payment.succeeded</c> AND <c>reservation.confirmed</c> together when a charge finalizes, so
/// binding both would double-email the user. <c>reservation.confirmed</c> is the single booking-confirmation trigger.
/// </para>
/// </summary>
public static class MessagingTopology
{
    /// <summary>Durable topic exchange carrying all reservation/payment events.</summary>
    public const string EventsExchange = "courtly.events";

    /// <summary>Durable fanout exchange that receives dead-lettered messages.</summary>
    public const string DeadLetterExchange = "courtly.events.dlx";

    /// <summary>Durable queue feeding the email consumer; dead-letters to <see cref="DeadLetterExchange"/>.</summary>
    public const string EmailQueue = "q.email";

    /// <summary>Durable queue holding exhausted email messages for manual inspection.</summary>
    public const string EmailDeadLetterQueue = "q.email.dlq";

    /// <summary>Durable queue feeding the notification consumer (feature 18); dead-letters to <see cref="NotificationDeadLetterExchange"/>.</summary>
    public const string NotificationQueue = "q.notifications";

    /// <summary>
    /// Durable fanout exchange that receives dead-lettered notification messages. The notification queue has its OWN
    /// dead-letter exchange (separate from the email <see cref="DeadLetterExchange"/>): the email DLX is fanout, so a
    /// second DLQ bound to it would also receive the email queue's dead-letters and vice-versa — a dedicated exchange
    /// keeps each consumer's poison/exhausted messages in its own inspection queue.
    /// </summary>
    public const string NotificationDeadLetterExchange = "courtly.events.notifications.dlx";

    /// <summary>Durable queue holding exhausted notification messages for manual inspection.</summary>
    public const string NotificationDeadLetterQueue = "q.notifications.dlq";

    /// <summary>Routing keys the <see cref="EmailQueue"/> binds — the events that trigger a customer email.</summary>
    public static readonly string[] EmailBindingKeys =
    {
        ReservationRoutingKeys.Confirmed,
        ReservationRoutingKeys.Cancelled,
        ReservationRoutingKeys.Rescheduled,
        PaymentRoutingKeys.Refunded,
    };

    /// <summary>
    /// Routing keys the <see cref="NotificationQueue"/> binds — every reservation/payment event that produces a
    /// persisted in-app notification (feature 18). Unlike <see cref="EmailBindingKeys"/> this binds the full lifecycle
    /// (including <c>reservation.created</c>/<c>reservation.completed</c> and <c>payment.succeeded</c>): an in-app
    /// notification per event does not risk the double-email problem that omits those keys from the email queue.
    /// </summary>
    public static readonly string[] NotificationBindingKeys =
    {
        ReservationRoutingKeys.Created,
        ReservationRoutingKeys.Confirmed,
        ReservationRoutingKeys.Cancelled,
        ReservationRoutingKeys.Rescheduled,
        ReservationRoutingKeys.Completed,
        PaymentRoutingKeys.Succeeded,
        PaymentRoutingKeys.Refunded,
    };

    /// <summary>
    /// Shared JSON settings for publishing/consuming event records: web defaults plus a string-enum converter so enums
    /// (e.g. <c>ReservationStatus</c>) serialize as readable strings — keeping DLQ payloads inspectable — and
    /// <see cref="DateTime"/> values round-trip cleanly between publisher and consumer.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
