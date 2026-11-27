using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class GameDownloadService(AppDbContext db, IGamePackageStorage storage) : IGameDownloadService
{
    private const string Unavailable = "Game này chưa có tệp tải xuống.";

    public async Task<LibraryDownloadInfoResponse> GetInfoAsync(Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(userId, gameId, cancellationToken);
        var package = storage.Open(gameId);
        if (package is null) return new(gameId, false, null, null, Unavailable);

        await using var content = package.Content;
        return new(gameId, true, package.FileName, content.Length, null);
    }

    public async Task<GamePackage> OpenAsync(Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(userId, gameId, cancellationToken);
        return storage.Open(gameId)
            ?? throw new NotFoundException(Unavailable) { Code = "game_package_unavailable" };
    }

    private async Task EnsureOwnedAsync(Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        // Quyền tải dựa trên sở hữu đã ghi nhận, kể cả khi game đã chuyển sang Archived.
        if (!await db.UserLibraries.AnyAsync(item => item.UserId == userId && item.GameId == gameId, cancellationToken))
            throw new NotFoundException("Không tìm thấy game trong thư viện của bạn.") { Code = "library_game_not_found" };
    }
}
