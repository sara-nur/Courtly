using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class CourtConfiguration : IEntityTypeConfiguration<Court>
{
    public void Configure(EntityTypeBuilder<Court> builder)
    {
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.HourlyPrice)
            .HasColumnType("numeric(10,2)");

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.IsFeatured)
            .HasDefaultValue(false);

        builder.HasOne(x => x.City)
            .WithMany()
            .HasForeignKey(x => x.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SurfaceType)
            .WithMany()
            .HasForeignKey(x => x.SurfaceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CourtType)
            .WithMany()
            .HasForeignKey(x => x.CourtTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
