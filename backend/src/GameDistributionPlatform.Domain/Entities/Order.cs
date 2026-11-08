using GameDistributionPlatform.Domain.Common;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Domain.Entities;

public class Order : BaseEntity
{
    public string OrderCode { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime? PaidAt { get; set; }

    // Navigation properties
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
