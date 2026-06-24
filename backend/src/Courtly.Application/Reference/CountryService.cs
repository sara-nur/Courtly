using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Reference;

/// <summary>
/// Reference-data CRUD for <c>Country</c> (feature 9). Reads are <c>AsNoTracking</c> + projected to
/// <see cref="CountryDto"/> (never entities) and always paged. The name search runs at the database
/// (case-insensitive, no load-all-then-filter). Writes normalize the ISO code to upper-case, enforce
/// Name/IsoCode uniqueness with a service-level <c>AnyAsync</c> check (no new DB index), block deletes that
/// would orphan cities, and invalidate the lookup cache. This is the golden template the other four
/// reference entities (City / SurfaceType / CourtType / Amenity) mirror.
/// </summary>
public sealed class CountryService : ICountryService
{
    /// <summary>Memory-cache key for the full name-ordered lookup list.</summary>
    private const string LookupCacheKey = "ref:countries:lookup";

    /// <summary>Short TTL — the list is tiny and writes invalidate it explicitly, so staleness is bounded.</summary>
    private static readonly TimeSpan LookupCacheTtl = TimeSpan.FromMinutes(5);

    private readonly CourtlyDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CountryService> _logger;

    public CountryService(CourtlyDbContext db, IMemoryCache cache, ILogger<CountryService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PagedResult<CountryDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default)
    {
        var query = _db.Countries.AsNoTracking();

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
            .Select(c => new CountryDto(c.Id, c.Name, c.IsoCode))
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<CountryDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.Countries.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CountryDto(c.Id, c.Name, c.IsoCode))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException($"Country {id} was not found.");
    }

    public async Task<IReadOnlyList<CountryDto>> GetLookupAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(LookupCacheKey, out IReadOnlyList<CountryDto>? cached) && cached is not null)
        {
            return cached;
        }

        var list = await _db.Countries.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto(c.Id, c.Name, c.IsoCode))
            .ToListAsync(ct);

        _cache.Set(LookupCacheKey, (IReadOnlyList<CountryDto>)list, LookupCacheTtl);
        return list;
    }

    public async Task<CountryDto> CreateAsync(CreateCountryRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var isoCode = request.IsoCode.Trim().ToUpperInvariant();

        await EnsureUniqueAsync(name, isoCode, excludeId: null, ct);

        var entity = new Domain.Entities.Country { Name = name, IsoCode = isoCode };
        _db.Countries.Add(entity);
        await _db.SaveChangesAsync(ct);

        InvalidateLookup();
        _logger.LogInformation("Created country {CountryId} ({IsoCode}).", entity.Id, entity.IsoCode);

        return new CountryDto(entity.Id, entity.Name, entity.IsoCode);
    }

    public async Task<CountryDto> UpdateAsync(long id, UpdateCountryRequest request, CancellationToken ct = default)
    {
        var entity = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Country {id} was not found.");

        var name = request.Name.Trim();
        var isoCode = request.IsoCode.Trim().ToUpperInvariant();

        await EnsureUniqueAsync(name, isoCode, excludeId: id, ct);

        entity.Name = name;
        entity.IsoCode = isoCode;
        await _db.SaveChangesAsync(ct);

        InvalidateLookup();
        _logger.LogInformation("Updated country {CountryId} ({IsoCode}).", entity.Id, entity.IsoCode);

        return new CountryDto(entity.Id, entity.Name, entity.IsoCode);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Country {id} was not found.");

        // Delete-restrict: refuse while cities still reference this country (rubric: BusinessException with a
        // human reason, not a raw FK violation).
        var cityCount = await _db.Cities.CountAsync(c => c.CountryId == id, ct);
        if (cityCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete country '{entity.Name}' because {cityCount} cities reference it.");
        }

        _db.Countries.Remove(entity);
        await _db.SaveChangesAsync(ct);

        InvalidateLookup();
        _logger.LogInformation("Deleted country {CountryId} ({IsoCode}).", id, entity.IsoCode);
    }

    /// <summary>Service-level duplicate guard (no DB index dependency): rejects a Name (case-insensitive) or
    /// IsoCode that already belongs to a <em>different</em> row.</summary>
    private async Task EnsureUniqueAsync(string name, string isoCode, long? excludeId, CancellationToken ct)
    {
        var lowerName = name.ToLower();
        // excludeId is null on create (compare against every row) and the current id on update (skip self).
        // Written as `!excludeId.HasValue || c.Id != excludeId` so the create case isn't a NULL comparison.
        if (await _db.Countries.AnyAsync(
                c => (!excludeId.HasValue || c.Id != excludeId.Value) && c.Name.ToLower() == lowerName, ct))
        {
            throw new ConflictException($"A country named '{name}' already exists.");
        }

        if (await _db.Countries.AnyAsync(
                c => (!excludeId.HasValue || c.Id != excludeId.Value) && c.IsoCode == isoCode, ct))
        {
            throw new ConflictException($"A country with ISO code '{isoCode}' already exists.");
        }
    }

    private void InvalidateLookup() => _cache.Remove(LookupCacheKey);
}
