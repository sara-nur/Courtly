using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Persistence;

/// <summary>
/// Model smoke tests (feature 3 DoD). They build the EF model entirely in-memory (no live DB) and assert the
/// rubric-critical shape: every entity is mapped, the double-booking guards exist, RESTRICT behaviors are
/// explicit, and PK types follow the Guid/long convention. Covers the raw bits the compiler cannot check
/// (filtered-index SQL string, per-FK DeleteBehavior).
/// </summary>
public class CourtlyModelTests
{
    // A well-formed but non-connectable connection string; the model builds lazily without opening a socket.
    private const string DummyConnectionString =
        "Host=localhost;Port=5432;Database=200067;Username=courtly;Password=dummy";

    private static CourtlyDbContext BuildContext()
    {
        var builder = new DbContextOptionsBuilder<CourtlyDbContext>();
        // Same configurer used by runtime DI and the design-time factory -> identical model.
        PersistenceServiceCollectionExtensions.ConfigureCourtlyDbContext(builder, DummyConnectionString);
        return new CourtlyDbContext(builder.Options);
    }

    [Fact]
    public void Model_Maps_All_Courtly_Entities()
    {
        using var ctx = BuildContext();
        var model = ctx.Model;

        Type[] entities =
        {
            typeof(AppUser), typeof(Country), typeof(City), typeof(SurfaceType), typeof(CourtType),
            typeof(Amenity), typeof(Court), typeof(CourtImage), typeof(CourtAmenity),
            typeof(CourtMaintenanceLog), typeof(TimeSlot), typeof(Reservation), typeof(ReservationAudit),
            typeof(Payment), typeof(Refund), typeof(Review), typeof(Notification),
            typeof(Courtly.Domain.Entities.News),
            typeof(Courtly.Domain.Entities.SearchHistory), typeof(RecommendationFeedback),
            typeof(RefreshToken), typeof(RevokedToken), typeof(PasswordResetToken),
        };

        foreach (var type in entities)
        {
            Assert.NotNull(model.FindEntityType(type));
        }
    }

    [Fact]
    public void Reservation_Has_Filtered_Unique_Active_Slot_Index()
    {
        using var ctx = BuildContext();
        var reservation = ctx.Model.FindEntityType(typeof(Reservation))!;

        var index = Assert.Single(
            reservation.GetIndexes(),
            i => i.GetDatabaseName() == "ux_reservations_active_timeslot");

        Assert.True(index.IsUnique);
        Assert.Equal("status IN (0, 1)", index.GetFilter());
        Assert.Equal(nameof(Reservation.TimeSlotId), Assert.Single(index.Properties).Name);
    }

    [Fact]
    public void Unique_Business_Guards_Are_Present()
    {
        using var ctx = BuildContext();
        var model = ctx.Model;

        var slot = model.FindEntityType(typeof(TimeSlot))!;
        Assert.Contains(slot.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(TimeSlot.CourtId), nameof(TimeSlot.StartUtc) }));

        var payment = model.FindEntityType(typeof(Payment))!;
        Assert.Contains(payment.GetIndexes(), i => i.IsUnique
            && i.Properties.Count == 1 && i.Properties[0].Name == nameof(Payment.ReservationId));

        var review = model.FindEntityType(typeof(Review))!;
        Assert.Contains(review.GetIndexes(), i => i.IsUnique
            && i.Properties.Count == 1 && i.Properties[0].Name == nameof(Review.ReservationId));
    }

    [Fact]
    public void Reservation_Foreign_Keys_Use_Restrict()
    {
        using var ctx = BuildContext();
        var reservation = ctx.Model.FindEntityType(typeof(Reservation))!;

        foreach (var fk in reservation.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void Primary_Key_Types_Follow_Guid_Long_Convention()
    {
        using var ctx = BuildContext();
        var model = ctx.Model;

        Assert.Equal(typeof(Guid), model.FindEntityType(typeof(AppUser))!.FindPrimaryKey()!.Properties[0].ClrType);
        Assert.Equal(typeof(long), model.FindEntityType(typeof(Court))!.FindPrimaryKey()!.Properties[0].ClrType);
        Assert.Equal(typeof(long), model.FindEntityType(typeof(Reservation))!.FindPrimaryKey()!.Properties[0].ClrType);
    }
}
