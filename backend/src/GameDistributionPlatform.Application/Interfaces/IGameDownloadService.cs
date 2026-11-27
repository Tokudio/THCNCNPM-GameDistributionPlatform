using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IGameDownloadService
{
    Task<LibraryDownloadInfoResponse> GetInfoAsync(Guid userId, Guid gameId, CancellationToken cancellationToken);
    Task<GamePackage> OpenAsync(Guid userId, Guid gameId, CancellationToken cancellationToken);
}
