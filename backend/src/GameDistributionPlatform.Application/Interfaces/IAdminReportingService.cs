using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IAdminReportingService
{
    Task<DashboardSummaryResponse> GetDashboardAsync(CancellationToken cancellationToken);
    Task<PageResponse<AdminOrderSummaryResponse>> GetOrdersAsync(AdminOrderQuery query, CancellationToken cancellationToken);
    Task<AdminOrderDetailResponse> GetOrderAsync(Guid id, CancellationToken cancellationToken);
}
