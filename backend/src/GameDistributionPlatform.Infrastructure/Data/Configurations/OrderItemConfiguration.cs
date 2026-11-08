using GameDistributionPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameDistributionPlatform.Infrastructure.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(oi => oi.Id);

        builder.Property(oi => oi.GameTitle)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(oi => oi.UnitPrice)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // Khóa ngoại đến Order (Restrict)
        builder.HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Khóa ngoại đến Game (Restrict)
        builder.HasOne(oi => oi.Game)
            .WithMany(g => g.OrderItems)
            .HasForeignKey(oi => oi.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chống trùng Game trong cùng một Order
        builder.HasIndex(oi => new { oi.OrderId, oi.GameId })
            .IsUnique();

        // Quan hệ 1 - 0..1 với UserLibrary thông qua OrderItemId (Restrict)
        builder.HasOne(oi => oi.UserLibrary)
            .WithOne(ul => ul.OrderItem)
            .HasForeignKey<UserLibrary>(ul => ul.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

