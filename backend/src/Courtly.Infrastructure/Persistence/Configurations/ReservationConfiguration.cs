namespace Courtly.Infrastructure.Persistence.Configurations;

using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Court)
            .WithMany()
            .HasForeignKey(r => r.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.TimeSlot)
            .WithMany()
            .HasForeignKey(r => r.TimeSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.TotalPrice)
            .HasColumnType("numeric(10,2)");

        builder.Property(r => r.Status)
            .HasDefaultValue(ReservationStatus.Pending);

        builder.Property(r => r.CancellationReason)
            .HasMaxLength(500);

        builder.HasIndex(r => r.TimeSlotId)
            .IsUnique()
            .HasFilter("status IN (0, 1)")
            .HasDatabaseName("ux_reservations_active_timeslot");

        builder.HasIndex(r => new { r.UserId, r.Status })
            .HasDatabaseName("ix_reservations_user_status");

        builder.HasIndex(r => new { r.CourtId, r.Status })
            .HasDatabaseName("ix_reservations_court_status");
    }
}
