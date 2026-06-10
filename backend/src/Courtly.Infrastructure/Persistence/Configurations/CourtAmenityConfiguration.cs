using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class CourtAmenityConfiguration : IEntityTypeConfiguration<CourtAmenity>
{
    public void Configure(EntityTypeBuilder<CourtAmenity> builder)
    {
        builder.Property(x => x.Note)
            .HasMaxLength(300);

        builder.Property(x => x.IsHighlighted)
            .HasDefaultValue(false);

        builder.HasOne(x => x.Court)
            .WithMany(c => c.Amenities)
            .HasForeignKey(x => x.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Amenity)
            .WithMany()
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CourtId, x.AmenityId })
            .IsUnique()
            .HasDatabaseName("ux_court_amenities_court_amenity");
    }
}
