using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Courts;

/// <summary>
/// Court-catalog CRUD (feature 10). Reads are <c>AsNoTracking</c> + projected to <see cref="CourtDto"/> (never
/// entities) and always paged; the nav names (City/Country/SurfaceType/CourtType) are resolved by projecting the
/// navs, which EF translates to JOINs (no extra query / N+1). EVERY list filter — name search, city, country
/// (through <c>City.CountryId</c>), surface type, court type, indoor, active, featured, price range — runs at the
/// database (Where clause), never in memory. The paged list is intentionally NOT cached: it is fully dynamic
/// (filterable on ten dimensions). Writes verify the referenced City/SurfaceType/CourtType exist (clean 404, not a
/// raw FK violation), trim Name/Description, and block deletes that would orphan reservations or time slots.
/// Mirrors the feature 9 <c>CityService</c>. <see cref="CourtDto.PrimaryImageUrl"/> is left <c>null</c> in F10
/// (images are <c>bytea</c> with no serving endpoint yet — F11).
/// </summary>
public sealed class CourtService : ICourtService
{
    // Court images are stored as bytea with no public serving endpoint in F10; the DTO field stays null until F11
    // adds a GET .../images/{id} endpoint. We never base64 image bytes into the list payload.
    private const string? PrimaryImageUrl = null;

    private readonly CourtlyDbContext _db;
    private readonly ILogger<CourtService> _logger;

