using GameDistributionPlatform.Domain.Common;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentMethod Method { get; set; } = PaymentMethod.Mock;

    public int AttemptNumber { get; set; } = 1;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? CompletedAt { get; set; }
}
