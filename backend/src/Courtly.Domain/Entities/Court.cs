namespace Courtly.Domain.Entities;

/// <summary>Represents a bookable court with location, pricing, and related media/amenities.</summary>
public class Court
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public long CityId { get; set; }
    public City City { get; set; } = null!;

    public long SurfaceTypeId { get; set; }
    public SurfaceType SurfaceType { get; set; } = null!;

    public long CourtTypeId { get; set; }
    public CourtType CourtType { get; set; } = null!;

    public bool IsIndoor { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }

    public decimal HourlyPrice { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public ICollection<CourtImage> Images { get; set; } = new List<CourtImage>();
    public ICollection<CourtAmenity> Amenities { get; set; } = new List<CourtAmenity>();
    public ICollection<CourtMaintenanceLog> MaintenanceLogs { get; set; } = new List<CourtMaintenanceLog>();
    public ICollection<TimeSlot> TimeSlots { get; set; } = new List<TimeSlot>();
}
