namespace Courtly.Domain.Entities;

/// <summary>A published news article authored by a user.</summary>
public class News
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public byte[] ImageBytes { get; set; } = null!;
    public string ImageContentType { get; set; } = string.Empty;
    public DateTime PublishedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public Guid AuthorId { get; set; }
    public AppUser Author { get; set; } = null!;
}
