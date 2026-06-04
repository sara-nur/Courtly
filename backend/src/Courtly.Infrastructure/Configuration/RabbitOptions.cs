namespace Courtly.Infrastructure.Configuration;

/// <summary>RabbitMQ broker connection settings. Bound from the <c>RABBITMQ_*</c> env keys.</summary>
public sealed class RabbitOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
