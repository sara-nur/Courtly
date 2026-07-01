using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Configurations;

public class RecommendationFeedbackConfiguration : IEntityTypeConfiguration<RecommendationFeedback>
{
    public void Configure(EntityTypeBuilder<RecommendationFeedback> builder)
    {
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Court)
            .WithMany()
            .HasForeignKey(x => x.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        // One feedback row per (user, court): the recommender upserts on Yes/No, and this index also serves the
        // per-user feedback read on every recommendation fetch. Unique so a double-tap can never create duplicate
        // rows (feature 29).
        builder.HasIndex(x => new { x.UserId, x.CourtId })
            .IsUnique()
            .HasDatabaseName("ix_recommendation_feedback_user_court");
    }
}
