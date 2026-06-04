namespace Courtly.Infrastructure.Configuration;

/// <summary>PostgreSQL settings, bound from the <c>DB_CONNECTION_STRING</c> env key.</summary>
public sealed class DbOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}
