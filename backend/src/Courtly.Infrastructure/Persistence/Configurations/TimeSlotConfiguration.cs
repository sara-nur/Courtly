using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class TimeSlotConfiguration : IEntityTypeConfiguration<TimeSlot>
{
    public void Configure(EntityTypeBuilder<TimeSlot> builder)
    {
        builder.Property(x => x.Price)
            .HasColumnType("numeric(10,2)");

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.HasOne(x => x.Court)
            .WithMany(c => c.TimeSlots)
            .HasForeignKey(x => x.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.CourtId, x.StartUtc })
            .IsUnique()
            .HasDatabaseName("ux_time_slots_court_start");
    }
}
