namespace Courtly.Application.Abstractions;

/// <summary>
/// Outbound email seam. Feature 5 ships a logging no-op implementation; Feature 17 swaps in the real
/// Mailtrap sender driven by the RabbitMQ Worker.
/// </summary>
public interface IEmailSender
{
    Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default);
}
