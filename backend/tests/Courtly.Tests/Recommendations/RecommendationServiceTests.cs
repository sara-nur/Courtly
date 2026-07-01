using Courtly.Application.Abstractions;
using Courtly.Application.Courts;
using Courtly.Application.Recommendations;
using Courtly.Contracts.Common;
using Courtly.Contracts.Recommendations;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Recommendations;

/// <summary>
/// Feature 29 DoD (auto): the recommender's scoring is driven by really-written signals and behaves per
/// <c>recommender-dokumentacija.md</c>. Mirrors the F19 harness — EF InMemory, fresh DB per test, fixed
/// <see cref="IClock"/>, and the real <see cref="CourtService"/> for card hydration. Covers content weighting,
/// the time-bucket reason, cold-start = popularity, feedback demotion ("No" sinks a court, never removes it) +
/// reinforcement + upsert, price proximity, maintenance/inactive exclusion, division-by-zero safety, summary
/// honesty and pagination.
/// </summary>
public class RecommendationServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);

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
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public bool IsInRole(string role) => false;
    }

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"reco-{Guid.NewGuid()}")
            .Options);

    private static RecommendationService NewService(CourtlyDbContext db, Guid? userId)
    {
        var clock = new TestClock();
        var courts = new CourtService(db, clock, NullLogger<CourtService>.Instance);
        return new RecommendationService(
            db, courts, new TestCurrentUser { UserId = userId }, clock,
            NullLogger<RecommendationService>.Instance);
    }

    private static PaginationQuery Page(int page = 1, int size = 20) => new() { Page = page, PageSize = size };

    // --- seed helpers -------------------------------------------------------------------------------

    private sealed record Refs(long CityId, long ClayId, long GrassId, long SinglesId, long DoublesId);

    private static async Task<Refs> SeedRefsAsync(CourtlyDbContext db)
    {
        var country = new Country { Name = "Bosnia", IsoCode = "BIH" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var city = new City { Name = "Sarajevo", CountryId = country.Id };
        var clay = new SurfaceType { Name = "Clay" };
        var grass = new SurfaceType { Name = "Grass" };
        var singles = new CourtType { Name = "Singles" };
        var doubles = new CourtType { Name = "Doubles" };
        db.Cities.Add(city);
        db.SurfaceTypes.AddRange(clay, grass);
        db.CourtTypes.AddRange(singles, doubles);
        await db.SaveChangesAsync();

        return new Refs(city.Id, clay.Id, grass.Id, singles.Id, doubles.Id);
    }

    private static async Task<long> SeedCourtAsync(
        CourtlyDbContext db, Refs r, long surfaceId, long courtTypeId, string name,
        bool isActive = true, bool isFeatured = false, bool isIndoor = false, decimal price = 40m)
    {
        var court = new Court
        {
            Name = name,
            CityId = r.CityId,
            SurfaceTypeId = surfaceId,
            CourtTypeId = courtTypeId,
            IsActive = isActive,
            IsFeatured = isFeatured,
            IsIndoor = isIndoor,
            HourlyPrice = price,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        return court.Id;
    }

    private static async Task<Guid> SeedUserAsync(CourtlyDbContext db)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id,
            FirstName = "Test",
            LastName = "User",
            Email = $"u{id:N}@courtly.test",
            UserName = $"u{id:N}",
            IsActive = true,
            CreatedAtUtc = Now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<long> SeedSlotAsync(
        CourtlyDbContext db, long courtId, TimeOfDayBucket bucket, DateTime startUtc, bool isActive = true)
    {
        var slot = new TimeSlot
        {
            CourtId = courtId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddHours(1),
            Price = 40m,
            Bucket = bucket,
            IsActive = isActive,
        };
        db.TimeSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot.Id;
    }

    /// <summary>A non-cancelled reservation (a content signal). The slot's bucket becomes the user's time-bucket
    /// signal; the court's price becomes a price signal.</summary>
    private static async Task SeedBookingAsync(
        CourtlyDbContext db, long courtId, Guid userId, TimeOfDayBucket bucket,
        ReservationStatus status = ReservationStatus.Completed)
    {
        var slotId = await SeedSlotAsync(db, courtId, bucket, Now.AddDays(-3));
        db.Reservations.Add(new Reservation
        {
            CourtId = courtId,
            TimeSlotId = slotId,
            UserId = userId,
            Status = status,
            TotalPrice = 40m,
            CreatedAtUtc = Now.AddDays(-3),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSearchAsync(
        CourtlyDbContext db, Guid userId, long? surfaceId = null, decimal? minPrice = null, decimal? maxPrice = null)
    {
        db.SearchHistories.Add(new Courtly.Domain.Entities.SearchHistory
        {
            UserId = userId,
            SurfaceTypeId = surfaceId,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            CreatedAtUtc = Now.AddDays(-1),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A completed reservation + review by a distinct reviewer — pure popularity signal that does not touch
    /// the requesting user's profile.</summary>
    private static async Task SeedReviewByOtherAsync(CourtlyDbContext db, long courtId, int rating)
    {
        var reviewer = await SeedUserAsync(db);
        var slotId = await SeedSlotAsync(db, courtId, TimeOfDayBucket.Morning, Now.AddDays(-10));
        var res = new Reservation
        {
            CourtId = courtId,
            TimeSlotId = slotId,
            UserId = reviewer,
            Status = ReservationStatus.Completed,
            TotalPrice = 40m,
            CreatedAtUtc = Now.AddDays(-10),
        };
        db.Reservations.Add(res);
        await db.SaveChangesAsync();

        db.Reviews.Add(new Review
        {
            ReservationId = res.Id,
            CourtId = courtId,
            UserId = reviewer,
            Rating = rating,
            CreatedAtUtc = Now.AddDays(-9),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedMaintenanceAsync(CourtlyDbContext db, long courtId)
    {
        db.CourtMaintenanceLogs.Add(new CourtMaintenanceLog
        {
            CourtId = courtId,
            Status = MaintenanceStatus.InProgress,
            Reason = "Resurfacing",
            StartUtc = Now.AddDays(-1),
            EndUtc = null,
            CreatedAtUtc = Now.AddDays(-1),
        });
        await db.SaveChangesAsync();
    }

    private static List<long> CourtIds(RecommendationsResponse response) =>
        response.Page.Items.Select(i => i.Court.Id).ToList();

    // --- tests --------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_ranks_preferred_surface_above_others_with_surface_reason()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        var clayCourt = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");
        var grassCourt = await SeedCourtAsync(db, refs, refs.GrassId, refs.SinglesId, "Grass Court");
        // The user only ever plays clay.
        await SeedBookingAsync(db, clayCourt, user, TimeOfDayBucket.Afternoon);

        var response = await NewService(db, user).GetAsync(Page());

        var ids = CourtIds(response);
        Assert.Equal(clayCourt, ids.First());
        var top = response.Page.Items.First();
        Assert.Equal(RecommendationReason.Surface, top.ReasonCode);
        Assert.Contains("Clay", top.Reason);
        Assert.True(response.Summary.IsContentBased);
    }

    [Fact]
    public async Task Get_uses_time_bucket_reason_when_surface_does_not_match()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        // User books clay in the morning.
        var clayCourt = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");
        await SeedBookingAsync(db, clayCourt, user, TimeOfDayBucket.Morning);
        // A grass court (surface does NOT match) that has an upcoming morning slot.
        var grassCourt = await SeedCourtAsync(db, refs, refs.GrassId, refs.SinglesId, "Grass Court");
        await SeedSlotAsync(db, grassCourt, TimeOfDayBucket.Morning, Now.AddDays(1));

        var response = await NewService(db, user).GetAsync(Page());

        var grass = response.Page.Items.Single(i => i.Court.Id == grassCourt);
        Assert.Equal(RecommendationReason.TimeBucket, grass.ReasonCode);
        Assert.Contains("Morning", grass.Reason);
    }

    [Fact]
    public async Task Get_cold_start_ranks_by_popularity_and_is_not_content_based()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db); // no bookings, no searches
        var quiet = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Quiet Court");
        var popular = await SeedCourtAsync(db, refs, refs.GrassId, refs.SinglesId, "Popular Court");
        await SeedReviewByOtherAsync(db, popular, rating: 5);
        await SeedReviewByOtherAsync(db, popular, rating: 5);

        var response = await NewService(db, user).GetAsync(Page());

        Assert.False(response.Summary.IsContentBased);
        Assert.Equal(0, response.Summary.BasedOnBookings);
        Assert.Equal(popular, CourtIds(response).First());
        Assert.All(response.Page.Items, i =>
            Assert.True(i.ReasonCode is RecommendationReason.Popular or RecommendationReason.TopRated));
    }

    [Fact]
    public async Task Get_feedback_no_demotes_the_court_below_others_on_the_next_fetch()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        var clay = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");
        var grass = await SeedCourtAsync(db, refs, refs.GrassId, refs.SinglesId, "Grass Court");
        await SeedBookingAsync(db, clay, user, TimeOfDayBucket.Morning);
        var service = NewService(db, user);

        var before = CourtIds(await service.GetAsync(Page()));
        Assert.Equal(clay, before.First()); // clay ranks first (surface match)

        await service.RecordFeedbackAsync(new RecommendationFeedbackRequest(IsHelpful: false, new[] { clay }));
        var after = await service.GetAsync(Page());
        var afterIds = CourtIds(after);

        // Demoted, not removed: still present, now ranked last, and the non-disliked court leads.
        Assert.Contains(clay, afterIds);
        Assert.Equal(grass, afterIds.First());
        Assert.Equal(clay, afterIds.Last());
        Assert.Equal(before.Count, after.Page.TotalCount);
    }

    [Fact]
    public async Task Get_feedback_yes_keeps_the_court()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        var clay = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");
        await SeedBookingAsync(db, clay, user, TimeOfDayBucket.Morning);
        var service = NewService(db, user);

        await service.RecordFeedbackAsync(new RecommendationFeedbackRequest(IsHelpful: true, new[] { clay }));
        var after = await service.GetAsync(Page());

        Assert.Contains(clay, CourtIds(after));
    }

    [Fact]
    public async Task RecordFeedback_upserts_one_row_per_user_court_with_latest_value()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        var clay = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");
        var service = NewService(db, user);

        await service.RecordFeedbackAsync(new RecommendationFeedbackRequest(IsHelpful: false, new[] { clay }));
        await service.RecordFeedbackAsync(new RecommendationFeedbackRequest(IsHelpful: true, new[] { clay }));

        var rows = await db.RecommendationFeedbacks.Where(f => f.UserId == user && f.CourtId == clay).ToListAsync();
        Assert.Single(rows);
        Assert.True(rows[0].IsHelpful);
    }

    [Fact]
    public async Task Get_ranks_courts_closer_to_the_users_usual_price_higher()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        // The user books a $40 clay court, so their target price is ~40.
        var booked = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Booked $40", price: 40m);
        await SeedBookingAsync(db, booked, user, TimeOfDayBucket.Morning);
        // Two more clay courts (same surface weight) differing only in price.
        var near = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Near $45", price: 45m);
        var far = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Far $150", price: 150m);

        var ids = CourtIds(await NewService(db, user).GetAsync(Page()));

        Assert.True(ids.IndexOf(near) < ids.IndexOf(far));
    }

    [Fact]
    public async Task Get_excludes_courts_under_maintenance()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        var open = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Open Court");
        var broken = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Broken Court");
        await SeedMaintenanceAsync(db, broken);

        var ids = CourtIds(await NewService(db, user).GetAsync(Page()));

        Assert.Contains(open, ids);
        Assert.DoesNotContain(broken, ids);
    }

    [Fact]
    public async Task Get_excludes_inactive_courts()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        var active = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Active", isActive: true);
        var inactive = await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Inactive", isActive: false);

        var ids = CourtIds(await NewService(db, user).GetAsync(Page()));

        Assert.Contains(active, ids);
        Assert.DoesNotContain(inactive, ids);
    }

    [Fact]
    public async Task Get_with_no_reviews_or_reservations_produces_finite_scores_deterministically()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db); // cold start
        await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "A");
        await SeedCourtAsync(db, refs, refs.GrassId, refs.SinglesId, "B", isFeatured: true);

        var response = await NewService(db, user).GetAsync(Page());

        Assert.Equal(2, response.Page.TotalCount);
        Assert.All(response.Page.Items, i => Assert.True(double.IsFinite(i.Score)));
        // Featured court wins the tie when both have zero popularity.
        Assert.Equal("B", response.Page.Items.First().Court.Name);
    }

    [Fact]
    public async Task Get_summary_references_searches_when_the_user_has_only_searched()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");
        await SeedSearchAsync(db, user, surfaceId: refs.ClayId, minPrice: 20m, maxPrice: 60m);

        var response = await NewService(db, user).GetAsync(Page());

        Assert.True(response.Summary.IsContentBased);
        Assert.Equal(0, response.Summary.BasedOnBookings);
        Assert.Contains("searches", response.Summary.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_clamps_page_size_and_paginates_the_ranked_list()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var user = await SeedUserAsync(db);
        for (var i = 0; i < 3; i++)
        {
            await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, $"Court {i}");
        }
        var service = NewService(db, user);

        var clamped = await service.GetAsync(Page(size: 500));
        Assert.Equal(PaginationQuery.MaxPageSize, clamped.Page.PageSize);
        Assert.Equal(3, clamped.Page.TotalCount);

        var first = await service.GetAsync(Page(page: 1, size: 1));
        var second = await service.GetAsync(Page(page: 2, size: 1));
        Assert.Single(first.Page.Items);
        Assert.Single(second.Page.Items);
        Assert.NotEqual(first.Page.Items[0].Court.Id, second.Page.Items[0].Court.Id);
    }

    [Fact]
    public async Task Get_returns_empty_for_an_unauthenticated_caller()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        await SeedCourtAsync(db, refs, refs.ClayId, refs.SinglesId, "Clay Court");

        var response = await NewService(db, userId: null).GetAsync(Page());

        Assert.Empty(response.Page.Items);
        Assert.False(response.Summary.IsContentBased);
    }
}

/// <summary>Validation for the Yes/No feedback request (feature 29): at least one court, at most the list max.</summary>
public class RecommendationFeedbackRequestValidatorTests
{
    private readonly RecommendationFeedbackRequestValidator _validator = new();

    [Fact]
    public void Empty_court_ids_is_invalid()
    {
        var result = _validator.Validate(new RecommendationFeedbackRequest(IsHelpful: false, Array.Empty<long>()));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void More_than_the_max_court_ids_is_invalid()
    {
        var ids = Enumerable.Range(1, PaginationQuery.MaxPageSize + 1).Select(i => (long)i).ToArray();
        var result = _validator.Validate(new RecommendationFeedbackRequest(IsHelpful: true, ids));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_single_valid_court_id_is_valid()
    {
        var result = _validator.Validate(new RecommendationFeedbackRequest(IsHelpful: true, new long[] { 5 }));
        Assert.True(result.IsValid);
    }
}
