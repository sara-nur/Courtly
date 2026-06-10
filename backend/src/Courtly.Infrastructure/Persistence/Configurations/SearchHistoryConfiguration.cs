using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class SearchHistoryConfiguration : IEntityTypeConfiguration<SearchHistory>
{
    public void Configure(EntityTypeBuilder<SearchHistory> builder)
    {
        builder.Property(x => x.MinPrice).HasColumnType("numeric(10,2)");
        builder.Property(x => x.MaxPrice).HasColumnType("numeric(10,2)");
        builder.Property(x => x.RawQuery).HasMaxLength(300);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SurfaceType)
            .WithMany()
            .HasForeignKey(x => x.SurfaceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CourtType)
            .WithMany()
            .HasForeignKey(x => x.CourtTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc })
            .HasDatabaseName("ix_search_histories_user_created");
    }
}
