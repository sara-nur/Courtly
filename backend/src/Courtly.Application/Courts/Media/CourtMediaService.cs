using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Court;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Courts.Media;

/// <summary>
/// Court media + amenities (feature 11). Reads are <c>AsNoTracking</c> and projected to DTOs (never entities);
/// writes fetch the tracked rows, mutate, and <c>SaveChanges</c>. Upload bytes are validated by the pure
/// <see cref="ImageContentValidator"/> before they touch the DB. The image <c>Url</c> (<c>/api/images/{id}</c>) is
/// built IN MEMORY after materialization — never inside the SQL projection — so the read stays provider-agnostic
/// (Npgsql in prod, EF InMemory in tests). The service owns two invariants: a court with images has exactly one
/// primary (set/add/delete all re-balance it), and an amenity set is REPLACED wholesale on write.
/// </summary>
public sealed class CourtMediaService : ICourtMediaService
{
    private readonly CourtlyDbContext _db;
    private readonly ILogger<CourtMediaService> _logger;

    public CourtMediaService(CourtlyDbContext db, ILogger<CourtMediaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CourtImageDto>> GetImagesAsync(long courtId, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        // Project to scalars (never the bytea blob) ordered primary-first; build the relative Url in memory.
        var rows = await _db.CourtImages.AsNoTracking()
            .Where(i => i.CourtId == courtId)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.Id)
            .Select(i => new ImageRow(i.Id, i.CourtId, i.IsPrimary, i.Caption))
            .ToListAsync(ct);

        return rows.Select(ToImageDto).ToList();
    }

    public async Task<CourtImageDto> AddImageAsync(
        long courtId, byte[] bytes, string? declaredContentType, string? caption, bool isPrimary,
        CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        var contentType = ImageContentValidator.Validate(declaredContentType, bytes);

        var hasExisting = await _db.CourtImages.AnyAsync(i => i.CourtId == courtId, ct);
        var makePrimary = isPrimary || !hasExisting;

        if (makePrimary && hasExisting)
        {
            // New image is taking over as primary — unset the current primary(s) by id only, never loading the
            // 5 MB Bytes blob just to flip a flag.
            var currentPrimaryIds = await _db.CourtImages
                .Where(i => i.CourtId == courtId && i.IsPrimary)
                .Select(i => i.Id)
                .ToListAsync(ct);
            foreach (var id in currentPrimaryIds)
            {
                SetImagePrimaryFlag(id, false);
            }
        }

        var normalizedCaption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        var entity = new Domain.Entities.CourtImage
        {
            CourtId = courtId,
            Bytes = bytes,
            ContentType = contentType,
            IsPrimary = makePrimary,
            Caption = normalizedCaption,
        };
        _db.CourtImages.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Added image {ImageId} to court {CourtId} (primary={IsPrimary}).", entity.Id, courtId, entity.IsPrimary);

        return ToImageDto(new ImageRow(entity.Id, entity.CourtId, entity.IsPrimary, entity.Caption));
    }

