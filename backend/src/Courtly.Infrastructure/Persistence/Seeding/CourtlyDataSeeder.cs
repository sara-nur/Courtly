using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Infrastructure.Persistence.Seeding;

/// <summary>
/// Idempotent runtime seeder for the dynamic demo data. Runs after migrations (which apply the HasData reference
/// rows), so it can reference surfaces/cities/etc. by their fixed <see cref="SeedIds"/>. Each block is guarded by
/// an <c>AnyAsync</c> existence check, so re-running on every startup never duplicates rows.
/// </summary>
public sealed class CourtlyDataSeeder : ICourtlyDataSeeder
{
    /// <summary>Shared password for every seeded account (paired with the usernames in the README credentials table).</summary>
    private const string SeedPassword = "test";

    // Slot horizon: hourly slots from 08:00 to 20:00, for a window centred on today so past bookings are realistic.
    private const int SlotWindowDaysBack = 7;
    private const int SlotWindowDaysAhead = 7;
    private const int SlotFirstHour = 8;
    private const int SlotLastHour = 19; // last slot starts 19:00, ends 20:00

    private readonly CourtlyDbContext _db;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly SeedImageLoader _images;
    private readonly ILogger<CourtlyDataSeeder> _logger;

    public CourtlyDataSeeder(
        CourtlyDbContext db,
        IPasswordHasher<AppUser> passwordHasher,
        SeedImageLoader images,
        ILogger<CourtlyDataSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _images = images;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        _logger.LogInformation("Running Courtly data seeder.");

        await SeedUsersAsync(now, cancellationToken);
        await SeedCourtsAsync(now, cancellationToken);
        await SeedTimeSlotsAsync(now, cancellationToken);
        await SeedReservationsAsync(now, cancellationToken);
        await SeedNewsAsync(now, cancellationToken);

        _logger.LogInformation("Courtly data seeder finished.");
    }

