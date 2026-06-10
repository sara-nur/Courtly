namespace Courtly.Domain.Entities;

/// <summary>A country that cities belong to.</summary>
public class Country
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string IsoCode { get; set; } = string.Empty;

    public ICollection<City> Cities { get; set; } = new List<City>();
}
