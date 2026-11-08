using GameDistributionPlatform.Domain.Common;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Domain.Entities;

public class Game : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "VND";
    public int DiscountPercent { get; set; } = 0; // 0 - 100%
    public string? CoverImageUrl { get; set; }
    public string? BannerImageUrl { get; set; }
    public string? TrailerUrl { get; set; }
    public string? DemoDownloadUrl { get; set; }
    public GameStatus Status { get; set; } = GameStatus.Draft;
    public string? Publisher { get; set; }
    public string? Developer { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? MinimumRequirements { get; set; }
    public string? RecommendedRequirements { get; set; }

    // Navigation properties
    public ICollection<GameCategory> GameCategories { get; set; } = new List<GameCategory>();
    public ICollection<GameMedia> Media { get; set; } = new List<GameMedia>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();
}
