using GameDistributionPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameDistributionPlatform.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.AttemptNumber)
            .IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Payment_AttemptNumber", "[AttemptNumber] > 0");
            t.HasCheckConstraint("CK_Payment_Amount", "[Amount] >= 0");
        });

        builder.Property(p => p.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Status)
            .IsRequired();

        builder.Property(p => p.Method)
            .IsRequired();

        builder.Property(p => p.TransactionReference)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(p => p.FailureReason)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(p => p.CompletedAt)
            .IsRequired(false);

        // Khóa ngoại đến Order (Restrict)
        builder.HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique Composite Indexes
        builder.HasIndex(p => new { p.OrderId, p.AttemptNumber })
            .IsUnique();

        builder.HasIndex(p => new { p.OrderId, p.IdempotencyKey })
            .IsUnique();
    }
}
