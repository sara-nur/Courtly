using Courtly.Domain.Constants;
using Courtly.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Courtly.Infrastructure.Persistence.Seeding;

/// <summary>
/// Static, deterministic definitions for the <c>HasData</c> half of the seed: identity roles and all reference
/// data (countries, cities, surfaces, court types, amenities). These are inserted by the EF migration, so values
/// must be constant — no <c>DateTime.Now</c>, no random. The runtime seeder (<see cref="CourtlyDataSeeder"/>)
/// owns the dynamic half (users, courts, images, slots, reservations) and depends on these rows existing.
/// </summary>
public static class SeedData
{
    // Fixed concurrency stamps keep the migration insert deterministic (HasData re-evaluates on every model build).
    public static readonly IReadOnlyList<IdentityRole<Guid>> IdentityRoles = new[]
    {
        Role(SeedIds.RoleAdmin, Roles.Admin, "r-admin-stamp"),
        Role(SeedIds.RoleStaff, Roles.Staff, "r-staff-stamp"),
        Role(SeedIds.RoleUser, Roles.User, "r-user-stamp"),
    };

    public static readonly IReadOnlyList<Country> Countries = new[]
    {
        new Country { Id = SeedIds.CountryBosnia, Name = "Bosnia and Herzegovina", IsoCode = "BIH" },
    };

    public static readonly IReadOnlyList<City> Cities = new[]
    {
        new City { Id = SeedIds.CitySarajevo, Name = "Sarajevo", CountryId = SeedIds.CountryBosnia },
        new City { Id = SeedIds.CityMostar, Name = "Mostar", CountryId = SeedIds.CountryBosnia },
        new City { Id = SeedIds.CityTuzla, Name = "Tuzla", CountryId = SeedIds.CountryBosnia },
    };

    public static readonly IReadOnlyList<SurfaceType> SurfaceTypes = new[]
    {
        new SurfaceType { Id = SeedIds.SurfaceClay, Name = "Clay", Description = "Slow surface, high bounce." },
        new SurfaceType { Id = SeedIds.SurfaceGrass, Name = "Grass", Description = "Fast surface, low bounce." },
        new SurfaceType { Id = SeedIds.SurfaceHard, Name = "Hard", Description = "Medium-fast, consistent bounce." },
        new SurfaceType { Id = SeedIds.SurfaceCarpet, Name = "Carpet", Description = "Indoor textile surface." },
    };

    public static readonly IReadOnlyList<CourtType> CourtTypes = new[]
    {
        new CourtType { Id = SeedIds.CourtTypeSingles, Name = "Singles", Description = "Standard singles court." },
        new CourtType { Id = SeedIds.CourtTypeDoubles, Name = "Doubles", Description = "Wider doubles court." },
        new CourtType { Id = SeedIds.CourtTypeTraining, Name = "Training", Description = "Practice / coaching court." },
        new CourtType { Id = SeedIds.CourtTypeMulti, Name = "Multi", Description = "Multi-purpose court." },
    };

    public static readonly IReadOnlyList<Amenity> Amenities = new[]
    {
        new Amenity { Id = SeedIds.AmenityLights, Name = "Floodlights", IconKey = "lights" },
        new Amenity { Id = SeedIds.AmenityShowers, Name = "Showers", IconKey = "showers" },
        new Amenity { Id = SeedIds.AmenityLocker, Name = "Lockers", IconKey = "locker" },
        new Amenity { Id = SeedIds.AmenityParking, Name = "Parking", IconKey = "parking" },
        new Amenity { Id = SeedIds.AmenityProShop, Name = "Pro Shop", IconKey = "proshop" },
    };

    private static IdentityRole<Guid> Role(Guid id, string name, string stamp) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = stamp,
    };
}
