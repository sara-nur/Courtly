using Courtly.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Email;

/// <summary>
/// Feature 5 placeholder <see cref="IEmailSender"/>: logs the reset token instead of sending mail so the
/// flow is testable end-to-end before the Mailtrap Worker exists. Feature 17 replaces this registration.
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
}
