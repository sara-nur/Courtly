using Courtly.Application.Payments;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Payments;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// The payments API (feature 16, Stripe server-side). A thin controller over <see cref="IPaymentService"/>: the
/// idempotency, ownership, pricing and the reservation transition all live in the service, never here (rubric §7/§7.1).
/// <c>POST intent</c> and <c>POST {id}/refund</c> require authentication (the latter is Admin/Staff only); ownership is
/// taken from the JWT in the service, never the route/body. <c>POST webhook</c> is the one
/// <see cref="AllowAnonymousAttribute"/> exception — Stripe can't present a JWT, so it is authenticated by its HMAC
/// signature (verified in the service against <c>STRIPE_WEBHOOK_SECRET</c>), exactly as the rubric's §7.1 server-side
/// finalization requires. The client never records success.
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
public sealed class PaymentsController : ControllerBase
{
    // Constant role list for [Authorize] (constant string concatenation → valid attribute argument).
    private const string AdminOrStaff = Roles.Admin + "," + Roles.Staff;
    private const string StripeSignatureHeader = "Stripe-Signature";

    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments)
    {
        _payments = payments;
    }

    /// <summary>Creates (or re-fetches) the Stripe PaymentIntent for the caller's Pending, unpaid reservation and
    /// returns what the in-app PaymentSheet needs (client secret + publishable key + server-owned amount).</summary>
    [HttpPost("intent")]
    public async Task<ActionResult<PaymentIntentResponse>> CreateIntent(
        CreatePaymentIntentRequest request, CancellationToken ct)
        => Ok(await _payments.CreateIntentAsync(request, ct));

    /// <summary>Stripe webhook — the server-side finalization. Authenticated by its HMAC signature (not a JWT); the
    /// raw body is read unparsed for signature verification. On <c>payment_intent.succeeded</c> the payment is
    /// finalized idempotently and the reservation confirmed; a replayed event is a no-op.</summary>
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers[StripeSignatureHeader].ToString();

        await _payments.ProcessWebhookAsync(payload, signature, ct);
        return Ok();
    }

    /// <summary>Refunds a paid reservation (Admin/Staff): refunds the actually-charged amount via Stripe, marks the
    /// payment Refunded and cancels the reservation with the supplied reason.</summary>
    [Authorize(Roles = AdminOrStaff)]
    [HttpPost("{reservationId:long}/refund")]
    public async Task<ActionResult<PaymentDto>> Refund(
        long reservationId, RefundReservationRequest request, CancellationToken ct)
        => Ok(await _payments.RefundAsync(reservationId, request, ct));
}
