using Courtly.Application.Common.Validation.Payments;
using Courtly.Contracts.Payments;
using Xunit;

namespace Courtly.Tests.Payments;

/// <summary>
/// Feature 16: server-side validation for the payment requests. The create-intent request is just a reservation id
/// (the real preconditions — ownership, Pending, unpaid — are business rules in the service); the refund request
/// requires a non-empty, bounded reason (rubric §4 — clear, format-specific messages; §7 — a cancellation/refund must
/// carry a reason).
/// </summary>
public class PaymentValidatorTests
{
    [Fact]
    public void CreateIntent_requires_a_positive_reservation_id()
    {
        var result = new CreatePaymentIntentRequestValidator().Validate(new CreatePaymentIntentRequest(0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePaymentIntentRequest.ReservationId));
    }

    [Fact]
    public void CreateIntent_accepts_a_real_reservation_id()
    {
        var result = new CreatePaymentIntentRequestValidator().Validate(new CreatePaymentIntentRequest(42));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Refund_requires_a_reason()
    {
        var result = new RefundReservationRequestValidator().Validate(new RefundReservationRequest(string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefundReservationRequest.Reason));
    }

    [Fact]
    public void Refund_rejects_an_overlong_reason()
    {
        var result = new RefundReservationRequestValidator()
            .Validate(new RefundReservationRequest(new string('x', 501)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Refund_accepts_a_normal_reason()
    {
        var result = new RefundReservationRequestValidator()
            .Validate(new RefundReservationRequest("Customer requested a refund"));

        Assert.True(result.IsValid);
    }
}
