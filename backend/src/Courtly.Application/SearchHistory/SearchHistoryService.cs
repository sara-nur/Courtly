using Courtly.Application.Abstractions;
using Courtly.Contracts.SearchHistory;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.SearchHistory;

/// <summary>
/// Persists a user's court search as a recommender signal (feature 23). The owner comes from <see cref="ICurrentUser"/>
/// (the JWT, never the body); an unauthenticated caller is a silent no-op. To keep the signal clean, an identical
/// search repeated within <see cref="DedupeWindowSeconds"/> seconds is dropped via an <c>AnyAsync</c> existence check
/// rather than written again. The <c>RawQuery</c> is trimmed and null-if-blank, capped at 300 chars; <c>Bucket</c> is
/// left null (derived later, not supplied by the client). Returns nothing — the entity never leaves this layer.
/// </summary>
public sealed class SearchHistoryService : ISearchHistoryService
{
    /// <summary>How recently an identical search must have been recorded for a repeat to be treated as a duplicate.</summary>
    private const int DedupeWindowSeconds = 10;

    /// <summary>Upper bound on the stored raw query text.</summary>
    private const int MaxRawQueryLength = 300;

    private readonly CourtlyDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<SearchHistoryService> _logger;

    public SearchHistoryService(CourtlyDbContext db, ICurrentUser currentUser, IClock clock, ILogger<SearchHistoryService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecordAsync(RecordSearchRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return;
        }

        var rawQuery = string.IsNullOrWhiteSpace(request.RawQuery) ? null : request.RawQuery.Trim();
        if (rawQuery is { Length: > MaxRawQueryLength })
        {
            rawQuery = rawQuery[..MaxRawQueryLength];
        }

        // Drop an identical search repeated inside the dedupe window so the recommender signal is not skewed by the
        // client re-posting the same filters (e.g. on re-render / debounce).
        var cutoff = _clock.UtcNow.AddSeconds(-DedupeWindowSeconds);
        var isDuplicate = await _db.SearchHistories.AsNoTracking().AnyAsync(
            h => h.UserId == userId.Value
                && h.CreatedAtUtc >= cutoff
                && h.SurfaceTypeId == request.SurfaceTypeId
                && h.CourtTypeId == request.CourtTypeId
                && h.MinPrice == request.MinPrice
                && h.MaxPrice == request.MaxPrice
                && h.IndoorOnly == request.IndoorOnly
                && h.RawQuery == rawQuery,
            ct);

        if (isDuplicate)
        {
            _logger.LogDebug("Deduped search history for user {UserId} (identical search within {Window}s).",
                userId.Value, DedupeWindowSeconds);
            return;
        }

        var entity = new Domain.Entities.SearchHistory
        {
            UserId = userId.Value,
            SurfaceTypeId = request.SurfaceTypeId,
            CourtTypeId = request.CourtTypeId,
            Bucket = null,
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            IndoorOnly = request.IndoorOnly,
            RawQuery = rawQuery,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.SearchHistories.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Recorded search history for user {UserId}.", userId.Value);
    }
}
