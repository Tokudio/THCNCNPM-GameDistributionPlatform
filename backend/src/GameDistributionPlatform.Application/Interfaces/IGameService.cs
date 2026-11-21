using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IGameService
{
    Task<PageResponse<GameSummaryResponse>> GetGamesAsync(GameQuery query, CancellationToken cancellationToken);
    Task<PageResponse<GameSummaryResponse>> GetAdminGamesAsync(AdminGameQuery query, CancellationToken cancellationToken);
    Task<GameResponse> GetByIdAsync(Guid id, bool includeUnpublished, CancellationToken cancellationToken);
    Task<GameResponse> CreateAsync(SaveGameRequest request, CancellationToken cancellationToken);
    Task<GameResponse> UpdateAsync(Guid id, SaveGameRequest request, CancellationToken cancellationToken);
    Task<GameResponse> UpdateStatusAsync(Guid id, UpdateGameStatusRequest request, CancellationToken cancellationToken);
}
