using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Reviews;
using Courtly.Contracts.Common;
using Courtly.Contracts.Reviews;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Reviews;

/// <summary>
/// Feature 24 DoD (auto): the review write is allowed only on a <see cref="ReservationStatus.Completed"/> reservation the
/// caller owns and once per reservation; the court is derived from the reservation (never the body); the read list is
/// paged + newest-first with the reviewer name resolved; and the eligibility probe is true only when the caller has a
/// completed, not-yet-reviewed reservation for the court. Mirrors the F14 service harness — EF InMemory, fresh DB per
/// test, NullLogger, fixed <see cref="IClock"/>, fake <see cref="ICurrentUser"/>.
/// </summary>
public class ReviewServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; init; } = Now;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string? Email => null;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAtUtc => null;
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"reviews-{Guid.NewGuid()}")
            .Options);

    private static ReviewService NewService(CourtlyDbContext db, Guid? userId = null, DateTime? now = null)
    {
        var clock = new TestClock { UtcNow = now ?? Now };
        var current = new TestCurrentUser { UserId = userId };
        return new ReviewService(db, clock, current, NullLogger<ReviewService>.Instance);
    }

    /// <summary>Seeds the FK chain + one active court with one slot, returning their ids.</summary>
    private static async Task<(long courtId, long slotId)> SeedCourtWithSlotAsync(CourtlyDbContext db)
    {
        var country = new Country { Name = "Bosnia", IsoCode = "BIH" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        var city = new City { Name = "Sarajevo", CountryId = country.Id };
        var surface = new SurfaceType { Name = "Clay" };
        var courtType = new CourtType { Name = "Tennis" };
        db.Cities.Add(city);
        db.SurfaceTypes.Add(surface);
        db.CourtTypes.Add(courtType);
        await db.SaveChangesAsync();
        var court = new Court
        {
            Name = "Center Court",
            CityId = city.Id,
            SurfaceTypeId = surface.Id,
            CourtTypeId = courtType.Id,
            IsActive = true,
            HourlyPrice = 30m,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        var slot = new TimeSlot
        {
            CourtId = court.Id,
            StartUtc = Now.AddHours(-3),
            EndUtc = Now.AddHours(-2),
            Price = 30m,
            Bucket = TimeOfDayBucket.Afternoon,
            IsActive = true,
        };
        db.TimeSlots.Add(slot);
        await db.SaveChangesAsync();
        return (court.Id, slot.Id);
    }

    private static async Task<Guid> SeedUserAsync(CourtlyDbContext db, string suffix)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id,
            FirstName = "Test",
            LastName = suffix,
            Email = $"{suffix}@courtly.test",
            UserName = suffix,
            IsActive = true,
            CreatedAtUtc = Now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<long> InsertReservationAsync(
        CourtlyDbContext db, Guid userId, long courtId, long slotId, ReservationStatus status)
    {
        var reservation = new Reservation
        {
            UserId = userId,
            CourtId = courtId,
            TimeSlotId = slotId,
            Status = status,
            TotalPrice = 30m,
            CreatedAtUtc = Now,
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.Id;
    }

    private static async Task<long> InsertReviewAsync(
        CourtlyDbContext db, long reservationId, long courtId, Guid userId, int rating, string? comment,
        DateTime? at = null)
    {
        var review = new Review
        {
            ReservationId = reservationId,
            CourtId = courtId,
            UserId = userId,
            Rating = rating,
            Comment = comment,
            CreatedAtUtc = at ?? Now,
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    // --- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_creates_a_review_on_a_completed_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "alice");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Completed);
        var svc = NewService(db, userId);

        var dto = await svc.CreateAsync(new CreateReviewRequest(rid, 5, "  Great court!  "));

        Assert.True(dto.Id > 0);
        Assert.Equal(courtId, dto.CourtId);              // court derived from the reservation
        Assert.Equal(5, dto.Rating);
        Assert.Equal("Great court!", dto.Comment);       // trimmed
        Assert.Equal("Test alice", dto.ReviewerName);
        Assert.Equal(Now, dto.CreatedAtUtc);             // stamped from IClock
        Assert.Equal(1, await db.Reviews.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_keeps_a_blank_comment_null()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "bob");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Completed);
        var svc = NewService(db, userId);

        var dto = await svc.CreateAsync(new CreateReviewRequest(rid, 4, "   "));

        Assert.Null(dto.Comment);
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Cancelled)]
    public async Task CreateAsync_blocks_a_non_completed_reservation(ReservationStatus status)
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "early");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, status);
        var svc = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new CreateReviewRequest(rid, 5, "nope")));
    }

    [Fact]
    public async Task CreateAsync_by_a_non_owner_is_forbidden()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Completed);
        var stranger = await SeedUserAsync(db, "stranger");
        var svc = NewService(db, stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(new CreateReviewRequest(rid, 5, "not mine")));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_second_review_for_the_same_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "twice");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Completed);
        var svc = NewService(db, userId);
        await svc.CreateAsync(new CreateReviewRequest(rid, 5, "first"));

        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(new CreateReviewRequest(rid, 3, "second")));
        Assert.Equal(1, await db.Reviews.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_missing_reservation_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db, await SeedUserAsync(db, "ghost"));

        await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(new CreateReviewRequest(999, 5, "where?")));
    }

    [Fact]
    public async Task CreateAsync_unauthenticated_throws_Unauthorized()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "someone");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Completed);
        var svc = NewService(db, userId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => svc.CreateAsync(new CreateReviewRequest(rid, 5, "anon")));
    }

    // --- Read list ------------------------------------------------------------------------------

    [Fact]
    public async Task GetForCourtAsync_returns_a_courts_reviews_newest_first_with_reviewer_name()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var u1 = await SeedUserAsync(db, "older");
        var u2 = await SeedUserAsync(db, "newer");
        var r1 = await InsertReservationAsync(db, u1, courtId, slotId, ReservationStatus.Completed);
        var r2 = await InsertReservationAsync(db, u2, courtId, slotId, ReservationStatus.Completed);
        await InsertReviewAsync(db, r1, courtId, u1, 3, "ok", at: Now.AddDays(-2));
        await InsertReviewAsync(db, r2, courtId, u2, 5, "great", at: Now.AddDays(-1));
        var svc = NewService(db, u1);

        var page = await svc.GetForCourtAsync(courtId, new PaginationQuery());

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("great", page.Items[0].Comment);       // newest first
        Assert.Equal("Test newer", page.Items[0].ReviewerName);
        Assert.Equal("ok", page.Items[1].Comment);
    }

    [Fact]
    public async Task GetForCourtAsync_excludes_other_courts_reviews()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var (otherCourtId, otherSlotId) = await SeedCourtWithSlotAsync(db);
        var u = await SeedUserAsync(db, "mixed");
        var rThis = await InsertReservationAsync(db, u, courtId, slotId, ReservationStatus.Completed);
        var rOther = await InsertReservationAsync(db, u, otherCourtId, otherSlotId, ReservationStatus.Completed);
        await InsertReviewAsync(db, rThis, courtId, u, 4, "this court");
        await InsertReviewAsync(db, rOther, otherCourtId, u, 2, "other court");
        var svc = NewService(db, u);

        var page = await svc.GetForCourtAsync(courtId, new PaginationQuery());

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("this court", Assert.Single(page.Items).Comment);
    }

    [Fact]
    public async Task GetForCourtAsync_paginates()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var u = await SeedUserAsync(db, "prolific");
        for (var i = 0; i < 3; i++)
        {
            var rid = await InsertReservationAsync(db, u, courtId, slotId, ReservationStatus.Completed);
            await InsertReviewAsync(db, rid, courtId, u, 5, $"review {i}", at: Now.AddMinutes(i));
        }
        var svc = NewService(db, u);

        var page = await svc.GetForCourtAsync(courtId, new PaginationQuery { Page = 1, PageSize = 2 });

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
    }

    // --- Eligibility ----------------------------------------------------------------------------

    [Fact]
    public async Task GetEligibilityAsync_true_with_a_completed_unreviewed_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "eligible");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Completed);
        var svc = NewService(db, userId);

        var result = await svc.GetEligibilityAsync(courtId);

        Assert.True(result.CanReview);
        Assert.Equal(rid, result.ReservationId);
    }

    [Fact]
    public async Task GetEligibilityAsync_false_without_a_completed_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "pendingonly");
        await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Confirmed);
        var svc = NewService(db, userId);

        var result = await svc.GetEligibilityAsync(courtId);

        Assert.False(result.CanReview);
        Assert.Null(result.ReservationId);
    }

    [Fact]
    public async Task GetEligibilityAsync_false_when_the_completed_reservation_is_already_reviewed()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "reviewedalready");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Completed);
        await InsertReviewAsync(db, rid, courtId, userId, 5, "done");
        var svc = NewService(db, userId);

        var result = await svc.GetEligibilityAsync(courtId);

        Assert.False(result.CanReview);
    }

    [Fact]
    public async Task GetEligibilityAsync_ignores_another_users_completed_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var someoneElse = await SeedUserAsync(db, "theirs");
        await InsertReservationAsync(db, someoneElse, courtId, slotId, ReservationStatus.Completed);
        var me = await SeedUserAsync(db, "mine");
        var svc = NewService(db, me);

        var result = await svc.GetEligibilityAsync(courtId);

        Assert.False(result.CanReview);
    }
}
