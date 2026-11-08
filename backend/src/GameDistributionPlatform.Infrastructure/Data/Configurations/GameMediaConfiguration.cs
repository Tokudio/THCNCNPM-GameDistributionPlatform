using GameDistributionPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameDistributionPlatform.Infrastructure.Data.Configurations;

public class GameMediaConfiguration : IEntityTypeConfiguration<GameMedia>
{
    public void Configure(EntityTypeBuilder<GameMedia> builder)
    {
        builder.HasKey(gm => gm.Id);

        builder.Property(gm => gm.MediaUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(gm => gm.Type)
            .IsRequired();

        builder.Property(gm => gm.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasOne(gm => gm.Game)
            .WithMany(g => g.Media)
            .HasForeignKey(gm => gm.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

