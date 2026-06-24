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
/// Feature 9 DoD for the CourtType slice: create returns a DTO, a duplicate Name is a conflict, the name search
/// filters at query time, and delete is restricted while courts reference the court type. Mirrors the golden
/// CountryService harness (EF InMemory provider, fresh DB per test). The search test relies on
/// <see cref="CourtTypeService"/>'s provider guard — under InMemory it uses a lowered <c>Contains</c> instead of
/// the Postgres-only <c>EF.Functions.ILike</c>, so the same test exercises case-insensitive filtering here.
/// </summary>
public class CourtTypeServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"court-types-{Guid.NewGuid()}")
            .Options);

    private static CourtTypeService NewService(CourtlyDbContext db) =>
        new(db, NullLogger<CourtTypeService>.Instance);

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var dto = await service.CreateAsync(new CreateCourtTypeRequest("Indoor", "Climate-controlled indoor court."));

        Assert.True(dto.Id > 0);
        Assert.Equal("Indoor", dto.Name);
        Assert.Equal("Climate-controlled indoor court.", dto.Description);
        Assert.Equal(1, await db.CourtTypes.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_duplicate_name_throws_Conflict()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await service.CreateAsync(new CreateCourtTypeRequest("Indoor", null));

        // Same name (any casing) → still a conflict.
        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(new CreateCourtTypeRequest("indoor", "Different description")));
    }

    [Fact]
    public async Task GetPagedAsync_search_filters_by_name_case_insensitively()
    {
        await using var db = NewDb();
        db.CourtTypes.AddRange(
            new CourtType { Name = "Indoor" },
            new CourtType { Name = "Outdoor" },
            new CourtType { Name = "Padel" });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), "indo");

        var only = Assert.Single(page.Items);
        Assert.Equal("Indoor", only.Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_court_references_the_court_type()
    {
        await using var db = NewDb();
        var courtType = new CourtType { Name = "Indoor" };
        db.CourtTypes.Add(courtType);
        await db.SaveChangesAsync();
        db.Courts.Add(new Court { CourtTypeId = courtType.Id });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(courtType.Id));
        Assert.Contains("Indoor", ex.Message);             // reason names the court type
        Assert.Contains("1 courts", ex.Message);           // and the referencing count
        Assert.Equal(1, await db.CourtTypes.CountAsync()); // not deleted
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }
}