    public async Task<CourtImageDto> SetPrimaryImageAsync(long courtId, long imageId, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        // Read only the scalar columns (never the bytea blob) needed to re-balance the primary flag.
        var images = await _db.CourtImages
            .Where(i => i.CourtId == courtId)
            .Select(i => new ImageRow(i.Id, i.CourtId, i.IsPrimary, i.Caption))
            .ToListAsync(ct);
        var target = images.FirstOrDefault(i => i.Id == imageId)
            ?? throw new NotFoundException($"Image {imageId} was not found on court {courtId}.");

        // Flip only the IsPrimary column via stub entities — the blob is never loaded or rewritten.
        foreach (var image in images)
        {
            var shouldBePrimary = image.Id == imageId;
            if (image.IsPrimary != shouldBePrimary)
            {
                SetImagePrimaryFlag(image.Id, shouldBePrimary);
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Set image {ImageId} as primary for court {CourtId}.", imageId, courtId);

        return ToImageDto(target with { IsPrimary = true });
    }

    public async Task DeleteImageAsync(long courtId, long imageId, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        // Read only the scalar columns (never the bytea blob) to decide the delete + primary re-balance.
        var images = await _db.CourtImages
            .Where(i => i.CourtId == courtId)
            .Select(i => new ImageRow(i.Id, i.CourtId, i.IsPrimary, i.Caption))
            .ToListAsync(ct);
        var target = images.FirstOrDefault(i => i.Id == imageId)
            ?? throw new NotFoundException($"Image {imageId} was not found on court {courtId}.");

        // Delete by key — reuse the tracked instance if present (avoids an identity conflict), else a key-only stub
        // so the blob is never loaded.
        var trackedTarget = _db.CourtImages.Local.FirstOrDefault(i => i.Id == imageId);
        _db.CourtImages.Remove(trackedTarget ?? new Domain.Entities.CourtImage { Id = imageId });

        if (target.IsPrimary)
        {
            // Promote the earliest remaining image (lowest id) so the court keeps exactly one primary.
            var promote = images.Where(i => i.Id != imageId).OrderBy(i => i.Id).FirstOrDefault();
            if (promote is not null)
            {
                SetImagePrimaryFlag(promote.Id, true);
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted image {ImageId} from court {CourtId}.", imageId, courtId);
    }

    public async Task<IReadOnlyList<CourtAmenityDto>> GetAmenitiesAsync(long courtId, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        return await ProjectAmenities(courtId, ct);
    }

    public async Task<IReadOnlyList<CourtAmenityDto>> SetAmenitiesAsync(
        long courtId, SetCourtAmenitiesRequest request, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        var inputs = request.Amenities ?? Array.Empty<CourtAmenityInput>();

        // Validate every referenced amenity exists in ONE query (clean 404 naming the first missing id, not a raw
        // FK violation, and not a round-trip per amenity).
        var requestedIds = inputs.Select(i => i.AmenityId).Distinct().ToList();
        if (requestedIds.Count > 0)
        {
            var knownIds = (await _db.Amenities
                .Where(a => requestedIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(ct)).ToHashSet();
            foreach (var id in requestedIds)
            {
                if (!knownIds.Contains(id))
                {
                    throw new NotFoundException($"Amenity {id} not found.");
                }
            }
        }

        // Replace the whole set: drop the existing rows, add the request rows. Distinct AmenityIds are enforced by
        // the validator, so the unique(CourtId, AmenityId) index is never violated here.
        var existing = await _db.CourtAmenities.Where(ca => ca.CourtId == courtId).ToListAsync(ct);
        _db.CourtAmenities.RemoveRange(existing);

        foreach (var input in inputs)
        {
            _db.CourtAmenities.Add(new Domain.Entities.CourtAmenity
            {
                CourtId = courtId,
                AmenityId = input.AmenityId,
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
                IsHighlighted = input.IsHighlighted,
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Replaced amenity set for court {CourtId} ({Count} amenities).", courtId, inputs.Count);

        return await ProjectAmenities(courtId, ct);
    }

    public async Task<ImageContent?> GetImageContentAsync(long imageId, CancellationToken ct = default)
    {
        var row = await _db.CourtImages.AsNoTracking()
            .Where(i => i.Id == imageId)
            .Select(i => new { i.Bytes, i.ContentType })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new ImageContent(row.Bytes, row.ContentType);
    }

    /// <summary>The amenity read shared by GET and the post-write re-read: JOINs the amenity for name/icon (no
    /// N+1), ordered by id.</summary>
    private async Task<IReadOnlyList<CourtAmenityDto>> ProjectAmenities(long courtId, CancellationToken ct)
    {
        return await _db.CourtAmenities.AsNoTracking()
            .Where(ca => ca.CourtId == courtId)
            .OrderBy(ca => ca.Id)
            .Select(ca => new CourtAmenityDto(
                ca.Id,
                ca.CourtId,
                ca.AmenityId,
                ca.Amenity.Name,
                ca.Amenity.IconKey,
                ca.Note,
                ca.IsHighlighted))
            .ToListAsync(ct);
    }

    /// <summary>Updates ONLY the <c>IsPrimary</c> column of an image (by id) via a stub entity, so the up-to-5 MB
    /// <c>Bytes</c> blob is never loaded or rewritten just to flip a flag. Uses change-tracker stubs (not
    /// <c>ExecuteUpdate</c>) so it works on both Npgsql and the EF InMemory provider.</summary>
    private void SetImagePrimaryFlag(long imageId, bool isPrimary)
    {
        // If the row is already tracked in this unit of work, mutate it directly (avoids an identity conflict);
        // otherwise attach a key-only stub and flag just IsPrimary so the bytea Bytes blob is never loaded.
        var tracked = _db.CourtImages.Local.FirstOrDefault(i => i.Id == imageId);
        if (tracked is not null)
        {
            tracked.IsPrimary = isPrimary;
            return;
        }

        var stub = new Domain.Entities.CourtImage { Id = imageId, IsPrimary = isPrimary };
        _db.CourtImages.Attach(stub);
        _db.Entry(stub).Property(x => x.IsPrimary).IsModified = true;
    }

    /// <summary>Builds the relative image Url in memory (never in the SQL projection) so the read is
    /// provider-agnostic.</summary>
    private static CourtImageDto ToImageDto(ImageRow r) =>
        new(r.Id, r.CourtId, $"/api/images/{r.Id}", r.IsPrimary, r.Caption);

    /// <summary>Verifies the court exists so a missing court is a clean 404, not an empty media list.</summary>
    private async Task EnsureCourtExistsAsync(long courtId, CancellationToken ct)
    {
        if (!await _db.Courts.AnyAsync(c => c.Id == courtId, ct))
        {
            throw new NotFoundException($"Court {courtId} was not found.");
        }
    }

    /// <summary>Lightweight scalar projection of a court image — excludes the bytea blob; the Url is added in
    /// memory by <see cref="ToImageDto"/>.</summary>
    private sealed record ImageRow(long Id, long CourtId, bool IsPrimary, string? Caption);
}
