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
/// Feature 9 DoD for the SurfaceType slice: create returns a DTO, duplicate Name is a conflict, the name search
/// filters at query time, and delete is restricted while courts reference the surface type. Mirrors the golden
/// Country in-memory DbContext harness (EF InMemory provider, fresh DB per test). The search test relies on
/// <see cref="SurfaceTypeService"/>'s provider guard — under InMemory it uses a lowered <c>Contains</c> instead
/// of the Postgres-only <c>EF.Functions.ILike</c>, so the same test exercises case-insensitive filtering here.
/// </summary>
public class SurfaceTypeServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"surface-types-{Guid.NewGuid()}")
            .Options);

    private static SurfaceTypeService NewService(CourtlyDbContext db) =>
        new(db, NullLogger<SurfaceTypeService>.Instance);

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var dto = await service.CreateAsync(new CreateSurfaceTypeRequest("Clay", "Crushed brick surface."));

        Assert.True(dto.Id > 0);
        Assert.Equal("Clay", dto.Name);
        Assert.Equal("Crushed brick surface.", dto.Description);
        Assert.Equal(1, await db.SurfaceTypes.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_duplicate_name_throws_Conflict()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await service.CreateAsync(new CreateSurfaceTypeRequest("Clay", null));

        // Same name (any casing) → still a conflict.
        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(new CreateSurfaceTypeRequest("clay", "Different description")));
    }

    [Fact]
    public async Task GetPagedAsync_search_filters_by_name_case_insensitively()
    {
        await using var db = NewDb();
        db.SurfaceTypes.AddRange(
            new SurfaceType { Name = "Clay" },
            new SurfaceType { Name = "Grass" },
            new SurfaceType { Name = "Hard" });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), "cla");

        var only = Assert.Single(page.Items);
        Assert.Equal("Clay", only.Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_court_references_the_surface_type()
    {
        await using var db = NewDb();
        var surfaceType = new SurfaceType { Name = "Clay" };
        db.SurfaceTypes.Add(surfaceType);
        await db.SaveChangesAsync();
        db.Courts.Add(new Court { Name = "Center Court", CityId = 1, SurfaceTypeId = surfaceType.Id, CourtTypeId = 1 });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(surfaceType.Id));
        Assert.Contains("Clay", ex.Message);                 // reason names the surface type
        Assert.Contains("1 courts", ex.Message);             // and the referencing count
        Assert.Equal(1, await db.SurfaceTypes.CountAsync()); // not deleted
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }
}
