using Courtly.Domain.Enums;

namespace Courtly.Domain.Entities;

/// <summary>An in-app notification delivered to a user.</summary>
public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
