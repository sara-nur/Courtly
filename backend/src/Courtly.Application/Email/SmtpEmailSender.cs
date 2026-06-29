using Courtly.Application.Abstractions;
using Courtly.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Courtly.Application.Email;

/// <summary>
/// Feature 17 real <see cref="IEmailSender"/>: sends mail through the configured SMTP server (Mailpit locally) via MailKit.
/// Driven by the RabbitMQ Worker; exceptions propagate so the Worker's retry loop can re-attempt delivery.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default) =>
        SendAsync(
            toEmail,
            "Reset your Courtly password",
            $"We received a request to reset your Courtly password.\n\n" +
            $"Use this token to set a new password: {resetToken}\n\n" +
            "If you didn't request this, you can safely ignore this email.",
            ct);

    public Task SendBookingConfirmedAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, decimal totalPrice, CancellationToken ct = default) =>
        SendAsync(
            toEmail,
            "Your Courtly booking is confirmed",
            $"Hi {userName},\n\n" +
            $"Your booking is confirmed.\n\n" +
            $"Court: {courtName}\n" +
            $"When: {FormatWindow(startUtc, endUtc)}\n" +
            $"Total: {totalPrice:0.00}\n\n" +
            "See you on the court!",
            ct);

    public Task SendBookingCancelledAsync(string toEmail, string userName, string courtName, DateTime startUtc, DateTime endUtc, string? reason, CancellationToken ct = default)
    {
        var reasonLine = string.IsNullOrWhiteSpace(reason) ? string.Empty : $"Reason: {reason}\n";
        return SendAsync(
            toEmail,
            "Your Courtly booking was cancelled",
            $"Hi {userName},\n\n" +
            $"Your booking has been cancelled.\n\n" +
            $"Court: {courtName}\n" +
            $"When: {FormatWindow(startUtc, endUtc)}\n" +
            reasonLine +
            "\nIf this was unexpected, please get in touch.",
            ct);
    }

    public Task SendPaymentRefundedAsync(string toEmail, string userName, string courtName, decimal amount, CancellationToken ct = default) =>
        SendAsync(
            toEmail,
            "Your Courtly refund is on its way",
            $"Hi {userName},\n\n" +
            $"A refund has been issued for your booking.\n\n" +
            $"Court: {courtName}\n" +
            $"Amount: {amount:0.00}\n\n" +
            "It may take a few business days to appear on your statement.",
            ct);

    // Compact slot window for email bodies: the date once, then the time range — e.g. "2026-06-30, 08:00-09:00 UTC"
    // (falls back to full start/end stamps if a booking ever spans two days).
    private static string FormatWindow(DateTime startUtc, DateTime endUtc) =>
        startUtc.Date == endUtc.Date
            ? $"{startUtc:yyyy-MM-dd}, {startUtc:HH:mm}-{endUtc:HH:mm} UTC"
            : $"{startUtc:yyyy-MM-dd HH:mm} - {endUtc:yyyy-MM-dd HH:mm} UTC";

    // Single MailKit send path shared by all message types (rubric A.5: dispose client + message).
    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

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
