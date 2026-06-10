namespace Courtly.Domain.Entities;

/// <summary>A city located within a country.</summary>
public class City
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long CountryId { get; set; }

    public Country Country { get; set; } = null!;
}
