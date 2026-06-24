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
/// Reference-data CRUD for <c>Amenity</c> (feature 9). Reads are <c>AsNoTracking</c> + projected to
/// <see cref="AmenityDto"/> (never entities) and always paged. The name search runs at the database
/// (case-insensitive, no load-all-then-filter). Writes enforce Name uniqueness with a service-level
/// <c>AnyAsync</c> check (no new DB index) and block deletes that would orphan court-amenities. Mirrors the
/// golden <c>CountryService</c>; amenities have no FK dropdown and no lookup cache.
/// </summary>
public sealed class AmenityService : IAmenityService
{
    private readonly CourtlyDbContext _db;
    private readonly ILogger<AmenityService> _logger;

    public AmenityService(CourtlyDbContext db, ILogger<AmenityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<AmenityDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default)
    {
        var query = _db.Amenities.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // ILike filters case-insensitively at the database (Postgres). The InMemory test provider does not
            // translate ILike, so fall back to a lowered Contains there — same case-insensitive semantics.
            query = _db.Database.IsNpgsql()
                ? query.Where(a => EF.Functions.ILike(a.Name, $"%{term}%"))
                : query.Where(a => a.Name.ToLower().Contains(term.ToLower()));
        }

        return await query
            .OrderByDescending(a => a.Id) // newest-first
            .Select(a => new AmenityDto(a.Id, a.Name, a.IconKey))
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<AmenityDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.Amenities.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AmenityDto(a.Id, a.Name, a.IconKey))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException($"Amenity {id} was not found.");
    }

    public async Task<AmenityDto> CreateAsync(CreateAmenityRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var iconKey = request.IconKey?.Trim();

        await EnsureUniqueAsync(name, excludeId: null, ct);

        var entity = new Domain.Entities.Amenity { Name = name, IconKey = iconKey };
        _db.Amenities.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created amenity {AmenityId} ({Name}).", entity.Id, entity.Name);

        return new AmenityDto(entity.Id, entity.Name, entity.IconKey);
    }

    public async Task<AmenityDto> UpdateAsync(long id, UpdateAmenityRequest request, CancellationToken ct = default)
    {
        var entity = await _db.Amenities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException($"Amenity {id} was not found.");

        var name = request.Name.Trim();
        var iconKey = request.IconKey?.Trim();

        await EnsureUniqueAsync(name, excludeId: id, ct);

        entity.Name = name;
        entity.IconKey = iconKey;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated amenity {AmenityId} ({Name}).", entity.Id, entity.Name);

        return new AmenityDto(entity.Id, entity.Name, entity.IconKey);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.Amenities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException($"Amenity {id} was not found.");

        // Delete-restrict: refuse while court-amenities still reference this amenity (rubric: BusinessException
        // with a human reason, not a raw FK violation).
        var courtAmenityCount = await _db.CourtAmenities.CountAsync(ca => ca.AmenityId == id, ct);
        if (courtAmenityCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete amenity '{entity.Name}' because {courtAmenityCount} court-amenities reference it.");
        }

        _db.Amenities.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted amenity {AmenityId} ({Name}).", id, entity.Name);
    }

    /// <summary>Service-level duplicate guard (no DB index dependency): rejects a Name (case-insensitive) that
    /// already belongs to a <em>different</em> row.</summary>
    private async Task EnsureUniqueAsync(string name, long? excludeId, CancellationToken ct)
    {
        var lowerName = name.ToLower();
        // excludeId is null on create (compare against every row) and the current id on update (skip self).
        // Written as `!excludeId.HasValue || a.Id != excludeId` so the create case isn't a NULL comparison.
        if (await _db.Amenities.AnyAsync(
                a => (!excludeId.HasValue || a.Id != excludeId.Value) && a.Name.ToLower() == lowerName, ct))
        {
            throw new ConflictException($"An amenity named '{name}' already exists.");
        }
    }
}
