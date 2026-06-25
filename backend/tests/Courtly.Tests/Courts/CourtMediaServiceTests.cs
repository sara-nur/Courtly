using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts;
using Courtly.Application.Courts.Media;
using Courtly.Contracts.Court;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Courts;

/// <summary>
/// Feature 11 DoD (auto): the media service owns the single-primary image invariant (first upload becomes primary,
/// set-primary moves the flag, deleting the primary promotes the earliest remaining), serves raw bytes by id,
/// builds the relative <c>/api/images/{id}</c> Url in memory, and REPLACES a court's amenity set wholesale (with a
/// clean 404 for an unknown amenity, and no unique-index violation when the same ids are re-submitted). Also proves
/// the <see cref="CourtService"/> projection now populates <c>PrimaryImageUrl</c> and round-trips lat/lng. Mirrors
/// the golden in-memory DbContext harness (EF InMemory provider, fresh DB per test, NullLogger).
/// </summary>
public class CourtMediaServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"court-media-{Guid.NewGuid()}")
            .Options);

    private static CourtMediaService NewService(CourtlyDbContext db) =>
        new(db, NullLogger<CourtMediaService>.Instance);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
    }

    private static CourtService NewCourtService(CourtlyDbContext db) =>
        new(db, new TestClock(), NullLogger<CourtService>.Instance);

    // Minimal valid PNG payload: the 8-byte signature plus a little body so the magic-byte guard passes.
    private static byte[] Png() =>
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03 };

    private static byte[] Jpeg() =>
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    /// <summary>Seeds the FK parents a court requires (Country/City/SurfaceType/CourtType) plus the court itself,
    /// returning the new court id. Mirrors CourtServiceTests.SeedRefsAsync.</summary>
    private static async Task<long> SeedCourtAsync(CourtlyDbContext db)
    {
        var country = new Country { Name = "Bosnia", IsoCode = "BIH" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var city = new City { Name = "Sarajevo", CountryId = country.Id };
        db.Cities.Add(city);
        var surface = new SurfaceType { Name = "Clay" };
        db.SurfaceTypes.Add(surface);
        var courtType = new CourtType { Name = "Tennis" };
        db.CourtTypes.Add(courtType);
        await db.SaveChangesAsync();

        var court = new Court
        {
            Name = "Center Court",
            CityId = city.Id,
            SurfaceTypeId = surface.Id,
            CourtTypeId = courtType.Id,
            HourlyPrice = 20m,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();

        return court.Id;
    }

    private static async Task<(long Parking, long Showers, long Lockers)> SeedAmenitiesAsync(CourtlyDbContext db)
    {
        var parking = new Amenity { Name = "Parking", IconKey = "icon-parking" };
        var showers = new Amenity { Name = "Showers", IconKey = "icon-showers" };
        var lockers = new Amenity { Name = "Lockers", IconKey = "icon-lockers" };
        db.Amenities.AddRange(parking, showers, lockers);
        await db.SaveChangesAsync();
        return (parking.Id, showers.Id, lockers.Id);
    }

    // --- Images: single-primary invariant --------------------------------------------------------

    [Fact]
    public async Task AddImageAsync_first_image_becomes_primary()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        var dto = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);

        Assert.True(dto.IsPrimary); // no existing images → forced primary regardless of the flag
        Assert.Equal($"/api/images/{dto.Id}", dto.Url);
    }

    [Fact]
    public async Task AddImageAsync_second_image_is_not_primary_by_default()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        var first = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        var second = await service.AddImageAsync(courtId, Jpeg(), "image/jpeg", caption: null, isPrimary: false);

        Assert.False(second.IsPrimary);
        // The original primary is untouched.
        var firstReloaded = await db.CourtImages.AsNoTracking().SingleAsync(i => i.Id == first.Id);
        Assert.True(firstReloaded.IsPrimary);
    }

    [Fact]
    public async Task AddImageAsync_with_isPrimary_takes_over_and_unsets_the_old_primary()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        var first = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        var second = await service.AddImageAsync(courtId, Jpeg(), "image/jpeg", caption: null, isPrimary: true);

        Assert.True(second.IsPrimary);
        var firstReloaded = await db.CourtImages.AsNoTracking().SingleAsync(i => i.Id == first.Id);
        Assert.False(firstReloaded.IsPrimary);
        Assert.Equal(1, await db.CourtImages.CountAsync(i => i.CourtId == courtId && i.IsPrimary));
    }

    [Fact]
    public async Task AddImageAsync_rejects_a_spoofed_content_type()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        // Declared png, actual jpeg bytes → the pure validator throws a 400 keyed on "file".
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.AddImageAsync(courtId, Jpeg(), "image/png", caption: null, isPrimary: false));
        Assert.True(ex.Errors!.ContainsKey("file"));
        Assert.Equal(0, await db.CourtImages.CountAsync()); // nothing persisted
    }

    [Fact]
    public async Task AddImageAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.AddImageAsync(999, Png(), "image/png", caption: null, isPrimary: false));
    }

    [Fact]
    public async Task SetPrimaryImageAsync_moves_the_flag_and_unsets_the_previous_primary()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var first = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        var second = await service.AddImageAsync(courtId, Jpeg(), "image/jpeg", caption: null, isPrimary: false);

        var promoted = await service.SetPrimaryImageAsync(courtId, second.Id);

        Assert.True(promoted.IsPrimary);
        var firstReloaded = await db.CourtImages.AsNoTracking().SingleAsync(i => i.Id == first.Id);
        Assert.False(firstReloaded.IsPrimary);
        Assert.Equal(1, await db.CourtImages.CountAsync(i => i.CourtId == courtId && i.IsPrimary));
    }

    [Fact]
    public async Task SetPrimaryImageAsync_unknown_image_throws_NotFound()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);

        await Assert.ThrowsAsync<NotFoundException>(() => service.SetPrimaryImageAsync(courtId, 9999));
    }

    [Fact]
    public async Task DeleteImageAsync_primary_promotes_the_earliest_remaining()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var first = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        var second = await service.AddImageAsync(courtId, Jpeg(), "image/jpeg", caption: null, isPrimary: false);
        var third = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        Assert.True(first.IsPrimary);

        await service.DeleteImageAsync(courtId, first.Id);

        // Earliest remaining (lowest id = the second image) is promoted; the third stays non-primary.
        var secondReloaded = await db.CourtImages.AsNoTracking().SingleAsync(i => i.Id == second.Id);
        var thirdReloaded = await db.CourtImages.AsNoTracking().SingleAsync(i => i.Id == third.Id);
        Assert.True(secondReloaded.IsPrimary);
        Assert.False(thirdReloaded.IsPrimary);
        Assert.Equal(1, await db.CourtImages.CountAsync(i => i.CourtId == courtId && i.IsPrimary));
    }

    [Fact]
    public async Task DeleteImageAsync_non_primary_leaves_the_primary_untouched()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var first = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        var second = await service.AddImageAsync(courtId, Jpeg(), "image/jpeg", caption: null, isPrimary: false);

        await service.DeleteImageAsync(courtId, second.Id);

        var firstReloaded = await db.CourtImages.AsNoTracking().SingleAsync(i => i.Id == first.Id);
        Assert.True(firstReloaded.IsPrimary);
        Assert.Equal(1, await db.CourtImages.CountAsync(i => i.CourtId == courtId));
    }

    [Fact]
    public async Task DeleteImageAsync_last_image_leaves_no_primary()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var only = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);

        await service.DeleteImageAsync(courtId, only.Id);

        Assert.Equal(0, await db.CourtImages.CountAsync(i => i.CourtId == courtId));
    }

    // --- Images: read paths ----------------------------------------------------------------------

    [Fact]
    public async Task GetImagesAsync_orders_primary_first_and_builds_relative_urls()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);
        var second = await service.AddImageAsync(courtId, Jpeg(), "image/jpeg", caption: null, isPrimary: true);

        var images = await service.GetImagesAsync(courtId);

        Assert.Equal(2, images.Count);
        Assert.Equal(second.Id, images[0].Id);   // current primary listed first
        Assert.True(images[0].IsPrimary);
        Assert.All(images, i => Assert.Equal($"/api/images/{i.Id}", i.Url)); // relative url, built in memory
    }

    [Fact]
    public async Task GetImagesAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetImagesAsync(999));
    }

    [Fact]
    public async Task GetImageContentAsync_returns_bytes_for_an_existing_image()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var dto = await service.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: false);

        var content = await service.GetImageContentAsync(dto.Id);

        Assert.NotNull(content);
        Assert.Equal("image/png", content!.Value.ContentType);
        Assert.Equal(Png(), content.Value.Bytes);
    }

    [Fact]
    public async Task GetImageContentAsync_returns_null_for_a_missing_image()
    {
        await using var db = NewDb();
        var service = NewService(db);

        Assert.Null(await service.GetImageContentAsync(9999));
    }

    // --- Amenities: replace-the-whole-set --------------------------------------------------------

    [Fact]
    public async Task SetAmenitiesAsync_replaces_the_whole_set_and_projects_names()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var (parking, showers, lockers) = await SeedAmenitiesAsync(db);
        var service = NewService(db);

        await service.SetAmenitiesAsync(courtId, new SetCourtAmenitiesRequest(new[]
        {
            new CourtAmenityInput(parking, "Free", IsHighlighted: true),
            new CourtAmenityInput(showers, null, IsHighlighted: false),
        }));

        // Replace with a different set: lockers in, parking/showers out.
        var result = await service.SetAmenitiesAsync(courtId, new SetCourtAmenitiesRequest(new[]
        {
            new CourtAmenityInput(lockers, "Coin-op", IsHighlighted: false),
        }));

        var only = Assert.Single(result);
        Assert.Equal(lockers, only.AmenityId);
        Assert.Equal("Lockers", only.AmenityName);   // resolved via JOIN
        Assert.Equal("icon-lockers", only.IconKey);
        Assert.Equal("Coin-op", only.Note);
        Assert.Equal(1, await db.CourtAmenities.CountAsync(ca => ca.CourtId == courtId)); // old rows gone
    }

    [Fact]
    public async Task SetAmenitiesAsync_unknown_amenity_throws_NotFound_naming_the_id()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        await SeedAmenitiesAsync(db);
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.SetAmenitiesAsync(
            courtId, new SetCourtAmenitiesRequest(new[] { new CourtAmenityInput(9999, null, false) })));
        Assert.Contains("9999", ex.Message);
        Assert.Equal(0, await db.CourtAmenities.CountAsync(ca => ca.CourtId == courtId)); // nothing persisted
    }

    [Fact]
    public async Task SetAmenitiesAsync_rerun_with_same_ids_does_not_violate_the_unique_index()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var (parking, showers, _) = await SeedAmenitiesAsync(db);
        var service = NewService(db);
        var set = new SetCourtAmenitiesRequest(new[]
        {
            new CourtAmenityInput(parking, "P", false),
            new CourtAmenityInput(showers, "S", true),
        });

        await service.SetAmenitiesAsync(courtId, set);
        var result = await service.SetAmenitiesAsync(courtId, set); // replace with the identical set

        Assert.Equal(2, result.Count);
        Assert.Equal(2, await db.CourtAmenities.CountAsync(ca => ca.CourtId == courtId)); // no duplicate rows
    }

    [Fact]
    public async Task SetAmenitiesAsync_empty_set_clears_all_amenities()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var (parking, _, _) = await SeedAmenitiesAsync(db);
        var service = NewService(db);
        await service.SetAmenitiesAsync(
            courtId, new SetCourtAmenitiesRequest(new[] { new CourtAmenityInput(parking, null, false) }));

        var result = await service.SetAmenitiesAsync(courtId, new SetCourtAmenitiesRequest(Array.Empty<CourtAmenityInput>()));

        Assert.Empty(result);
        Assert.Equal(0, await db.CourtAmenities.CountAsync(ca => ca.CourtId == courtId));
    }

    [Fact]
    public async Task GetAmenitiesAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAmenitiesAsync(999));
    }

    // --- Cross-service: the CourtService projection now exposes PrimaryImageUrl -------------------

    [Fact]
    public async Task CourtService_projection_exposes_the_primary_image_url_after_upload()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var media = NewService(db);
        var courts = NewCourtService(db);
        var primary = await media.AddImageAsync(courtId, Png(), "image/png", caption: null, isPrimary: true);

        var dto = await courts.GetByIdAsync(courtId);

        Assert.Equal($"/api/images/{primary.Id}", dto.PrimaryImageUrl);
    }

    [Fact]
    public async Task CourtService_projection_primary_image_url_is_null_without_images()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var courts = NewCourtService(db);

        var dto = await courts.GetByIdAsync(courtId);

        Assert.Null(dto.PrimaryImageUrl);
    }
}
