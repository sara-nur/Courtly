using Microsoft.AspNetCore.Identity;

namespace Courtly.Domain.Entities;

/// <summary>Application user (extends ASP.NET Identity) with profile and domain attributes.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public long? CityId { get; set; }
    public City? City { get; set; }
    public bool IsActive { get; set; }
    public byte[]? AvatarBytes { get; set; }
    public string? AvatarContentType { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
