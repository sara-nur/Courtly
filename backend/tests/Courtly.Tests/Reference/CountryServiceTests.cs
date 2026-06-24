using Courtly.Application.Common.Exceptions;
using Courtly.Application.Reference;
using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Reference;

/// <summary>
/// Feature 9 DoD for the golden Country slice: create returns a DTO, duplicate IsoCode is a conflict, the
/// name search filters at query time, and delete is restricted while cities reference the country. Mirrors the
/// existing in-memory DbContext harness (EF InMemory provider, fresh DB per test). The search test relies on
/// <see cref="CountryService"/>'s provider guard — under InMemory it uses a lowered <c>Contains</c> instead of
/// the Postgres-only <c>EF.Functions.ILike</c>, so the same test exercises case-insensitive filtering here.
/// </summary>
public class CountryServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"countries-{Guid.NewGuid()}")
            .Options);

    private static CountryService NewService(CourtlyDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CountryService>.Instance);

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto_with_uppercased_iso()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var dto = await service.CreateAsync(new CreateCountryRequest("Bosnia and Herzegovina", "bih"));

        Assert.True(dto.Id > 0);
        Assert.Equal("Bosnia and Herzegovina", dto.Name);
        Assert.Equal("BIH", dto.IsoCode); // normalized to upper-case
        Assert.Equal(1, await db.Countries.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_duplicate_iso_code_throws_Conflict()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await service.CreateAsync(new CreateCountryRequest("Bosnia", "BIH"));

        // Same ISO (any casing), different name → still a conflict.
        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(new CreateCountryRequest("Bosnia Duplicate", "bih")));
    }

    [Fact]
    public async Task GetPagedAsync_search_filters_by_name_case_insensitively()
    {
        await using var db = NewDb();
        db.Countries.AddRange(
            new Country { Name = "Croatia", IsoCode = "HRV" },
            new Country { Name = "Serbia", IsoCode = "SRB" },
            new Country { Name = "Slovenia", IsoCode = "SVN" });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), "cro");

        var only = Assert.Single(page.Items);
        Assert.Equal("Croatia", only.Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_city_references_the_country()
    {
        await using var db = NewDb();
        var country = new Country { Name = "Bosnia", IsoCode = "BIH" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        db.Cities.Add(new City { Name = "Sarajevo", CountryId = country.Id });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(country.Id));
        Assert.Contains("Bosnia", ex.Message);          // reason names the country
        Assert.Contains("1 cities", ex.Message);        // and the referencing count
        Assert.Equal(1, await db.Countries.CountAsync()); // not deleted
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }
}
