namespace Courtly.Infrastructure.Configuration;

/// <summary>Mailtrap SMTP sandbox settings (consumed by feature 17: worker email). Bound from the <c>SMTP_*</c> env keys.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 2525;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}
