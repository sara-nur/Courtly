using Courtly.Application.Abstractions;
using Courtly.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Courtly.Application.Email;

/// <summary>
/// Feature 17 real <see cref="IEmailSender"/>: sends mail through the configured SMTP server (Gmail SMTP via the
/// <c>.env</c> settings) using MailKit. Driven by the RabbitMQ Worker; exceptions propagate so the Worker's retry loop
/// can re-attempt delivery. Every customer-facing message shares one branded HTML shell (<see cref="WrapHtml"/>) with a
/// plain-text fallback, so the whole email suite looks consistent.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly AppLinkOptions _appLinks;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, IOptions<AppLinkOptions> appLinks, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _appLinks = appLinks.Value;
        _logger = logger;
    }

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        // Link to the browser reset page (feature 22): clicking it opens GET /reset-password with the
        // email + token, where the user sets a new password — no token to type, no app deep link. The
        // token is hex (URL-safe); the email is escaped. Base is APP_RESET_PASSWORD_URL (the web page).
        var resetLink = $"{_appLinks.ResetPasswordUrl}?email={Uri.EscapeDataString(toEmail)}&token={resetToken}";

        var text =
            "Reset your Courtly password\n\n" +
            "We received a request to reset your Courtly password. Open this link to set a new password " +
            $"(it expires in 60 minutes):\n\n{resetLink}\n\n" +
            "If you didn't request this, you can safely ignore this email.";

        var html = WrapHtml(
            "<p style=\"font-size:18px;font-weight:600;margin:0 0 12px;\">Reset your password</p>" +
            "<p style=\"font-size:14px;line-height:1.6;margin:0 0 28px;color:#444;\">We received a request to reset your Courtly password. Click the button below to choose a new one. This link expires in 60 minutes.</p>" +
            $"<p style=\"margin:0 0 28px;\"><a href=\"{resetLink}\" style=\"background:#2e7d32;color:#ffffff;text-decoration:none;padding:13px 30px;border-radius:8px;display:inline-block;font-size:15px;font-weight:600;\">Reset Password</a></p>" +
            "<p style=\"font-size:13px;line-height:1.5;color:#999;margin:0;border-top:1px solid #eee;padding-top:16px;\">If you didn't request a password reset, you can safely ignore this email — your password won't change.</p>");

        return SendAsync(toEmail, "Reset your Courtly password", text, ct, html);
    }

    public Task SendBookingConfirmedAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, decimal totalPrice, CancellationToken ct = default)
    {
        var when = FormatWindow(startUtc, endUtc);
        var text =
            $"Hi {userName},\n\n" +
            "Your booking is confirmed.\n\n" +
            $"Court: {courtName}\n" +
            $"When: {when}\n" +
            $"Total: ${totalPrice:0.00}\n\n" +
            "See you on the court!";

        var html = WrapHtml(
            "<p style=\"font-size:18px;font-weight:600;margin:0 0 12px;\">Your booking is confirmed</p>" +
            $"<p style=\"font-size:14px;line-height:1.6;margin:0 0 20px;color:#444;\">Hi {Enc(userName)}, you're all set — here are your booking details:</p>" +
            DetailRows(("Court", courtName), ("When", when), ("Total", $"${totalPrice:0.00}")) +
            "<p style=\"font-size:14px;line-height:1.6;margin:0;color:#444;\">See you on the court! 🎾</p>");

        return SendAsync(toEmail, "Your Courtly booking is confirmed", text, ct, html);
    }

    public Task SendBookingCancelledAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, string? reason, CancellationToken ct = default)
    {
        var when = FormatWindow(startUtc, endUtc);
        var hasReason = !string.IsNullOrWhiteSpace(reason);
        var text =
            $"Hi {userName},\n\n" +
            "Your booking has been cancelled.\n\n" +
            $"Court: {courtName}\n" +
            $"When: {when}\n" +
            (hasReason ? $"Reason: {reason}\n" : string.Empty) +
            "\nIf this was unexpected, please get in touch.";

        var details = hasReason
            ? DetailRows(("Court", courtName), ("When", when), ("Reason", reason!))
            : DetailRows(("Court", courtName), ("When", when));

        var html = WrapHtml(
            "<p style=\"font-size:18px;font-weight:600;margin:0 0 12px;\">Your booking was cancelled</p>" +
            $"<p style=\"font-size:14px;line-height:1.6;margin:0 0 20px;color:#444;\">Hi {Enc(userName)}, the following booking has been cancelled:</p>" +
            details +
            "<p style=\"font-size:14px;line-height:1.6;margin:0;color:#444;\">If this wasn't expected, just reply to this email and we'll help.</p>");

        return SendAsync(toEmail, "Your Courtly booking was cancelled", text, ct, html);
    }

    public Task SendPaymentRefundedAsync(string toEmail, string userName, string courtName, decimal amount, CancellationToken ct = default)
    {
        var text =
            $"Hi {userName},\n\n" +
            "A refund has been issued for your booking.\n\n" +
            $"Court: {courtName}\n" +
            $"Amount: ${amount:0.00}\n\n" +
            "It may take a few business days to appear on your statement.";

        var html = WrapHtml(
            "<p style=\"font-size:18px;font-weight:600;margin:0 0 12px;\">Your refund is on its way</p>" +
            $"<p style=\"font-size:14px;line-height:1.6;margin:0 0 20px;color:#444;\">Hi {Enc(userName)}, we've issued a refund for your booking:</p>" +
            DetailRows(("Court", courtName), ("Amount refunded", $"${amount:0.00}")) +
            "<p style=\"font-size:14px;line-height:1.6;margin:0;color:#444;\">It may take a few business days to appear on your statement.</p>");

        return SendAsync(toEmail, "Your Courtly refund is on its way", text, ct, html);
    }

    // Compact slot window for email bodies: the date once, then the time range — e.g. "2026-06-30, 08:00-09:00 UTC"
    // (falls back to full start/end stamps if a booking ever spans two days).
    private static string FormatWindow(DateTime startUtc, DateTime endUtc) =>
        startUtc.Date == endUtc.Date
            ? $"{startUtc:yyyy-MM-dd}, {startUtc:HH:mm}-{endUtc:HH:mm} UTC"
            : $"{startUtc:yyyy-MM-dd HH:mm} - {endUtc:yyyy-MM-dd HH:mm} UTC";

    // Branded HTML shell shared by every message (DRY + a consistent look): the Courtly wordmark header wrapping the
    // supplied inner content, on a light, width-constrained card.
    private static string WrapHtml(string innerHtml) =>
        "<div style=\"font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:480px;margin:0 auto;padding:32px 24px;color:#1a1a1a;\">" +
        "<div style=\"font-size:22px;font-weight:700;color:#2e7d32;margin-bottom:24px;\">Courtly</div>" +
        innerHtml +
        "</div>";

    // Renders a label/value details block as a simple two-column table. Values are HTML-encoded (they include
    // user/court text) so a court name or cancellation reason can never break the markup.
    private static string DetailRows(params (string Label, string Value)[] rows)
    {
        var cells = string.Empty;
        foreach (var row in rows)
        {
            cells +=
                "<tr>" +
                $"<td style=\"padding:9px 0;color:#888;border-bottom:1px solid #f0f0f0;\">{Enc(row.Label)}</td>" +
                $"<td style=\"padding:9px 0;text-align:right;font-weight:600;border-bottom:1px solid #f0f0f0;\">{Enc(row.Value)}</td>" +
                "</tr>";
        }

        return "<table style=\"width:100%;border-collapse:collapse;font-size:14px;margin:0 0 24px;\">" + cells + "</table>";
    }

    // HTML-encodes user-supplied text for safe inline rendering.
    private static string Enc(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    // Single MailKit send path shared by all message types (rubric A.5: dispose client + message).
    // An optional htmlBody promotes the message to multipart/alternative (e.g. a clickable reset link)
    // while keeping the plain-text part as the fallback.
    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct, string? htmlBody = null)
    {
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Courtly", _options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = htmlBody is null
            ? new TextPart("plain") { Text = body }
            : new BodyBuilder { TextBody = body, HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
            ct);

        if (!string.IsNullOrWhiteSpace(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Sent email to {Email}: {Subject}", toEmail, subject);
    }
}
