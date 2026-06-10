namespace Courtly.Infrastructure.Persistence.Seeding;

/// <summary>
/// Fixed primary keys for seeded rows. HasData requires deterministic keys (no <c>Guid.NewGuid()</c> /
/// <c>DateTime.Now</c>), and the runtime seeder reuses the same constants so its foreign keys line up with the
/// reference rows the migration inserted. Long-keyed dynamic rows (courts, slots, reservations) are DB-generated
/// and are not listed here; only roles, reference data, and the fixed test users need stable ids.
/// </summary>
public static class SeedIds
{
    // Roles (IdentityRole<Guid>)
    public static readonly Guid RoleAdmin = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid RoleStaff = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid RoleUser = new("33333333-3333-3333-3333-333333333333");

    // Test users (AppUser) — fixed so reservations/reviews/news authored by them stay stable across reseeds.
    public static readonly Guid UserAdmin = new("a0000000-0000-0000-0000-000000000001"); // desktop / test
    public static readonly Guid UserStaff = new("a0000000-0000-0000-0000-000000000002"); // staff   / test
    public static readonly Guid UserMobile = new("a0000000-0000-0000-0000-000000000003"); // mobile  / test
    public static readonly Guid UserEmma = new("a0000000-0000-0000-0000-000000000004"); // emma    / test

    // Reference data (long keys, inserted with explicit values by HasData)
    public const long CountryBosnia = 1;

    public const long CitySarajevo = 1;
    public const long CityMostar = 2;
    public const long CityTuzla = 3;

    public const long SurfaceClay = 1;
    public const long SurfaceGrass = 2;
    public const long SurfaceHard = 3;
    public const long SurfaceCarpet = 4;

    public const long CourtTypeSingles = 1;
    public const long CourtTypeDoubles = 2;
    public const long CourtTypeTraining = 3;
    public const long CourtTypeMulti = 4;

    public const long AmenityLights = 1;
    public const long AmenityShowers = 2;
    public const long AmenityLocker = 3;
    public const long AmenityParking = 4;
    public const long AmenityProShop = 5;
}
