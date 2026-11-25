using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IUserLibraryService
{
    Task<PageResponse<LibraryGameResponse>> GetMyLibraryAsync(Guid userId, LibraryQuery query, CancellationToken cancellationToken);
    Task<LibraryGameDetailResponse> GetMyGameAsync(Guid userId, Guid gameId, CancellationToken cancellationToken);
}