    public CourtService(CourtlyDbContext db, ILogger<CourtService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<CourtDto>> GetPagedAsync(
        PaginationQuery pagination, CourtListQuery filter, CancellationToken ct = default)
    {
        var query = _db.Courts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            // ILike filters case-insensitively at the database (Postgres). The InMemory test provider does not
            // translate ILike, so fall back to a lowered Contains there — same case-insensitive semantics.
            query = _db.Database.IsNpgsql()
                ? query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%"))
                : query.Where(c => c.Name.ToLower().Contains(term.ToLower()));
        }

        // Every remaining filter is applied at the DB (Where clause), never in memory.
        if (filter.CityId.HasValue)
        {
            query = query.Where(c => c.CityId == filter.CityId.Value);
        }

        if (filter.CountryId.HasValue)
        {
            query = query.Where(c => c.City.CountryId == filter.CountryId.Value);
        }

        if (filter.SurfaceTypeId.HasValue)
        {
            query = query.Where(c => c.SurfaceTypeId == filter.SurfaceTypeId.Value);
        }

        if (filter.CourtTypeId.HasValue)
        {
            query = query.Where(c => c.CourtTypeId == filter.CourtTypeId.Value);
        }

        if (filter.IsIndoor.HasValue)
        {
            query = query.Where(c => c.IsIndoor == filter.IsIndoor.Value);
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(c => c.IsActive == filter.IsActive.Value);
        }

        if (filter.IsFeatured.HasValue)
        {
            query = query.Where(c => c.IsFeatured == filter.IsFeatured.Value);
        }

        if (filter.MinPrice.HasValue)
        {
            query = query.Where(c => c.HourlyPrice >= filter.MinPrice.Value);
        }

        if (filter.MaxPrice.HasValue)
        {
            query = query.Where(c => c.HourlyPrice <= filter.MaxPrice.Value);
        }

        return await Project(query.OrderByDescending(c => c.Id)) // newest-first; navs -> JOINs, no N+1
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<CourtDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await Project(_db.Courts.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException($"Court {id} was not found.");
    }

    public async Task<CourtDto> CreateAsync(CreateCourtRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await EnsureCityExistsAsync(request.CityId, ct);
        await EnsureSurfaceTypeExistsAsync(request.SurfaceTypeId, ct);
        await EnsureCourtTypeExistsAsync(request.CourtTypeId, ct);

        var entity = new Domain.Entities.Court
        {
            Name = name,
            Description = description,
            CityId = request.CityId,
            SurfaceTypeId = request.SurfaceTypeId,
            CourtTypeId = request.CourtTypeId,
            IsIndoor = request.IsIndoor,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
            HourlyPrice = request.HourlyPrice,
        };
        _db.Courts.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created court {CourtId} ({Name}).", entity.Id, entity.Name);

        return await ProjectAsync(entity.Id, ct);
    }

    public async Task<CourtDto> UpdateAsync(long id, UpdateCourtRequest request, CancellationToken ct = default)
    {
        var entity = await _db.Courts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Court {id} was not found.");

        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await EnsureCityExistsAsync(request.CityId, ct);
        await EnsureSurfaceTypeExistsAsync(request.SurfaceTypeId, ct);
        await EnsureCourtTypeExistsAsync(request.CourtTypeId, ct);

        entity.Name = name;
        entity.Description = description;
        entity.CityId = request.CityId;
        entity.SurfaceTypeId = request.SurfaceTypeId;
        entity.CourtTypeId = request.CourtTypeId;
        entity.IsIndoor = request.IsIndoor;
        entity.IsActive = request.IsActive;
        entity.IsFeatured = request.IsFeatured;
        entity.HourlyPrice = request.HourlyPrice;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated court {CourtId} ({Name}).", entity.Id, entity.Name);

        return await ProjectAsync(entity.Id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.Courts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Court {id} was not found.");

        // Delete-restrict: reservations are an FK Restrict on the court — a raw delete would throw an FK violation,
        // so refuse with a human reason (rubric: BusinessException, not a raw FK error).
        var reservationCount = await _db.Reservations.CountAsync(r => r.CourtId == id, ct);
        if (reservationCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete court '{entity.Name}' because {reservationCount} reservations reference it.");
        }

        // Time slots cascade at the DB level, but deleting a court with slots silently destroys booking history.
        // Treat them as a blocking reference too — refuse and surface the reason rather than cascade-deleting.
        var timeSlotCount = await _db.TimeSlots.CountAsync(t => t.CourtId == id, ct);
        if (timeSlotCount > 0)
        {
            throw new BusinessException(
                $"Cannot delete court '{entity.Name}' because it has {timeSlotCount} time slots.");
        }

        _db.Courts.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted court {CourtId} ({Name}).", id, entity.Name);
    }

    /// <summary>The single nav-JOIN projection shared by the read paths and the post-write re-read, so writes
    /// return the same shape as reads without a second round-trip per field. The filter is applied to the
    /// <see cref="Domain.Entities.Court"/> query BEFORE this projection so the predicate translates server-side —
    /// a <c>Where</c> applied AFTER the DTO projection cannot be translated by the Npgsql provider.</summary>
    private static IQueryable<CourtDto> Project(IQueryable<Domain.Entities.Court> source)
    {
        return source.Select(c => new CourtDto(
            c.Id,
            c.Name,
            c.Description,
            c.CityId,
            c.City.Name,
            c.City.CountryId,
            c.City.Country.Name,
            c.SurfaceTypeId,
            c.SurfaceType.Name,
            c.CourtTypeId,
            c.CourtType.Name,
            c.IsIndoor,
            c.IsActive,
            c.IsFeatured,
            c.HourlyPrice,
            PrimaryImageUrl));
    }

    /// <summary>Re-reads the row with the nav names projected (single JOIN query) so writes return the same shape
    /// as the read path.</summary>
    private async Task<CourtDto> ProjectAsync(long id, CancellationToken ct)
    {
        return await Project(_db.Courts.AsNoTracking().Where(c => c.Id == id)).FirstAsync(ct);
    }

    /// <summary>Validates the City FK before insert/update so a bad CityId is a clean 404, not an FK violation.</summary>
    private async Task EnsureCityExistsAsync(long cityId, CancellationToken ct)
    {
        if (!await _db.Cities.AnyAsync(c => c.Id == cityId, ct))
        {
            throw new NotFoundException($"City {cityId} not found.");
        }
    }

    /// <summary>Validates the SurfaceType FK before insert/update (clean 404, not an FK violation).</summary>
    private async Task EnsureSurfaceTypeExistsAsync(long surfaceTypeId, CancellationToken ct)
    {
        if (!await _db.SurfaceTypes.AnyAsync(s => s.Id == surfaceTypeId, ct))
        {
            throw new NotFoundException($"SurfaceType {surfaceTypeId} not found.");
        }
    }

    /// <summary>Validates the CourtType FK before insert/update (clean 404, not an FK violation).</summary>
    private async Task EnsureCourtTypeExistsAsync(long courtTypeId, CancellationToken ct)
    {
        if (!await _db.CourtTypes.AnyAsync(t => t.Id == courtTypeId, ct))
        {
            throw new NotFoundException($"CourtType {courtTypeId} not found.");
        }
    }
}
