namespace Courtly.Infrastructure.Persistence.Configurations;

using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ReservationAuditConfiguration : IEntityTypeConfiguration<ReservationAudit>
{
    public void Configure(EntityTypeBuilder<ReservationAudit> builder)
    {
        builder.HasOne(a => a.Reservation)
            .WithMany(r => r.Audits)
            .HasForeignKey(a => a.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ChangedBy)
            .WithMany()
            .HasForeignKey(a => a.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.Reason)
            .HasMaxLength(500);
    }
}
