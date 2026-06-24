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
/// Reference-data CRUD for <c>City</c> (feature 9). Reads are <c>AsNoTracking</c> + projected to
/// <see cref="CityDto"/> (never entities) and always paged; <c>CountryName</c> is resolved by projecting the
/// <c>Country</c> nav, which EF translates to a JOIN (no extra query / N+1). The name search runs at the
/// database (case-insensitive, no load-all-then-filter). Writes verify the referenced <c>Country</c> exists,
/// enforce Name uniqueness with a service-level <c>AnyAsync</c> check (no new DB index), and block deletes that
/// would orphan courts. Mirrors the golden <c>CountryService</c> without the cached lookup (Country-only).
/// </summary>
public sealed class CityService : ICityService
{
    private readonly CourtlyDbContext _db;
    private readonly ILogger<CityService> _logger;

    public CityService(CourtlyDbContext db, ILogger<CityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<CityDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default)
    {
        var query = _db.Cities.AsNoTracking();

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
            .Select(c => new CityDto(c.Id, c.Name, c.CountryId, c.Country.Name)) // nav -> JOIN, no N+1
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<CityDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.Cities.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CityDto(c.Id, c.Name, c.CountryId, c.Country.Name)) // nav -> JOIN, no N+1
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException($"City {id} was not found.");
    }

    public async Task<CityDto> CreateAsync(CreateCityRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        await EnsureCountryExistsAsync(request.CountryId, ct);
        await EnsureUniqueAsync(name, request.CountryId, excludeId: null, ct);

        var entity = new Domain.Entities.City { Name = name, CountryId = request.CountryId };
        _db.Cities.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created city {CityId} in country {CountryId}.", entity.Id, entity.CountryId);

        return await ProjectAsync(entity.Id, ct);
    }

    public async Task<CityDto> UpdateAsync(long id, UpdateCityRequest request, CancellationToken ct = default)
    {
        var entity = await _db.Cities.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"City {id} was not found.");

        var name = request.Name.Trim();

        await EnsureCountryExistsAsync(request.CountryId, ct);
        await EnsureUniqueAsync(name, request.CountryId, excludeId: id, ct);

        entity.Name = name;
        entity.CountryId = request.CountryId;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated city {CityId} in country {CountryId}.", entity.Id, entity.CountryId);

        return await ProjectAsync(entity.Id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.Cities.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"City {id} was not found.");

        // Delete-restrict: refuse while courts still reference this city (rubric: BusinessException with a
        // human reason, not a raw FK violation).
        var courtCount = await _db.Courts.CountAsync(c => c.CityId == id, ct);
        if (courtCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete city '{entity.Name}' because {courtCount} courts reference it.");
        }

        _db.Cities.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted city {CityId}.", id);
    }

    /// <summary>Re-reads the row with <c>CountryName</c> projected (single JOIN query) so writes return the same
    /// shape as the read path without a second round-trip per field.</summary>
    private async Task<CityDto> ProjectAsync(long id, CancellationToken ct)
    {
        return await _db.Cities.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CityDto(c.Id, c.Name, c.CountryId, c.Country.Name))
            .FirstAsync(ct);
    }

    /// <summary>Validates the FK before insert/update so a bad CountryId is a clean 404, not an FK violation.</summary>
    private async Task EnsureCountryExistsAsync(long countryId, CancellationToken ct)
    {
        if (!await _db.Countries.AnyAsync(c => c.Id == countryId, ct))
        {
            throw new NotFoundException($"Country {countryId} not found.");
        }
    }

    /// <summary>Service-level duplicate guard (no DB index dependency): rejects a Name (case-insensitive) that
    /// already belongs to a <em>different</em> city <em>in the same country</em>. City names are unique per
    /// country, not globally (two countries may both have e.g. "Tripoli").</summary>
    private async Task EnsureUniqueAsync(string name, long countryId, long? excludeId, CancellationToken ct)
    {
        var lowerName = name.ToLower();
        // excludeId is null on create (compare against every row in the country) and the current id on update
        // (skip self). Written as `!excludeId.HasValue || c.Id != excludeId.Value` so create isn't a NULL compare.
        if (await _db.Cities.AnyAsync(
                c => c.CountryId == countryId
                     && (!excludeId.HasValue || c.Id != excludeId.Value)
                     && c.Name.ToLower() == lowerName, ct))
        {
            throw new ConflictException($"A city named '{name}' already exists in the selected country.");
        }
    }
}
