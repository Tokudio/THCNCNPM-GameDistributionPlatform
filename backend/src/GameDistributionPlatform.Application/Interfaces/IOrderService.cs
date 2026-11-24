using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IOrderService
{
    Task<CreateOrderResult> CreateAsync(Guid userId, Guid gameId, CancellationToken cancellationToken);
    Task<OrderResponse> GetAsync(Guid userId, Guid orderId, CancellationToken cancellationToken);
    Task<PageResponse<OrderSummaryResponse>> GetMineAsync(Guid userId, OrderQuery query, CancellationToken cancellationToken);
    Task<MockPaymentResponse> CompleteMockPaymentAsync(
        Guid userId, Guid orderId, MockPaymentOutcome outcome, CancellationToken cancellationToken);
}

public sealed record CheckoutAvailability(bool MockPaymentAvailable);
