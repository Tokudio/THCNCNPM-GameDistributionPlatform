using GameDistributionPlatform.Domain.Common;

namespace GameDistributionPlatform.Domain.Entities;

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid GameId { get; set; }
    public Game Game { get; set; } = null!;

    // Snapshot at purchase time
    public string GameTitle { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; } // VND

    // Navigation property
    public UserLibrary? UserLibrary { get; set; }
}
