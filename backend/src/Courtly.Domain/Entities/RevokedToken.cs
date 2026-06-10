namespace Courtly.Domain.Entities;

/// <summary>A revoked access token tracked by its JTI to enforce server-side invalidation.</summary>
public class RevokedToken
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime RevokedAtUtc { get; set; }
}
