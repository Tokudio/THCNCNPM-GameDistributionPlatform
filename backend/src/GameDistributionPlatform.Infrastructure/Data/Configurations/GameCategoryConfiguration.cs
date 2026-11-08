using GameDistributionPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameDistributionPlatform.Infrastructure.Data.Configurations;

public class GameCategoryConfiguration : IEntityTypeConfiguration<GameCategory>
{
    public void Configure(EntityTypeBuilder<GameCategory> builder)
    {
        // Khóa chính kết hợp (Composite Key)
        builder.HasKey(gc => new { gc.GameId, gc.CategoryId });

        builder.HasOne(gc => gc.Game)
            .WithMany(g => g.GameCategories)
            .HasForeignKey(gc => gc.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(gc => gc.Category)
            .WithMany(c => c.GameCategories)
            .HasForeignKey(gc => gc.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

