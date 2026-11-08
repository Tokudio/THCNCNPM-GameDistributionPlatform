using GameDistributionPlatform.Domain.Common;

namespace GameDistributionPlatform.Domain.Entities;

public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation property
    public ICollection<GameCategory> GameCategories { get; set; } = new List<GameCategory>();
}
