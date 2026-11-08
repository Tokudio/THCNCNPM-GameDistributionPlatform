using GameDistributionPlatform.Domain.Common;

namespace GameDistributionPlatform.Domain.Entities;

public class UserLibrary : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid GameId { get; set; }
    public Game Game { get; set; } = null!;

    public Guid OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
}
