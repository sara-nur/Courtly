namespace Courtly.Infrastructure.Persistence.Configurations;

using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasOne(p => p.Reservation)
            .WithOne(r => r.Payment)
            .HasForeignKey<Payment>(p => p.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.Amount)
            .HasColumnType("numeric(10,2)");

        builder.Property(p => p.ProviderPaymentIntentId)
            .HasMaxLength(200);

        builder.Property(p => p.IdempotencyKey)
            .HasMaxLength(200);
    }
}
