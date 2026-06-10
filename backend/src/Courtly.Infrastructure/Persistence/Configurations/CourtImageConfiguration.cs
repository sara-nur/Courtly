using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class CourtImageConfiguration : IEntityTypeConfiguration<CourtImage>
{
    public void Configure(EntityTypeBuilder<CourtImage> builder)
    {
        builder.Property(x => x.Bytes)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Caption)
            .HasMaxLength(200);

        builder.Property(x => x.IsPrimary)
            .HasDefaultValue(false);

        builder.HasOne(x => x.Court)
            .WithMany(c => c.Images)
            .HasForeignKey(x => x.CourtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
