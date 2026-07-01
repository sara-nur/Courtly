using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Contracts.Recommendations;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Recommendations;

/// <summary>
/// Content-based + popularity recommender (feature 29). It mines signals that are really written elsewhere — the
/// user's non-cancelled reservations (surface / court type / time-bucket / price / indoor) and their F23 search
/// history — into a normalized preference profile, scores every bookable court against it (blended 0.75 content /
/// 0.25 popularity, with a popularity-only path on cold-start), and reports the single strongest dimension per court
/// as an explainable reason. Yes/No feedback is upserted per (user, court) and fed back in: a "No" hard-excludes the
/// court next fetch, a "Yes" nudges it up. All the tunables live in <see cref="RecommendationWeights"/> so the code
/// stays literal-free and matches <c>recommender-dokumentacija.md</c> exactly.
///
/// Everything is computed fresh per request (a handful of bounded, <c>AsNoTracking</c> queries) so new bookings and
/// feedback take effect immediately — the per-user profile is dynamic (it changes with every booking), so it is
/// deliberately NOT cached, which would make a new booking or a "No" lag behind a TTL. Scoring runs in memory over a
/// bounded candidate set (≤ <see cref="RecommendationWeights.CandidateCap"/>) because explainability needs each
/// dimension's contribution. A "No" <b>demotes</b> the shown courts (a large negative) and a "Yes" nudges them up —
/// feedback never excludes, so the list cannot empty out.
/// </summary>
public sealed class RecommendationService : IRecommendationService
{
    private readonly CourtlyDbContext _db;
    private readonly ICourtService _courts;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        CourtlyDbContext db,
        ICourtService courts,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<RecommendationService> logger)
    {
        _db = db;
        _courts = courts;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RecommendationsResponse> GetAsync(PaginationQuery pagination, CancellationToken ct = default)
    {
        var page = pagination.Normalize();

        var userId = _currentUser.UserId;
        if (userId is null)
        {
            // Defensive: the controller is [Authorize], so this should not happen in practice.
            return EmptyResponse(page);
        }

        var profile = await BuildProfileAsync(userId.Value, ct);
        var candidates = await LoadCandidatesAsync(ct);
        var feedback = await LoadFeedbackAsync(userId.Value, ct);

        var candidateIds = candidates.Select(c => c.Id).ToList();
        // Which candidates have an upcoming active slot in the user's top bucket — one grouped query, no N+1.
        HashSet<long> topBucketCourtIds = profile.TopBucket is TimeOfDayBucket bucket
            ? await LoadCourtsWithUpcomingBucketAsync(candidateIds, bucket, ct)
            : [];

        var maxCompleted = candidates.Count == 0 ? 0 : candidates.Max(c => c.CompletedReservationCount);

        var scored = new List<ScoredCourt>(candidates.Count);
        foreach (var candidate in candidates)
        {
            // Feedback demotes (a "No") or nudges up (a "Yes"); it never excludes, so the list can't empty out even
            // when every shown court was rated. `null` = no feedback yet.
            bool? feedbackVote = feedback.TryGetValue(candidate.Id, out var vote) ? vote : null;
            scored.Add(Score(candidate, profile, topBucketCourtIds, maxCompleted, feedbackVote));
        }

        scored.Sort(CompareByScore);

        var total = scored.Count;
        var pageItems = scored.Skip(page.Skip).Take(page.PageSize).ToList();

        // Hydrate the page's court cards in a single query (DRY reuse of the catalog projection); preserve rank order.
        var pageIds = pageItems.Select(s => s.CourtId).ToList();
        var courtsById = (await _courts.GetByIdsAsync(pageIds, ct)).ToDictionary(c => c.Id);

        var items = pageItems
            .Where(s => courtsById.ContainsKey(s.CourtId))
            .Select(s => new RecommendationDto(
                courtsById[s.CourtId], s.Score, s.ReasonCode, s.Reason,
                feedback.TryGetValue(s.CourtId, out var fb) ? fb : null))
            .ToList();

        return new RecommendationsResponse(
            BuildSummary(profile),
            new PagedResult<RecommendationDto>(items, page.Page, page.PageSize, total));
    }

    public async Task RecordFeedbackAsync(RecommendationFeedbackRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return; // defensive (controller is [Authorize]); the validator guards shape
        }

        var courtIds = request.CourtIds.Distinct().ToList();
        if (courtIds.Count == 0)
        {
            return;
        }

        // Only rate courts that actually exist — a stale/foreign id is skipped, not an error.
        var existingCourtIds = await _db.Courts.AsNoTracking()
            .Where(c => courtIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (existingCourtIds.Count == 0)
        {
            return;
        }

        // Upsert one row per (user, court): load current rows (tracked), update-or-add, then a single SaveChanges.
        var existing = await _db.RecommendationFeedbacks
            .Where(f => f.UserId == userId.Value && existingCourtIds.Contains(f.CourtId))
            .ToDictionaryAsync(f => f.CourtId, ct);

        var now = _clock.UtcNow;
        var reason = request.IsHelpful ? "Marked helpful" : "Marked not helpful";
        foreach (var courtId in existingCourtIds)
        {
            if (existing.TryGetValue(courtId, out var row))
            {
                row.IsHelpful = request.IsHelpful;
                row.Reason = reason;
                row.CreatedAtUtc = now;
            }
            else
            {
                _db.RecommendationFeedbacks.Add(new Domain.Entities.RecommendationFeedback
                {
                    UserId = userId.Value,
                    CourtId = courtId,
                    IsHelpful = request.IsHelpful,
                    Reason = reason,
                    CreatedAtUtc = now,
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded recommendation feedback (helpful={IsHelpful}) for {Count} court(s) by user {UserId}.",
            request.IsHelpful, existingCourtIds.Count, userId.Value);
    }

    // ── Scoring ──────────────────────────────────────────────────────────────────────────────────────────────

    private static ScoredCourt Score(
        CandidateCourt c, PreferenceProfile p, HashSet<long> topBucketCourtIds, int maxCompleted, bool? feedback)
    {
        var adjustment = feedback switch
        {
            true => RecommendationWeights.FeedbackPositive,
            false => RecommendationWeights.FeedbackNegative,
            null => 0.0,
        };

        var popularityReservations = Norm(c.CompletedReservationCount, maxCompleted);
        var ratingNorm = c is { ReviewCount: > 0, AvgRating: not null }
            ? Math.Clamp((c.AvgRating.Value - 1.0) / 4.0, 0.0, 1.0)
            : 0.0;
        var popularity = RecommendationWeights.PopReservations * popularityReservations
            + RecommendationWeights.PopRating * ratingNorm;

        if (p.IsColdStart)
        {
            var coldReason = c.CompletedReservationCount > 0
                ? RecommendationReason.Popular
                : RecommendationReason.TopRated;
            return new ScoredCourt(
                c.Id, popularity + adjustment, coldReason, ReasonText(coldReason, c, p), c.IsFeatured, c.AvgRating);
        }

        // Content contributions (each already multiplied by its weight, so a disabled dimension contributes 0).
        var surface = RecommendationWeights.Surface * p.SurfaceWeight.GetValueOrDefault(c.SurfaceTypeId);
        var courtType = RecommendationWeights.CourtType * p.CourtTypeWeight.GetValueOrDefault(c.CourtTypeId);
        var bucket = p.TopBucket is not null && topBucketCourtIds.Contains(c.Id) ? RecommendationWeights.Bucket : 0.0;
        var price = p.TargetPrice is decimal target
            ? RecommendationWeights.Price * PriceProximity(c.HourlyPrice, target)
            : 0.0;
        var indoor = p.IndoorPref is double pref
            ? RecommendationWeights.Indoor * (c.IsIndoor ? pref : 1.0 - pref)
            : 0.0;

        var content = surface + courtType + bucket + price + indoor;
        var score = RecommendationWeights.ContentGroup * content
            + RecommendationWeights.PopularityGroup * popularity
            + adjustment;

        var reasonCode = PickReason(surface, bucket, price, indoor, courtType, c);
        return new ScoredCourt(c.Id, score, reasonCode, ReasonText(reasonCode, c, p), c.IsFeatured, c.AvgRating);
    }

    /// <summary>Min-max normalize against the batch maximum, guarding division by zero.</summary>
    private static double Norm(double value, double max) => max <= 0 ? 0.0 : value / max;

    /// <summary>Gaussian price closeness in (0,1]: 1.0 at the target, decaying with a tolerance of ±25% of the
    /// target (floored so cheap courts are not hypersensitive to small absolute gaps).</summary>
    private static double PriceProximity(decimal price, decimal target)
    {
        var t = (double)target;
        var sigma = Math.Max(t * RecommendationWeights.SigmaFactor, RecommendationWeights.SigmaFloor);
        var diff = (double)price - t;
        return Math.Exp(-(diff * diff) / (2.0 * sigma * sigma));
    }

    /// <summary>The single strongest enabled content dimension becomes the explainable reason; ties resolve toward
    /// the mockup's visible drivers (Surface &gt; TimeBucket &gt; Price &gt; Indoor &gt; CourtType). When no content
    /// dimension applies, fall back to a popularity reason.</summary>
    private static RecommendationReason PickReason(
        double surface, double bucket, double price, double indoor, double courtType, CandidateCourt c)
    {
        (RecommendationReason Reason, double Value)[] ranked =
        [
            (RecommendationReason.Surface, surface),
            (RecommendationReason.TimeBucket, bucket),
            (RecommendationReason.Price, price),
            (RecommendationReason.Indoor, indoor),
            (RecommendationReason.CourtType, courtType),
        ];

        var best = ranked[0];
        foreach (var entry in ranked)
        {
            if (entry.Value > best.Value)
            {
                best = entry;
            }
        }

        if (best.Value <= 0)
        {
            return c.CompletedReservationCount > 0 ? RecommendationReason.Popular : RecommendationReason.TopRated;
        }

        return best.Reason;
    }

    private static string ReasonText(RecommendationReason reason, CandidateCourt c, PreferenceProfile p) => reason switch
    {
        RecommendationReason.Surface => $"Because you like {c.SurfaceTypeName}",
        RecommendationReason.CourtType => $"More {c.CourtTypeName} courts",
        RecommendationReason.TimeBucket => $"{BucketEmoji(p.TopBucket)} {BucketLabel(p.TopBucket)} availability",
        RecommendationReason.Price => "Around your usual price",
        RecommendationReason.Indoor => c.IsIndoor ? "Indoor courts you prefer" : "Outdoor courts you prefer",
        RecommendationReason.Popular => "Popular right now",
        RecommendationReason.TopRated => "Highly rated",
        _ => "Recommended for you",
    };

    private static string BucketLabel(TimeOfDayBucket? bucket) => bucket switch
    {
        TimeOfDayBucket.Morning => "Morning",
        TimeOfDayBucket.Afternoon => "Afternoon",
        TimeOfDayBucket.Evening => "Evening",
        _ => "Preferred-time",
    };

    private static string BucketEmoji(TimeOfDayBucket? bucket) => bucket switch
    {
        TimeOfDayBucket.Morning => "☀️",
        TimeOfDayBucket.Afternoon => "🌤️",
        TimeOfDayBucket.Evening => "🌆",
        _ => "🎾",
    };

    private static RecommendationSummaryDto BuildSummary(PreferenceProfile p)
    {
        if (p.IsColdStart)
        {
            return new RecommendationSummaryDto(
                "New here — showing popular and top-rated courts to get you started.", IsContentBased: false, BasedOnBookings: 0);
        }

        var message = p.BasedOnBookings > 0
            ? $"Based on your {p.BasedOnBookings} booking{(p.BasedOnBookings == 1 ? string.Empty : "s")} and recent searches, here are some matches."
            : "Based on your recent searches, here are some matches.";
        return new RecommendationSummaryDto(message, IsContentBased: true, p.BasedOnBookings);
    }

    private static int CompareByScore(ScoredCourt a, ScoredCourt b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        var byFeatured = b.IsFeatured.CompareTo(a.IsFeatured);
        if (byFeatured != 0)
        {
            return byFeatured;
        }

        var byRating = (b.AvgRating ?? 0.0).CompareTo(a.AvgRating ?? 0.0);
        return byRating != 0 ? byRating : a.CourtId.CompareTo(b.CourtId);
    }

    private static RecommendationsResponse EmptyResponse(PaginationQuery page) => new(
        new RecommendationSummaryDto("No recommendations available.", IsContentBased: false, BasedOnBookings: 0),
        new PagedResult<RecommendationDto>([], page.Page, page.PageSize, 0));

    // ── Preference profile (computed fresh per request) ──────────────────────────────────────────────────────

    private async Task<PreferenceProfile> BuildProfileAsync(Guid userId, CancellationToken ct)
    {
        // Booking signals — non-cancelled reservations joined to their court + slot (surface/type/bucket/price/indoor).
        var bookings = await _db.Reservations.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status != ReservationStatus.Cancelled)
            .Select(r => new BookingSignal(
                r.Court.SurfaceTypeId, r.Court.CourtTypeId, r.TimeSlot.Bucket, r.Court.HourlyPrice, r.Court.IsIndoor))
            .ToListAsync(ct);

        // Search signals — everything the user explicitly searched (F23). Bucket is intentionally not captured on
        // search (always null), so the time-bucket preference is booking-derived only.
        var searches = await _db.SearchHistories.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new SearchSignal(s.SurfaceTypeId, s.CourtTypeId, s.MinPrice, s.MaxPrice, s.IndoorOnly))
            .ToListAsync(ct);

        if (bookings.Count + searches.Count == 0)
        {
            return PreferenceProfile.ColdStart;
        }

        var surfaceWeight = BuildWeightMap(
            bookings.Select(b => ((long?)b.SurfaceTypeId, RecommendationWeights.BookingSignalWeight))
                .Concat(searches.Select(s => (s.SurfaceTypeId, RecommendationWeights.SearchSignalWeight))));

        var courtTypeWeight = BuildWeightMap(
            bookings.Select(b => ((long?)b.CourtTypeId, RecommendationWeights.BookingSignalWeight))
                .Concat(searches.Select(s => (s.CourtTypeId, RecommendationWeights.SearchSignalWeight))));

        var bucketWeight = BuildWeightMap(
            bookings.Select(b => ((long?)(long)b.Bucket, RecommendationWeights.BookingSignalWeight)));
        TimeOfDayBucket? topBucket = bucketWeight.Count == 0
            ? null
            : (TimeOfDayBucket)bucketWeight.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;

        var prices = new List<decimal>();
        prices.AddRange(bookings.Select(b => b.HourlyPrice));
        foreach (var s in searches)
        {
            if (s.MinPrice.HasValue && s.MaxPrice.HasValue)
            {
                prices.Add((s.MinPrice.Value + s.MaxPrice.Value) / 2m);
            }
            else if (s.MinPrice.HasValue)
            {
                prices.Add(s.MinPrice.Value);
            }
            else if (s.MaxPrice.HasValue)
            {
                prices.Add(s.MaxPrice.Value);
            }
        }
        decimal? targetPrice = prices.Count == 0 ? null : Median(prices);

        var indoorSignals = new List<bool>();
        indoorSignals.AddRange(bookings.Select(b => b.IsIndoor));
        indoorSignals.AddRange(searches.Where(s => s.IndoorOnly.HasValue).Select(s => s.IndoorOnly!.Value));
        double? indoorPref = indoorSignals.Count == 0
            ? null
            : indoorSignals.Count(x => x) / (double)indoorSignals.Count;

        return new PreferenceProfile(
            IsColdStart: false,
            surfaceWeight,
            courtTypeWeight,
            topBucket,
            targetPrice,
            indoorPref,
            BasedOnBookings: bookings.Count,
            SignalCount: bookings.Count + searches.Count);
    }

    /// <summary>Sums the weighted signal counts per key and normalizes to a distribution (Σ = 1). Null keys are
    /// ignored; an empty input yields an empty map (guarding division by zero).</summary>
    private static IReadOnlyDictionary<long, double> BuildWeightMap(IEnumerable<(long? Key, double Weight)> entries)
    {
        var raw = new Dictionary<long, double>();
        foreach (var (key, weight) in entries)
        {
            if (key is long k)
            {
                raw[k] = raw.GetValueOrDefault(k) + weight;
            }
        }

        var total = raw.Values.Sum();
        return total <= 0 ? raw : raw.ToDictionary(kv => kv.Key, kv => kv.Value / total);
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    // ── Bounded data loads ───────────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<CandidateCourt>> LoadCandidatesAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return await _db.Courts.AsNoTracking()
            .Where(c => c.IsActive
                && !c.MaintenanceLogs.Any(m =>
                    (m.Status == MaintenanceStatus.Scheduled || m.Status == MaintenanceStatus.InProgress)
                    && m.StartUtc <= now && (m.EndUtc == null || m.EndUtc > now)))
            // Deterministic cap: featured first, then id. Real ranking is the in-memory score below.
            .OrderByDescending(c => c.IsFeatured)
            .ThenBy(c => c.Id)
            .Take(RecommendationWeights.CandidateCap)
            .Select(c => new CandidateCourt(
                c.Id,
                c.SurfaceTypeId,
                c.SurfaceType.Name,
                c.CourtTypeId,
                c.CourtType.Name,
                c.IsIndoor,
                c.IsFeatured,
                c.HourlyPrice,
                _db.Reviews.Where(r => r.CourtId == c.Id).Select(r => (double?)r.Rating).Average(),
                _db.Reviews.Count(r => r.CourtId == c.Id),
                _db.Reservations.Count(r => r.CourtId == c.Id && r.Status == ReservationStatus.Completed)))
            .ToListAsync(ct);
    }

    private async Task<Dictionary<long, bool>> LoadFeedbackAsync(Guid userId, CancellationToken ct) =>
        await _db.RecommendationFeedbacks.AsNoTracking()
            .Where(f => f.UserId == userId)
            .ToDictionaryAsync(f => f.CourtId, f => f.IsHelpful, ct);

    private async Task<HashSet<long>> LoadCourtsWithUpcomingBucketAsync(
        IReadOnlyCollection<long> candidateIds, TimeOfDayBucket bucket, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var ids = await _db.TimeSlots.AsNoTracking()
            .Where(t => t.IsActive && t.StartUtc >= now && t.Bucket == bucket && candidateIds.Contains(t.CourtId))
            .Select(t => t.CourtId)
            .Distinct()
            .ToListAsync(ct);
        return [.. ids];
    }

    // ── Internal shapes ──────────────────────────────────────────────────────────────────────────────────────

    private sealed record CandidateCourt(
        long Id,
        long SurfaceTypeId,
        string SurfaceTypeName,
        long CourtTypeId,
        string CourtTypeName,
        bool IsIndoor,
        bool IsFeatured,
        decimal HourlyPrice,
        double? AvgRating,
        int ReviewCount,
        int CompletedReservationCount);

    private sealed record ScoredCourt(
        long CourtId, double Score, RecommendationReason ReasonCode, string Reason, bool IsFeatured, double? AvgRating);

    private sealed record BookingSignal(
        long SurfaceTypeId, long CourtTypeId, TimeOfDayBucket Bucket, decimal HourlyPrice, bool IsIndoor);

    private sealed record SearchSignal(
        long? SurfaceTypeId, long? CourtTypeId, decimal? MinPrice, decimal? MaxPrice, bool? IndoorOnly);

    private sealed record PreferenceProfile(
        bool IsColdStart,
        IReadOnlyDictionary<long, double> SurfaceWeight,
        IReadOnlyDictionary<long, double> CourtTypeWeight,
        TimeOfDayBucket? TopBucket,
        decimal? TargetPrice,
        double? IndoorPref,
        int BasedOnBookings,
        int SignalCount)
    {
        public static readonly PreferenceProfile ColdStart = new(
            IsColdStart: true,
            new Dictionary<long, double>(),
            new Dictionary<long, double>(),
            TopBucket: null,
            TargetPrice: null,
            IndoorPref: null,
            BasedOnBookings: 0,
            SignalCount: 0);
    }
}
