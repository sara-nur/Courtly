namespace Courtly.Infrastructure.Persistence.Configurations;

using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.HasOne(r => r.Payment)
            .WithMany(p => p.Refunds)
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.Amount)
            .HasColumnType("numeric(10,2)");

        builder.Property(r => r.ProviderRefundId)
            .HasMaxLength(200);

        builder.Property(r => r.Reason)
            .HasMaxLength(500);
    }
}
