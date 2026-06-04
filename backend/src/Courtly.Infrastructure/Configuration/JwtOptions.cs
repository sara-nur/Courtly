namespace Courtly.Infrastructure.Configuration;

/// <summary>JWT issuance settings (consumed by feature 5: auth). Bound from the <c>JWT_*</c> env keys.</summary>
public sealed class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessMinutes { get; set; } = 15;
}
