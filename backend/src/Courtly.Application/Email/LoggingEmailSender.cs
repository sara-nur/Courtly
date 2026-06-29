using Courtly.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Email;

/// <summary>
/// Feature 5 placeholder <see cref="IEmailSender"/>: logs the reset token instead of sending mail so the
/// flow is testable end-to-end before the SMTP Worker exists. Feature 17 replaces this registration.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        // Dev-only: the real sender (F17) emails a reset link; here we surface the token for manual testing.
        _logger.LogInformation("Password reset token for {Email}: {ResetToken}", toEmail, resetToken);
        return Task.CompletedTask;
    }

    public Task SendBookingConfirmedAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, decimal totalPrice, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Booking confirmed for {Email}: {Court} {StartUtc:o}-{EndUtc:o}, total {TotalPrice}",
            toEmail, courtName, startUtc, endUtc, totalPrice);
        return Task.CompletedTask;
    }

    public Task SendBookingCancelledAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, string? reason, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Booking cancelled for {Email}: {Court} {StartUtc:o}-{EndUtc:o}, reason {Reason}",
            toEmail, courtName, startUtc, endUtc, reason);
        return Task.CompletedTask;
    }

    public Task SendPaymentRefundedAsync(string toEmail, string userName, string courtName, decimal amount, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Payment refunded for {Email}: {Court}, amount {Amount}",
            toEmail, courtName, amount);
        return Task.CompletedTask;
    }
}
