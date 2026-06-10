using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class CourtMaintenanceLogConfiguration : IEntityTypeConfiguration<CourtMaintenanceLog>
{
    public void Configure(EntityTypeBuilder<CourtMaintenanceLog> builder)
    {
        builder.Property(x => x.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasOne(x => x.Court)
            .WithMany(c => c.MaintenanceLogs)
            .HasForeignKey(x => x.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PerformedBy)
            .WithMany()
            .HasForeignKey(x => x.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
