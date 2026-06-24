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
/// Reference-data CRUD for <c>CourtType</c> (feature 9). Mirrors the golden <see cref="CountryService"/>: reads
/// are <c>AsNoTracking</c> + projected to <see cref="CourtTypeDto"/> (never entities) and always paged; the name
/// search runs at the database (case-insensitive, no load-all-then-filter). Writes enforce Name uniqueness with a
/// service-level <c>AnyAsync</c> check (no new DB index) and block deletes that would orphan courts. CourtType has
/// no lookup cache and no FK dropdown, so it does not inject <c>IMemoryCache</c>.
/// </summary>
public sealed class CourtTypeService : ICourtTypeService
{
    private readonly CourtlyDbContext _db;
    private readonly ILogger<CourtTypeService> _logger;

    public CourtTypeService(CourtlyDbContext db, ILogger<CourtTypeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<CourtTypeDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default)
    {
        var query = _db.CourtTypes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // ILike filters case-insensitively at the database (Postgres). The InMemory test provider does not
            // translate ILike, so fall back to a lowered Contains there — same case-insensitive semantics.
            query = _db.Database.IsNpgsql()
                ? query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%"))
                : query.Where(c => c.Name.ToLower().Contains(term.ToLower()));
        }

        return await query
            .OrderByDescending(c => c.Id) // newest-first
            .Select(c => new CourtTypeDto(c.Id, c.Name, c.Description))
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<CourtTypeDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.CourtTypes.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CourtTypeDto(c.Id, c.Name, c.Description))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException($"CourtType {id} was not found.");
    }

    public async Task<CourtTypeDto> CreateAsync(CreateCourtTypeRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var description = request.Description?.Trim();

        await EnsureUniqueAsync(name, excludeId: null, ct);

        var entity = new Domain.Entities.CourtType { Name = name, Description = description };
        _db.CourtTypes.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created court type {CourtTypeId} ({Name}).", entity.Id, entity.Name);

        return new CourtTypeDto(entity.Id, entity.Name, entity.Description);
    }

    public async Task<CourtTypeDto> UpdateAsync(long id, UpdateCourtTypeRequest request, CancellationToken ct = default)
    {
        var entity = await _db.CourtTypes.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"CourtType {id} was not found.");

        var name = request.Name.Trim();
        var description = request.Description?.Trim();

        await EnsureUniqueAsync(name, excludeId: id, ct);

        entity.Name = name;
        entity.Description = description;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated court type {CourtTypeId} ({Name}).", entity.Id, entity.Name);

        return new CourtTypeDto(entity.Id, entity.Name, entity.Description);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.CourtTypes.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"CourtType {id} was not found.");

        // Delete-restrict: refuse while courts still reference this court type (rubric: BusinessException with a
        // human reason, not a raw FK violation).
        var courtCount = await _db.Courts.CountAsync(c => c.CourtTypeId == id, ct);
        if (courtCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete court type '{entity.Name}' because {courtCount} courts reference it.");
        }

        _db.CourtTypes.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted court type {CourtTypeId} ({Name}).", id, entity.Name);
    }

    /// <summary>Service-level duplicate guard (no DB index dependency): rejects a Name (case-insensitive) that
    /// already belongs to a <em>different</em> row.</summary>
    private async Task EnsureUniqueAsync(string name, long? excludeId, CancellationToken ct)
    {
        var lowerName = name.ToLower();
        // excludeId is null on create (compare against every row) and the current id on update (skip self).
        // Written as `!excludeId.HasValue || c.Id != excludeId` so the create case isn't a NULL comparison.
        if (await _db.CourtTypes.AnyAsync(
                c => (!excludeId.HasValue || c.Id != excludeId.Value) && c.Name.ToLower() == lowerName, ct))
        {
            throw new ConflictException($"A court type named '{name}' already exists.");
        }
    }
}
