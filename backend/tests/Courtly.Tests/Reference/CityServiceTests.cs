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
/// Feature 9 DoD for the City slice: create returns a DTO (with CountryName joined), a missing CountryId is a
/// NotFound, a duplicate name is a conflict, the name search filters at query time, and delete is restricted
/// while courts reference the city. Mirrors the golden Country harness (EF InMemory provider, fresh DB per
/// test). The search test relies on <see cref="CityService"/>'s provider guard — under InMemory it uses a
/// lowered <c>Contains</c> instead of the Postgres-only <c>EF.Functions.ILike</c>, so the same test exercises
/// case-insensitive filtering here.
/// </summary>
public class CityServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"cities-{Guid.NewGuid()}")
            .Options);

    private static CityService NewService(CourtlyDbContext db) =>
        new(db, NullLogger<CityService>.Instance);

    private static async Task<long> SeedCountryAsync(CourtlyDbContext db, string name = "Bosnia", string iso = "BIH")
    {
        var country = new Country { Name = name, IsoCode = iso };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        return country.Id;
    }

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto_with_country_name()
    {
        await using var db = NewDb();
        var countryId = await SeedCountryAsync(db);
        var service = NewService(db);

        var dto = await service.CreateAsync(new CreateCityRequest("Sarajevo", countryId));

        Assert.True(dto.Id > 0);
        Assert.Equal("Sarajevo", dto.Name);
        Assert.Equal(countryId, dto.CountryId);
        Assert.Equal("Bosnia", dto.CountryName); // joined from Country, no N+1
        Assert.Equal(1, await db.Cities.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_missing_country_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CreateAsync(new CreateCityRequest("Sarajevo", 999)));
    }

    [Fact]
    public async Task CreateAsync_duplicate_name_throws_Conflict()
    {
        await using var db = NewDb();
        var countryId = await SeedCountryAsync(db);
        var service = NewService(db);
        await service.CreateAsync(new CreateCityRequest("Sarajevo", countryId));

        // Same name (any casing) → still a conflict.
        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(new CreateCityRequest("sarajevo", countryId)));
    }

    [Fact]
    public async Task GetPagedAsync_search_filters_by_name_case_insensitively()
    {
        await using var db = NewDb();
        var countryId = await SeedCountryAsync(db);
        db.Cities.AddRange(
            new City { Name = "Mostar", CountryId = countryId },
            new City { Name = "Tuzla", CountryId = countryId },
            new City { Name = "Zenica", CountryId = countryId });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), "mos");

        var only = Assert.Single(page.Items);
        Assert.Equal("Mostar", only.Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_court_references_the_city()
    {
        await using var db = NewDb();
        var countryId = await SeedCountryAsync(db);
        var city = new City { Name = "Sarajevo", CountryId = countryId };
        db.Cities.Add(city);
        await db.SaveChangesAsync();
        db.Courts.Add(new Court
        {
            Name = "Center Court",
            CityId = city.Id,
            SurfaceTypeId = 1,
            CourtTypeId = 1,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(city.Id));
        Assert.Contains("Sarajevo", ex.Message);     // reason names the city
        Assert.Contains("1 courts", ex.Message);      // and the referencing count
        Assert.Equal(1, await db.Cities.CountAsync()); // not deleted
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }
}
