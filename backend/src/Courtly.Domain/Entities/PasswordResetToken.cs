namespace Courtly.Domain.Entities;

/// <summary>A persisted, single-use password-reset token (stored hashed) issued to a user.</summary>
public class PasswordResetToken
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
