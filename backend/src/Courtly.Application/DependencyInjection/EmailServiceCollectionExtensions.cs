using Courtly.Application.Abstractions;
using Courtly.Application.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>DI wiring for the real SMTP email sender (feature 17).</summary>
public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real SMTP sender as <see cref="IEmailSender"/>, replacing the logging stub. Only the
    /// Worker wires this — the API keeps the logging sender for password-reset, since email delivery is the Worker's
    /// responsibility. Scoped to match the other request-scoped services.
    /// </summary>
    public static IServiceCollection AddCourtlySmtpEmail(this IServiceCollection services)
    {
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