    private async Task SeedUsersAsync(DateTime now, CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct))
        {
            return;
        }

        var admin = BuildUser(SeedIds.UserAdmin, "desktop", "admin@courtly.local", "Super", "Admin", SeedIds.CitySarajevo, "avatar-admin.png", now);
        var staff = BuildUser(SeedIds.UserStaff, "staff", "staff@courtly.local", "Sami", "Staff", SeedIds.CityMostar, "avatar-staff.png", now);
        var mobile = BuildUser(SeedIds.UserMobile, "mobile", "mobile@courtly.local", "Mira", "Mobile", SeedIds.CitySarajevo, "avatar-user.png", now);
        var emma = BuildUser(SeedIds.UserEmma, "emma", "emma@courtly.local", "Emma", "Kovac", SeedIds.CityTuzla, "avatar-emma.png", now);

        _db.Users.AddRange(admin, staff, mobile, emma);
        _db.Set<IdentityUserRole<Guid>>().AddRange(
            new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = SeedIds.RoleAdmin },
            new IdentityUserRole<Guid> { UserId = staff.Id, RoleId = SeedIds.RoleStaff },
            new IdentityUserRole<Guid> { UserId = mobile.Id, RoleId = SeedIds.RoleUser },
            new IdentityUserRole<Guid> { UserId = emma.Id, RoleId = SeedIds.RoleUser });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} users with roles.", 4);
    }

    private AppUser BuildUser(Guid id, string userName, string email, string first, string last, long cityId, string avatarFile, DateTime now)
    {
        var avatar = _images.Load(avatarFile);
        var user = new AppUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            // Deterministic stamps keep reseeds stable; SecurityStamp is required for Identity sign-in (feature 5).
            SecurityStamp = id.ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = id.ToString(),
            LockoutEnabled = false,
            FirstName = first,
            LastName = last,
            CityId = cityId,
            IsActive = true,
            AvatarBytes = avatar.Bytes,
            AvatarContentType = avatar.ContentType,
            CreatedAtUtc = now,
        };
        // Same PBKDF2 hasher ASP.NET Identity validates against, so seeded credentials log in unchanged.
        user.PasswordHash = _passwordHasher.HashPassword(user, SeedPassword);
        return user;
    }

    private async Task SeedCourtsAsync(DateTime now, CancellationToken ct)
    {
        if (await _db.Courts.AnyAsync(ct))
        {
            return;
        }

        var courts = new[]
        {
            BuildCourt("Center Court", "Flagship clay court with floodlights.", SeedIds.SurfaceClay, SeedIds.CourtTypeSingles, SeedIds.CitySarajevo,
                indoor: false, featured: true, active: true, price: 30.00m, 43.8563, 18.4131, "court-clay-01.png",
                new[] { SeedIds.AmenityLights, SeedIds.AmenityShowers, SeedIds.AmenityParking }),
            BuildCourt("Wimbledon Lawn", "Natural grass court, fast surface.", SeedIds.SurfaceGrass, SeedIds.CourtTypeSingles, SeedIds.CitySarajevo,
                indoor: false, featured: true, active: true, price: 35.00m, 43.8520, 18.4080, "court-grass-01.png",
                new[] { SeedIds.AmenityShowers, SeedIds.AmenityLocker, SeedIds.AmenityParking }),
            BuildCourt("Riverside Hard", "Outdoor hard court by the river.", SeedIds.SurfaceHard, SeedIds.CourtTypeDoubles, SeedIds.CityMostar,
                indoor: false, featured: false, active: true, price: 25.00m, 43.3438, 17.8078, "court-hard-01.png",
                new[] { SeedIds.AmenityLights, SeedIds.AmenityParking }),
            BuildCourt("Highland Hard", "Hard court with mountain views.", SeedIds.SurfaceHard, SeedIds.CourtTypeSingles, SeedIds.CityTuzla,
                indoor: false, featured: false, active: true, price: 25.00m, 44.5384, 18.6671, "court-hard-02.png",
                new[] { SeedIds.AmenityLights, SeedIds.AmenityLocker }),
            BuildCourt("Indoor Arena", "Climate-controlled indoor carpet court.", SeedIds.SurfaceCarpet, SeedIds.CourtTypeMulti, SeedIds.CitySarajevo,
                indoor: true, featured: true, active: true, price: 40.00m, 43.8490, 18.3560, "court-indoor-01.png",
                new[] { SeedIds.AmenityLights, SeedIds.AmenityShowers, SeedIds.AmenityLocker, SeedIds.AmenityParking, SeedIds.AmenityProShop }),
            BuildCourt("Practice Court", "Coaching court (currently under maintenance).", SeedIds.SurfaceClay, SeedIds.CourtTypeTraining, SeedIds.CityMostar,
                indoor: false, featured: false, active: true, price: 18.00m, 43.3400, 17.8120, "court-maintenance-01.png",
                new[] { SeedIds.AmenityParking }),
        };

        _db.Courts.AddRange(courts);
        await _db.SaveChangesAsync(ct);

        // Last court is in maintenance: an open (InProgress) window marks it unavailable for booking/analytics (feature 12/13).
        var maintenanceCourt = courts[^1];
        _db.CourtMaintenanceLogs.Add(new CourtMaintenanceLog
        {
            CourtId = maintenanceCourt.Id,
            Status = MaintenanceStatus.InProgress,
            Reason = "Resurfacing in progress.",
            StartUtc = now.AddDays(-2),
            EndUtc = null,
            PerformedByUserId = SeedIds.UserStaff,
            CreatedAtUtc = now.AddDays(-2),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} courts (1 in maintenance) with images and amenities.", courts.Length);
    }

    private Court BuildCourt(string name, string description, long surfaceTypeId, long courtTypeId, long cityId,
        bool indoor, bool featured, bool active, decimal price, double lat, double lng, string imageFile, long[] amenityIds)
    {
        var image = _images.Load(imageFile);
        var court = new Court
        {
            Name = name,
            Description = description,
            SurfaceTypeId = surfaceTypeId,
            CourtTypeId = courtTypeId,
            CityId = cityId,
            IsIndoor = indoor,
            IsFeatured = featured,
            IsActive = active,
            HourlyPrice = price,
            Latitude = lat,
            Longitude = lng,
        };
        court.Images.Add(new CourtImage { Bytes = image.Bytes, ContentType = image.ContentType, IsPrimary = true, Caption = name });
        foreach (var amenityId in amenityIds)
        {
            court.Amenities.Add(new CourtAmenity { AmenityId = amenityId, IsHighlighted = amenityId == SeedIds.AmenityLights });
        }

        return court;
    }

    private async Task SeedTimeSlotsAsync(DateTime now, CancellationToken ct)
    {
        if (await _db.TimeSlots.AnyAsync(ct))
        {
            return;
        }

        var courts = await _db.Courts.Include(c => c.MaintenanceLogs).ToListAsync(ct);
        var bookable = courts
            .Where(c => c.IsActive && !c.MaintenanceLogs.Any(m =>
                m.Status == MaintenanceStatus.Scheduled || m.Status == MaintenanceStatus.InProgress))
            .ToList();

        var anchor = now.Date;
        var slots = new List<TimeSlot>();
        foreach (var court in bookable)
        {
            for (var dayOffset = -SlotWindowDaysBack; dayOffset <= SlotWindowDaysAhead; dayOffset++)
            {
                for (var hour = SlotFirstHour; hour <= SlotLastHour; hour++)
                {
                    var start = anchor.AddDays(dayOffset).AddHours(hour);
                    var bucket = BucketFor(hour);
                    slots.Add(new TimeSlot
                    {
                        CourtId = court.Id,
                        StartUtc = start,
                        EndUtc = start.AddHours(1),
                        // Server-owned price: a modest evening premium on the court's hourly rate.
                        Price = decimal.Round(court.HourlyPrice * (bucket == TimeOfDayBucket.Evening ? 1.2m : 1.0m), 2),
                        Bucket = bucket,
                        IsActive = true,
                    });
                }
            }
        }

        _db.TimeSlots.AddRange(slots);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} time slots across {Courts} bookable courts.", slots.Count, bookable.Count);
    }

    private static TimeOfDayBucket BucketFor(int hour) => hour switch
    {
        < 12 => TimeOfDayBucket.Morning,
        < 17 => TimeOfDayBucket.Afternoon,
        _ => TimeOfDayBucket.Evening,
    };

    private async Task SeedReservationsAsync(DateTime now, CancellationToken ct)
    {
        if (await _db.Reservations.AnyAsync(ct))
        {
            return;
        }

        var mobile = await _db.Users.FirstAsync(u => u.UserName == "mobile", ct);
        var emma = await _db.Users.FirstAsync(u => u.UserName == "emma", ct);

        var center = await _db.Courts.FirstAsync(c => c.Name == "Center Court", ct);
        var lawn = await _db.Courts.FirstAsync(c => c.Name == "Wimbledon Lawn", ct);
        var riverside = await _db.Courts.FirstAsync(c => c.Name == "Riverside Hard", ct);
        var highland = await _db.Courts.FirstAsync(c => c.Name == "Highland Hard", ct);
        var arena = await _db.Courts.FirstAsync(c => c.Name == "Indoor Arena", ct);

        // R1 — Completed (paid + reviewed): mobile, Center Court, 3 days ago.
        var r1Slot = await SlotAsync(center.Id, -3, 18, now, ct);
        var r1 = NewReservation(mobile.Id, center.Id, r1Slot, ReservationStatus.Completed, now.AddDays(-3).AddHours(-3));
        AddAudit(r1, null, ReservationStatus.Pending, null, mobile.Id, r1.CreatedAtUtc);
        AddAudit(r1, ReservationStatus.Pending, ReservationStatus.Confirmed, null, mobile.Id, r1.CreatedAtUtc.AddMinutes(3));
        AddAudit(r1, ReservationStatus.Confirmed, ReservationStatus.Completed, null, null, r1Slot.EndUtc);
        r1.Payment = NewSucceededPayment(r1, r1.CreatedAtUtc.AddMinutes(3));
        r1.Review = new Review
        {
            CourtId = center.Id,
            UserId = mobile.Id,
            Rating = 5,
            Comment = "Great clay court, perfectly maintained.",
            CreatedAtUtc = r1Slot.EndUtc.AddHours(1),
        };

        // R2 — Completed (paid + reviewed): emma, Riverside Hard, 5 days ago.
        var r2Slot = await SlotAsync(riverside.Id, -5, 10, now, ct);
        var r2 = NewReservation(emma.Id, riverside.Id, r2Slot, ReservationStatus.Completed, now.AddDays(-5).AddHours(-2));
        AddAudit(r2, null, ReservationStatus.Pending, null, emma.Id, r2.CreatedAtUtc);
        AddAudit(r2, ReservationStatus.Pending, ReservationStatus.Confirmed, null, emma.Id, r2.CreatedAtUtc.AddMinutes(5));
        AddAudit(r2, ReservationStatus.Confirmed, ReservationStatus.Completed, null, null, r2Slot.EndUtc);
        r2.Payment = NewSucceededPayment(r2, r2.CreatedAtUtc.AddMinutes(5));
        r2.Review = new Review
        {
            CourtId = riverside.Id,
            UserId = emma.Id,
            Rating = 4,
            Comment = "Solid hard court, nice riverside setting.",
            CreatedAtUtc = r2Slot.EndUtc.AddHours(2),
        };

        // R3 — Confirmed (paid, upcoming): mobile, Wimbledon Lawn, in 2 days.
        var r3Slot = await SlotAsync(lawn.Id, 2, 17, now, ct);
        var r3 = NewReservation(mobile.Id, lawn.Id, r3Slot, ReservationStatus.Confirmed, now.AddHours(-2));
        AddAudit(r3, null, ReservationStatus.Pending, null, mobile.Id, r3.CreatedAtUtc);
        AddAudit(r3, ReservationStatus.Pending, ReservationStatus.Confirmed, null, mobile.Id, r3.CreatedAtUtc.AddMinutes(1));
        r3.Payment = NewSucceededPayment(r3, r3.CreatedAtUtc.AddMinutes(1));

        // R4 — Pending (unpaid hold, upcoming): emma, Indoor Arena, in 3 days.
        var r4Slot = await SlotAsync(arena.Id, 3, 19, now, ct);
        var r4 = NewReservation(emma.Id, arena.Id, r4Slot, ReservationStatus.Pending, now.AddMinutes(-3));
        r4.HoldExpiresAtUtc = now.AddMinutes(12);
        AddAudit(r4, null, ReservationStatus.Pending, null, emma.Id, r4.CreatedAtUtc);

        // R5 — Cancelled (unpaid): mobile, Highland Hard, 4 days ago.
        var r5Slot = await SlotAsync(highland.Id, -4, 9, now, ct);
        var r5 = NewReservation(mobile.Id, highland.Id, r5Slot, ReservationStatus.Cancelled, now.AddDays(-4).AddHours(-1));
        r5.CancelledAtUtc = r5.CreatedAtUtc.AddMinutes(20);
        r5.CancellationReason = "Changed plans.";
        AddAudit(r5, null, ReservationStatus.Pending, null, mobile.Id, r5.CreatedAtUtc);
        AddAudit(r5, ReservationStatus.Pending, ReservationStatus.Cancelled, r5.CancellationReason, mobile.Id, r5.CancelledAtUtc.Value);

        _db.Reservations.AddRange(r1, r2, r3, r4, r5);

        _db.Notifications.AddRange(
            new Notification
            {
                UserId = mobile.Id,
                Type = NotificationType.ReservationCompleted,
                Title = "Booking completed",
                Text = "Your booking at Center Court is complete — leave a review!",
                IsRead = true,
                CreatedAtUtc = r1Slot.EndUtc,
                ReadAtUtc = r1Slot.EndUtc.AddHours(2),
            },
            new Notification
            {
                UserId = mobile.Id,
                Type = NotificationType.ReservationConfirmed,
                Title = "Booking confirmed",
                Text = "Your booking at Wimbledon Lawn is confirmed.",
                IsRead = false,
                CreatedAtUtc = now.AddHours(-2),
            },
            new Notification
            {
                UserId = emma.Id,
                Type = NotificationType.General,
                Title = "Welcome to Courtly",
                Text = "Find and book a court near you.",
                IsRead = false,
                CreatedAtUtc = now.AddDays(-1),
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded 5 reservations (with audits, payments, reviews) and 3 notifications.");
    }

    private async Task<TimeSlot> SlotAsync(long courtId, int dayOffset, int hour, DateTime now, CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(now.Date.AddDays(dayOffset).AddHours(hour), DateTimeKind.Utc);
        var end = start.AddHours(1);
        return await _db.TimeSlots.FirstAsync(s => s.CourtId == courtId && s.StartUtc >= start && s.StartUtc < end, ct);
    }

    private static Reservation NewReservation(Guid userId, long courtId, TimeSlot slot, ReservationStatus status, DateTime createdAt) => new()
    {
        UserId = userId,
        CourtId = courtId,
        TimeSlotId = slot.Id,
        Status = status,
        TotalPrice = slot.Price,
        CreatedAtUtc = createdAt,
    };

    private static void AddAudit(Reservation reservation, ReservationStatus? oldStatus, ReservationStatus newStatus, string? reason, Guid? changedBy, DateTime at) =>
        reservation.Audits.Add(new ReservationAudit
        {
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            ChangedByUserId = changedBy,
            CreatedAtUtc = at,
        });

    private static Payment NewSucceededPayment(Reservation reservation, DateTime paidAt) => new()
    {
        Status = PaymentStatus.Succeeded,
        Amount = reservation.TotalPrice,
        AmountChargedCents = (long)decimal.Round(reservation.TotalPrice * 100m),
        ProviderPaymentIntentId = $"pi_seed_{Guid.NewGuid():N}",
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = paidAt,
        PaidAtUtc = paidAt,
    };

    private async Task SeedNewsAsync(DateTime now, CancellationToken ct)
    {
        if (await _db.News.AnyAsync(ct))
        {
            return;
        }

        var admin = await _db.Users.FirstAsync(u => u.UserName == "desktop", ct);
        var tournament = _images.Load("news-tournament.png");
        var resurfacing = _images.Load("news-resurfacing.png");

        _db.News.AddRange(
            new News
            {
                Title = "Summer Open 2026 announced",
                Text = "Registration is now open for the Courtly Summer Open. Book early to secure your spot.",
                ImageBytes = tournament.Bytes,
                ImageContentType = tournament.ContentType,
                PublishedAtUtc = now.AddDays(-3),
                IsActive = true,
                AuthorId = admin.Id,
            },
            new News
            {
                Title = "Practice Court resurfacing",
                Text = "The Practice Court is temporarily closed for resurfacing and will reopen shortly.",
                ImageBytes = resurfacing.Bytes,
                ImageContentType = resurfacing.ContentType,
                PublishedAtUtc = now.AddDays(-1),
                IsActive = true,
                AuthorId = admin.Id,
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded 2 news articles.");
    }
}
