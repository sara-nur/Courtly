namespace Courtly.Application.Abstractions;

/// <summary>
/// Outbound email seam. Feature 5 ships a logging no-op implementation; Feature 17 swaps in the real
/// SMTP sender driven by the RabbitMQ Worker.
/// </summary>
public interface IEmailSender
{
    Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default);

    /// <summary>Notifies the user their booking is confirmed (court, slot window, total charged).</summary>
    Task SendBookingConfirmedAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, decimal totalPrice, CancellationToken ct = default);

    /// <summary>Notifies the user their booking was cancelled, with an optional reason.</summary>
    Task SendBookingCancelledAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, string? reason, CancellationToken ct = default);

    /// <summary>Notifies the user a refund was issued for their booking.</summary>
    Task SendPaymentRefundedAsync(string toEmail, string userName, string courtName, decimal amount, CancellationToken ct = default);
}
