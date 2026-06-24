using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Reference;

/// <summary>
/// Reference-data CRUD for <c>SurfaceType</c> (feature 9). Reads are <c>AsNoTracking</c> + projected to
/// <see cref="SurfaceTypeDto"/> (never entities) and always paged. The name search runs at the database
/// (case-insensitive, no load-all-then-filter). Writes trim Name, enforce Name uniqueness with a service-level
/// <c>AnyAsync</c> check (no new DB index), and block deletes that would orphan courts. Mirrors the golden
/// <see cref="CountryService"/> minus the cached dropdown lookup (surface types are not cached).
/// </summary>
public sealed class SurfaceTypeService : ISurfaceTypeService
{
    private readonly CourtlyDbContext _db;
    private readonly ILogger<SurfaceTypeService> _logger;

    public SurfaceTypeService(CourtlyDbContext db, ILogger<SurfaceTypeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<SurfaceTypeDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default)
    {
        var query = _db.SurfaceTypes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // ILike filters case-insensitively at the database (Postgres). The InMemory test provider does not
            // translate ILike, so fall back to a lowered Contains there — same case-insensitive semantics.
            query = _db.Database.IsNpgsql()
                ? query.Where(s => EF.Functions.ILike(s.Name, $"%{term}%"))
                : query.Where(s => s.Name.ToLower().Contains(term.ToLower()));
        }

        return await query
            .OrderByDescending(s => s.Id) // newest-first
            .Select(s => new SurfaceTypeDto(s.Id, s.Name, s.Description))
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<SurfaceTypeDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.SurfaceTypes.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SurfaceTypeDto(s.Id, s.Name, s.Description))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException($"SurfaceType {id} was not found.");
    }

    public async Task<SurfaceTypeDto> CreateAsync(CreateSurfaceTypeRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await EnsureUniqueAsync(name, excludeId: null, ct);

        var entity = new Domain.Entities.SurfaceType { Name = name, Description = description };
        _db.SurfaceTypes.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created surface type {SurfaceTypeId} ({Name}).", entity.Id, entity.Name);

        return new SurfaceTypeDto(entity.Id, entity.Name, entity.Description);
    }

    public async Task<SurfaceTypeDto> UpdateAsync(long id, UpdateSurfaceTypeRequest request, CancellationToken ct = default)
    {
        var entity = await _db.SurfaceTypes.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException($"SurfaceType {id} was not found.");

        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await EnsureUniqueAsync(name, excludeId: id, ct);

        entity.Name = name;
        entity.Description = description;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated surface type {SurfaceTypeId} ({Name}).", entity.Id, entity.Name);

        return new SurfaceTypeDto(entity.Id, entity.Name, entity.Description);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.SurfaceTypes.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException($"SurfaceType {id} was not found.");

        // Delete-restrict: refuse while courts still reference this surface type (rubric: BusinessException with a
        // human reason, not a raw FK violation).
        var courtCount = await _db.Courts.CountAsync(c => c.SurfaceTypeId == id, ct);
        if (courtCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete surface type '{entity.Name}' because {courtCount} courts reference it.");
        }

        _db.SurfaceTypes.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted surface type {SurfaceTypeId} ({Name}).", id, entity.Name);
    }

    /// <summary>Service-level duplicate guard (no DB index dependency): rejects a Name (case-insensitive) that
    /// already belongs to a <em>different</em> row.</summary>
    private async Task EnsureUniqueAsync(string name, long? excludeId, CancellationToken ct)
    {
        var lowerName = name.ToLower();
        // excludeId is null on create (compare against every row) and the current id on update (skip self).
        // Written as `!excludeId.HasValue || s.Id != excludeId` so the create case isn't a NULL comparison.
        if (await _db.SurfaceTypes.AnyAsync(
                s => (!excludeId.HasValue || s.Id != excludeId.Value) && s.Name.ToLower() == lowerName, ct))
        {
            throw new ConflictException($"A surface type named '{name}' already exists.");
        }
    }
}
