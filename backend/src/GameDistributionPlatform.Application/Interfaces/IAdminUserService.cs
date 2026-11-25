using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IAdminUserService
{
    Task<PageResponse<AdminUserSummaryResponse>> GetUsersAsync(AdminUserQuery query, CancellationToken cancellationToken);
    Task<AdminUserDetailResponse> GetUserAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateStatusAsync(Guid actorId, Guid id, bool isActive, CancellationToken cancellationToken);
}
