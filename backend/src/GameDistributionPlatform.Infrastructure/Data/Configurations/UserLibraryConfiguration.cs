using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameDistributionPlatform.Infrastructure.Data.Configurations;

public class UserLibraryConfiguration : IEntityTypeConfiguration<UserLibrary>
{
    public void Configure(EntityTypeBuilder<UserLibrary> builder)
    {
        builder.HasKey(ul => ul.Id);

        builder.Property(ul => ul.AcquiredAt)
            .IsRequired();

        // Khóa ngoại UserId đến ApplicationUser (Restrict)
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(ul => ul.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Khóa ngoại GameId đến Game (Restrict)
        builder.HasOne(ul => ul.Game)
            .WithMany(g => g.UserLibraries)
            .HasForeignKey(ul => ul.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        // Khóa ngoại OrderItemId đến OrderItem (Restrict)
        builder.HasOne(ul => ul.OrderItem)
            .WithOne(oi => oi.UserLibrary)
            .HasForeignKey<UserLibrary>(ul => ul.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique Index cho OrderItemId (Mỗi OrderItem chỉ sinh đúng 1 bản ghi thư viện)
        builder.HasIndex(ul => ul.OrderItemId)
            .IsUnique();

        // Unique Composite Index (UserId, GameId) (Chống sở hữu trùng Game)
        builder.HasIndex(ul => new { ul.UserId, ul.GameId })
            .IsUnique();
    }
}

