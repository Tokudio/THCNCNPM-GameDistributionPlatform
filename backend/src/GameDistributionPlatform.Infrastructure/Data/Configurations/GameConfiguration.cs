using GameDistributionPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameDistributionPlatform.Infrastructure.Data.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(g => g.Slug)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(g => g.Slug)
            .IsUnique();

        builder.Property(g => g.Description)
            .IsRequired();

        builder.Property(g => g.Price)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(g => g.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(g => g.DiscountPercent)
            .IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Game_DiscountPercent", "[DiscountPercent] >= 0 AND [DiscountPercent] <= 100");
            t.HasCheckConstraint("CK_Game_Price", "[Price] >= 0");
        });

        builder.Property(g => g.Status)
            .IsRequired();

        builder.Property(g => g.Developer)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(g => g.Publisher)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(g => g.CoverImageUrl)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(g => g.BannerImageUrl)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(g => g.TrailerUrl)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(g => g.DemoDownloadUrl)
            .HasMaxLength(1000)
            .IsRequired(false);

        // Relationships
        // 1-N với GameMedia (Cascade)
        builder.HasMany(g => g.Media)
            .WithOne(m => m.Game)
            .HasForeignKey(m => m.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1-N với OrderItems (Restrict - không cho phép xóa Game đã có lịch sử đơn hàng)
        builder.HasMany(g => g.OrderItems)
            .WithOne(oi => oi.Game)
            .HasForeignKey(oi => oi.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1-N với UserLibraries (Restrict - không cho phép xóa Game đã có người sở hữu)
        builder.HasMany(g => g.UserLibraries)
            .WithOne(ul => ul.Game)
            .HasForeignKey(ul => ul.GameId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
