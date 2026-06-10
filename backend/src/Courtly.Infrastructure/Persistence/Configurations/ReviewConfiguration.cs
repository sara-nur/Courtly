namespace Courtly.Infrastructure.Persistence.Configurations;

using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.HasOne(rv => rv.Reservation)
            .WithOne(r => r.Review)
            .HasForeignKey<Review>(rv => rv.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rv => rv.Court)
            .WithMany()
            .HasForeignKey(rv => rv.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rv => rv.User)
            .WithMany()
            .HasForeignKey(rv => rv.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(rv => rv.Comment)
            .HasMaxLength(2000);

        builder.HasIndex(rv => rv.CourtId)
            .HasDatabaseName("ix_reviews_court");
    }
}
