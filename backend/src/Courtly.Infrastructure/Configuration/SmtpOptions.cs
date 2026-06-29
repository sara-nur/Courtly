namespace Courtly.Infrastructure.Configuration;

/// <summary>SMTP sandbox settings (consumed by feature 17: worker email). Bound from the <c>SMTP_*</c> env keys.
/// Defaults target the local Mailpit container (plain SMTP on 1025, no auth); a hosted sandbox like Mailtrap works too.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1025;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;

    /// <summary>Use STARTTLS when the server requires it. Bound from <c>SMTP_USE_SSL</c>; false for Mailpit on 1025
    /// (plain SMTP), true for a hosted sandbox like Mailtrap on 2525.</summary>
    public bool UseStartTls { get; set; } = true;
}
