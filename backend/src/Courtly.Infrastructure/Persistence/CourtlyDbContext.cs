using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence.Conversions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Infrastructure.Persistence;

/// <summary>
/// EF Core context for Courtly. Extends <see cref="IdentityDbContext{TUser,TRole,TKey}"/> (ASP.NET Identity over
/// <see cref="AppUser"/> with <see cref="Guid"/> keys). Entity mappings live in one
/// <c>IEntityTypeConfiguration</c> per entity, applied via <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public class CourtlyDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public CourtlyDbContext(DbContextOptions<CourtlyDbContext> options) : base(options)
    {
    }

    // Reference data
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<SurfaceType> SurfaceTypes => Set<SurfaceType>();
    public DbSet<CourtType> CourtTypes => Set<CourtType>();
    public DbSet<Amenity> Amenities => Set<Amenity>();

    // Court aggregate
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<CourtImage> CourtImages => Set<CourtImage>();
    public DbSet<CourtAmenity> CourtAmenities => Set<CourtAmenity>();
    public DbSet<CourtMaintenanceLog> CourtMaintenanceLogs => Set<CourtMaintenanceLog>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();

    // Booking aggregate
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationAudit> ReservationAudits => Set<ReservationAudit>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Review> Reviews => Set<Review>();

    // User-owned
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<News> News => Set<News>();
    public DbSet<SearchHistory> SearchHistories => Set<SearchHistory>();
    public DbSet<RecommendationFeedback> RecommendationFeedbacks => Set<RecommendationFeedback>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity mappings first, then our per-entity configurations.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(CourtlyDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Force UTC on every DateTime (and DateTime?) so writes always map to timestamptz.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
