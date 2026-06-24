using Courtly.Application.Common.Exceptions;
using Courtly.Application.Reference;
using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Reference;

/// <summary>
/// Feature 9 DoD for the Amenity slice: create returns a DTO, a duplicate Name is a conflict, the name search
/// filters at query time, and delete is restricted while court-amenities reference the amenity. Mirrors the
/// golden Country in-memory DbContext harness (EF InMemory provider, fresh DB per test). Amenities have no
/// lookup cache, so the service is built without a MemoryCache. The search test relies on
/// <see cref="AmenityService"/>'s provider guard — under InMemory it uses a lowered <c>Contains</c> instead of
/// the Postgres-only <c>EF.Functions.ILike</c>, so the same test exercises case-insensitive filtering here.
/// </summary>
public class AmenityServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"amenities-{Guid.NewGuid()}")
            .Options);

    private static AmenityService NewService(CourtlyDbContext db) =>
        new(db, NullLogger<AmenityService>.Instance);

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var dto = await service.CreateAsync(new CreateAmenityRequest("Parking", "icon-parking"));

        Assert.True(dto.Id > 0);
        Assert.Equal("Parking", dto.Name);
        Assert.Equal("icon-parking", dto.IconKey);
        Assert.Equal(1, await db.Amenities.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_duplicate_name_throws_Conflict()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await service.CreateAsync(new CreateAmenityRequest("Parking", "icon-parking"));

        // Same name (any casing), different icon → still a conflict (duplicate guard is Name only).
        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(new CreateAmenityRequest("parking", "icon-other")));
    }

    [Fact]
    public async Task GetPagedAsync_search_filters_by_name_case_insensitively()
    {
        await using var db = NewDb();
        db.Amenities.AddRange(
            new Amenity { Name = "Parking", IconKey = "icon-parking" },
            new Amenity { Name = "Showers", IconKey = "icon-showers" },
            new Amenity { Name = "Lockers", IconKey = "icon-lockers" });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), "park");

        var only = Assert.Single(page.Items);
        Assert.Equal("Parking", only.Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_court_amenity_references_the_amenity()
    {
        await using var db = NewDb();
        var amenity = new Amenity { Name = "Parking", IconKey = "icon-parking" };
        db.Amenities.Add(amenity);
        await db.SaveChangesAsync();
        db.CourtAmenities.Add(new CourtAmenity { CourtId = 1, AmenityId = amenity.Id });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(amenity.Id));
        Assert.Contains("Parking", ex.Message);              // reason names the amenity
        Assert.Contains("1 court-amenities", ex.Message);    // and the referencing count
        Assert.Equal(1, await db.Amenities.CountAsync());    // not deleted
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }
}
