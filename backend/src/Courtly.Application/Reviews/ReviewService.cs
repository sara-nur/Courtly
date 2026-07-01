using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Reviews;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Reviews;

/// <summary>
/// Court reviews (feature 24). Reads are <c>AsNoTracking</c>, projected to <see cref="ReviewDto"/> (never entities) and
/// paged, newest-first; the reviewer name is resolved by projecting the user nav (a JOIN, no N+1) and the raw user id
/// is never exposed. The write is server-authoritative (rubric §7.1): the request carries only a reservation id — the
/// owner is the caller (from <see cref="ICurrentUser"/>, never the body) and the court is derived from the reservation.
/// A review is allowed <b>only on a <see cref="ReservationStatus.Completed"/> reservation the caller owns</b>, and
/// <b>once per reservation</b>: a service pre-check rejects a duplicate, and the one-to-one
/// <c>Review→Reservation</c> unique index is the hard guard for the concurrent race — a unique violation on insert is
/// translated to a friendly 409, never a 500 (mirrors the feature-14 overlap defense). Every timestamp comes from
/// <see cref="IClock"/> (UTC). Posting a review naturally moves the court's average rating / review count, which the
/// catalog read (feature 23) already aggregates at the database.
/// </summary>
public sealed class ReviewService : IReviewService
{
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        CourtlyDbContext db, IClock clock, ICurrentUser currentUser, ILogger<ReviewService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<PagedResult<ReviewDto>> GetForCourtAsync(
        long courtId, PaginationQuery pagination, CancellationToken ct = default)
    {
        // Newest-first (rubric §6: latest record on top). The user nav projects to a JOIN — no N+1.
        var paged = await Project(_db.Reviews.AsNoTracking().Where(r => r.CourtId == courtId)
                .OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id))
            .ToPagedResultAsync(pagination, ct);

        return new PagedResult<ReviewDto>(
            paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<ReviewDto> CreateAsync(CreateReviewRequest request, CancellationToken ct = default)
    {
        var userId = CurrentUserId();

        // The reservation drives owner + court — the client is never trusted for either (rubric §7.1).
        var reservation = await _db.Reservations.AsNoTracking()
            .Where(r => r.Id == request.ReservationId)
            .Select(r => new { r.Id, r.UserId, r.CourtId, r.Status })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Reservation {request.ReservationId} was not found.");

        if (reservation.UserId != userId)
        {
            throw new ForbiddenException("You can only review your own reservations.");
        }

        if (reservation.Status != ReservationStatus.Completed)
        {
            throw new BusinessException("You can only review a booking once it has been completed.");
        }

        // One review per reservation. The pre-check gives a friendly message; the unique index behind it (the
        // Review→Reservation one-to-one) is the hard guard for the concurrent race.
        if (await _db.Reviews.AsNoTracking().AnyAsync(r => r.ReservationId == reservation.Id, ct))
        {
            throw new ConflictException("You have already reviewed this booking.");
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var entity = new Review
        {
            ReservationId = reservation.Id,
            CourtId = reservation.CourtId, // derived from the reservation, never the body
            UserId = userId,
            Rating = request.Rating,
            Comment = comment,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.Reviews.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race: a review for this reservation already exists (the one-to-one unique index rejected it).
            if (await _db.Reviews.AsNoTracking().AnyAsync(r => r.ReservationId == reservation.Id && r.Id != entity.Id, ct))
            {
                throw new ConflictException("You have already reviewed this booking.");
            }

            throw;
        }

        _logger.LogInformation(
            "Created review {ReviewId} (user {UserId}, court {CourtId}, reservation {ReservationId}).",
            entity.Id, userId, reservation.CourtId, reservation.Id);

        return await ProjectAsync(entity.Id, ct);
    }

    public async Task<ReviewEligibilityDto> GetEligibilityAsync(long courtId, CancellationToken ct = default)
    {
        var userId = CurrentUserId();

        // The caller's most recent COMPLETED reservation for this court that hasn't been reviewed yet. Expressed as a
        // NOT-EXISTS over the Reviews DbSet (provider-agnostic: translates on Npgsql and EF InMemory alike) rather than
        // a navigation null-check.
        var reservationId = await _db.Reservations.AsNoTracking()
            .Where(r => r.UserId == userId
                && r.CourtId == courtId
                && r.Status == ReservationStatus.Completed
                && !_db.Reviews.Any(rv => rv.ReservationId == r.Id))
            .OrderByDescending(r => r.Id)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);

        return new ReviewEligibilityDto(reservationId.HasValue, reservationId);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private Guid CurrentUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("You must be signed in to manage reviews.");

    /// <summary>Re-reads one review into the wire DTO (single JOIN query) so a write returns the same shape as a read.</summary>
    private async Task<ReviewDto> ProjectAsync(long id, CancellationToken ct) =>
        ToDto(await Project(_db.Reviews.AsNoTracking().Where(r => r.Id == id)).FirstAsync(ct));

    /// <summary>Projection shared by the list and the post-write re-read: JOINs the user for the reviewer name so the
    /// DTO is built without an extra query. The display name is resolved in memory by <see cref="ToDto"/> so the SQL
    /// stays provider-agnostic (Npgsql in prod, EF InMemory in tests).</summary>
    private static IQueryable<ReviewRow> Project(IQueryable<Review> source) =>
        source.Select(r => new ReviewRow(
            r.Id,
            r.CourtId,
            r.User.FirstName + " " + r.User.LastName,
            r.User.Email,
            r.Rating,
            r.Comment,
            r.CreatedAtUtc));

    private static ReviewDto ToDto(ReviewRow r) =>
        new(r.Id, r.CourtId, FullName(r.ReviewerName, r.ReviewerEmail), r.Rating, r.Comment, r.CreatedAtUtc);

    private static string FullName(string? name, string? email)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return string.IsNullOrWhiteSpace(email) ? "Anonymous" : email;
    }

    /// <summary>Provider-agnostic intermediate for the review projection (reviewer name/email JOINed); the display name
    /// is resolved in memory by <see cref="ToDto"/>.</summary>
    private sealed record ReviewRow(
        long Id,
        long CourtId,
        string? ReviewerName,
        string? ReviewerEmail,
        int Rating,
        string? Comment,
        DateTime CreatedAtUtc);
}
