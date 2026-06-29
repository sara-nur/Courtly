using System.Text.Json;
using Courtly.Contracts.Messaging;
using Courtly.Domain.Enums;
using Xunit;

namespace Courtly.Tests.Messaging;

/// <summary>
/// Feature 17: the API (publisher) and the Worker (consumer) exchange <see cref="ReservationEvent"/> /
/// <see cref="PaymentEvent"/> records as JSON over RabbitMQ, so the wire contract — driven solely by
/// <see cref="MessagingTopology.SerializerOptions"/> — must round-trip losslessly or the two sides drift silently.
/// These assert the rubric §A.4 invariants: enums serialize as readable strings (inspectable DLQ payloads), money is
/// exact (decimal, never float), and UTC <see cref="DateTime"/> values keep both their <see cref="DateTimeKind.Utc"/>
/// kind and value across the trip.
/// </summary>
public class EventSerializationTests
{
    [Theory]
    [InlineData("a slot just opened up")]
    [InlineData(null)]
    public void ReservationEvent_round_trips_through_the_shared_serializer(string? reason)
    {
        var original = new ReservationEvent(
            RoutingKey: ReservationRoutingKeys.Confirmed,
            ReservationId: 42,
            UserId: Guid.NewGuid(),
            CourtId: 7,
            TimeSlotId: 99,
            Status: ReservationStatus.Confirmed,
            TotalPrice: 123.45m,
            SlotStartUtc: DateTime.SpecifyKind(new DateTime(2026, 6, 27, 14, 0, 0), DateTimeKind.Utc),
            SlotEndUtc: DateTime.SpecifyKind(new DateTime(2026, 6, 27, 15, 30, 0), DateTimeKind.Utc),
            Reason: reason,
            OccurredAtUtc: DateTime.SpecifyKind(new DateTime(2026, 6, 27, 13, 59, 0), DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(original, MessagingTopology.SerializerOptions);

        // Enum must be a readable string on the wire, not its numeric value (DLQ payloads stay inspectable).
        Assert.Contains(nameof(ReservationStatus.Confirmed), json);

        var roundTripped = JsonSerializer.Deserialize<ReservationEvent>(json, MessagingTopology.SerializerOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(ReservationStatus.Confirmed, roundTripped!.Status);
        Assert.Equal(123.45m, roundTripped.TotalPrice); // decimal: exact, no float drift
        Assert.Equal(reason, roundTripped.Reason);

        // UTC datetimes keep both kind and value (rubric §A.4).
        Assert.Equal(DateTimeKind.Utc, roundTripped.SlotStartUtc.Kind);
        Assert.Equal(original.SlotStartUtc, roundTripped.SlotStartUtc);
        Assert.Equal(DateTimeKind.Utc, roundTripped.SlotEndUtc.Kind);
        Assert.Equal(original.SlotEndUtc, roundTripped.SlotEndUtc);
        Assert.Equal(DateTimeKind.Utc, roundTripped.OccurredAtUtc.Kind);
        Assert.Equal(original.OccurredAtUtc, roundTripped.OccurredAtUtc);

        // The remaining identity fields survive too.
        Assert.Equal(original.RoutingKey, roundTripped.RoutingKey);
        Assert.Equal(original.ReservationId, roundTripped.ReservationId);
        Assert.Equal(original.UserId, roundTripped.UserId);
        Assert.Equal(original.CourtId, roundTripped.CourtId);
        Assert.Equal(original.TimeSlotId, roundTripped.TimeSlotId);
    }

    [Theory]
    [InlineData(2500L)]
    [InlineData(null)]
    public void PaymentEvent_round_trips_through_the_shared_serializer(long? amountChargedCents)
    {
        var original = new PaymentEvent(
            RoutingKey: PaymentRoutingKeys.Refunded,
            PaymentId: 11,
            ReservationId: 42,
            UserId: Guid.NewGuid(),
            Amount: 25.00m,
            AmountChargedCents: amountChargedCents,
            OccurredAtUtc: DateTime.SpecifyKind(new DateTime(2026, 6, 27, 16, 0, 0), DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(original, MessagingTopology.SerializerOptions);
        var roundTripped = JsonSerializer.Deserialize<PaymentEvent>(json, MessagingTopology.SerializerOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(25.00m, roundTripped!.Amount); // decimal: exact
        Assert.Equal(amountChargedCents, roundTripped.AmountChargedCents); // nullable long, null and value

        Assert.Equal(DateTimeKind.Utc, roundTripped.OccurredAtUtc.Kind);
        Assert.Equal(original.OccurredAtUtc, roundTripped.OccurredAtUtc);

        Assert.Equal(original.RoutingKey, roundTripped.RoutingKey);
        Assert.Equal(original.PaymentId, roundTripped.PaymentId);
        Assert.Equal(original.ReservationId, roundTripped.ReservationId);
        Assert.Equal(original.UserId, roundTripped.UserId);
    }
}
