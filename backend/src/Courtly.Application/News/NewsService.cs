using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Application.Courts.Media;
using Courtly.Contracts.Common;
using Courtly.Contracts.News;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.News;

/// <summary>
/// News / announcement CRUD (feature 21). Reads are <c>AsNoTracking</c> + projected to <see cref="NewsDto"/> (never
/// entities, never raw bytes) and always paged; the author's name is resolved by projecting the Author nav (EF
/// translates it to a JOIN — no N+1). The Title/Text search runs at the database (case-insensitive). The image is
/// stored inline on the row as <c>bytea</c>; the DTO carries only the relative <c>/api/news/{id}/image</c> URL, which
/// — like the court image URL (feature 11) — is built in memory by <see cref="ToDto"/>, never inside the IQueryable
/// (the Npgsql provider cannot translate the id-into-string concat). Writes validate the image (MIME + magic bytes +
/// size) via the shared <see cref="ImageContentValidator"/> and take the author from the JWT (passed in), never from
/// the body. The admin list returns both published and hidden rows; <see cref="GetPublishedAsync"/> is the client
/// read path and returns only active rows whose publish time has arrived.
/// </summary>
public sealed class NewsService : INewsService
{
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<NewsService> _logger;

    public NewsService(CourtlyDbContext db, IClock clock, ILogger<NewsService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PagedResult<NewsDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, bool? activeOnly, CancellationToken ct = default)
    {
        var query = _db.News.AsNoTracking();

        if (activeOnly.HasValue)
        {
            query = query.Where(n => n.IsActive == activeOnly.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // ILike filters case-insensitively at the database (Postgres). The InMemory test provider does not
            // translate ILike, so fall back to a lowered Contains there — same case-insensitive semantics.
            query = _db.Database.IsNpgsql()
                ? query.Where(n => EF.Functions.ILike(n.Title, $"%{term}%") || EF.Functions.ILike(n.Text, $"%{term}%"))
                : query.Where(n =>
                    n.Title.ToLower().Contains(term.ToLower()) || n.Text.ToLower().Contains(term.ToLower()));
        }

        return await PageAsync(query, pagination, ct);
    }

    public async Task<PagedResult<NewsDto>> GetPublishedAsync(PaginationQuery pagination, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var query = _db.News.AsNoTracking().Where(n => n.IsActive && n.PublishedAtUtc <= now);
        return await PageAsync(query, pagination, ct);
    }

    public async Task<NewsDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var row = await Project(_db.News.AsNoTracking().Where(n => n.Id == id)).FirstOrDefaultAsync(ct);
        return row is null ? throw new NotFoundException($"News {id} was not found.") : ToDto(row);
    }

    public async Task<NewsDto> CreateAsync(
        CreateNewsRequest request, byte[] imageBytes, string contentType, Guid authorId, CancellationToken ct = default)
    {
        // Validate the image up front (throws ValidationException keyed on "file" on bad MIME / magic bytes / size).
        var normalizedContentType = ImageContentValidator.Validate(contentType, imageBytes);

        var entity = new Domain.Entities.News
        {
            Title = request.Title.Trim(),
            Text = request.Text.Trim(),
            ImageBytes = imageBytes,
            ImageContentType = normalizedContentType,
            // The field is the UTC instant; the client sends UTC wall-clock, so stamp the Kind (matches the
            // dashboard/report AsUtc convention) rather than shifting the value.
            PublishedAtUtc = DateTime.SpecifyKind(request.PublishedAtUtc, DateTimeKind.Utc),
            IsActive = request.IsActive,
            AuthorId = authorId,
        };

        _db.News.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created news {NewsId} ('{Title}') by author {AuthorId}.", entity.Id, entity.Title, authorId);
        return await GetByIdAsync(entity.Id, ct);
    }

    public async Task<NewsDto> UpdateAsync(
        long id, UpdateNewsRequest request, byte[]? imageBytes, string? contentType, CancellationToken ct = default)
    {
        var entity = await _db.News.FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException($"News {id} was not found.");

        entity.Title = request.Title.Trim();
        entity.Text = request.Text.Trim();
        entity.PublishedAtUtc = DateTime.SpecifyKind(request.PublishedAtUtc, DateTimeKind.Utc);
        entity.IsActive = request.IsActive;

        // Replace the image only when a new file was supplied; otherwise keep the existing bytes.
        if (imageBytes is { Length: > 0 })
        {
            entity.ImageContentType = ImageContentValidator.Validate(contentType, imageBytes);
            entity.ImageBytes = imageBytes;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated news {NewsId} ('{Title}').", entity.Id, entity.Title);
        return await GetByIdAsync(entity.Id, ct);
    }

    public async Task<ImageContent?> GetImageContentAsync(long id, CancellationToken ct = default)
    {
        var row = await _db.News.AsNoTracking()
            .Where(n => n.Id == id)
            .Select(n => new { n.ImageBytes, n.ImageContentType })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new ImageContent(row.ImageBytes, row.ImageContentType);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.News.FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException($"News {id} was not found.");

        _db.News.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted news {NewsId} ('{Title}').", id, entity.Title);
    }

    /// <summary>Orders newest-first (by publish time, id as a stable tie-break), pages at the DB, then builds the
    /// image URL + author name in memory via <see cref="ToDto"/>.</summary>
    private static async Task<PagedResult<NewsDto>> PageAsync(
        IQueryable<Domain.Entities.News> query, PaginationQuery pagination, CancellationToken ct)
    {
        var paged = await Project(query.OrderByDescending(n => n.PublishedAtUtc).ThenByDescending(n => n.Id))
            .ToPagedResultAsync(pagination, ct);

        return new PagedResult<NewsDto>(
            paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    /// <summary>Projects to the provider-agnostic <see cref="NewsRow"/> (Author nav -> JOIN, no image bytes); the
    /// URL/name strings are built later in memory.</summary>
    private static IQueryable<NewsRow> Project(IQueryable<Domain.Entities.News> query) =>
        query.Select(n => new NewsRow(
            n.Id, n.Title, n.Text, n.PublishedAtUtc, n.IsActive, n.Author.FirstName, n.Author.LastName));

    private static NewsDto ToDto(NewsRow r) => new(
        r.Id, r.Title, r.Text, $"/api/news/{r.Id}/image", r.PublishedAtUtc, r.IsActive,
        $"{r.FirstName} {r.LastName}".Trim());

    private sealed record NewsRow(
        long Id, string Title, string Text, DateTime PublishedAtUtc, bool IsActive, string FirstName, string LastName);
}
