using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class UserLibraryService(AppDbContext db) : IUserLibraryService
{
    public async Task<PageResponse<LibraryGameResponse>> GetMyLibraryAsync(
        Guid userId, LibraryQuery query, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(query);
        var games = OwnedGames(userId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var title = query.Search.Trim().ToLowerInvariant();
            games = games.Where(row => row.Game.Title.ToLower().Contains(title));
        }

        var count = await games.CountAsync(cancellationToken);
        var items = await games.OrderByDescending(row => row.Ownership.AcquiredAt).ThenBy(row => row.Ownership.GameId)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(row => new LibraryGameResponse(row.Game.Id, row.Game.Title, row.Game.Slug, row.Game.CoverImageUrl,
                row.Game.Status, DateTime.SpecifyKind(row.Ownership.AcquiredAt, DateTimeKind.Utc),
                row.Game.GameCategories.Where(link => link.Category.IsActive)
                    .OrderBy(link => link.Category.Name).ThenBy(link => link.CategoryId).Select(link => link.Category.Name).ToArray()))
            .AsSingleQuery().ToListAsync(cancellationToken);
        return new PageResponse<LibraryGameResponse>(query.PageNumber, query.PageSize, count,
            (int)Math.Ceiling((decimal)count / query.PageSize), items);
    }

    public async Task<LibraryGameDetailResponse> GetMyGameAsync(
        Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        return await OwnedGames(userId).Where(row => row.Ownership.GameId == gameId)
            .Select(row => new LibraryGameDetailResponse(row.Game.Id, row.Game.Title, row.Game.Slug, row.Game.CoverImageUrl,
                row.Game.Status, DateTime.SpecifyKind(row.Ownership.AcquiredAt, DateTimeKind.Utc),
                row.Game.GameCategories.Where(link => link.Category.IsActive)
                    .OrderBy(link => link.Category.Name).ThenBy(link => link.CategoryId).Select(link => link.Category.Name).ToArray(),
                row.Game.BannerImageUrl, row.Game.Description, row.Game.Developer, row.Game.Publisher,
                row.Game.ReleaseDate, row.Game.MinimumRequirements, row.Game.RecommendedRequirements))
            .AsSingleQuery().SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Game was not found in your library.") { Code = "library_game_not_found" };
    }

    // The inner join keeps orphaned metadata out of both results and counts without changing entitlements.
    private IQueryable<OwnedGame> OwnedGames(Guid userId) =>
        from ownership in db.UserLibraries.AsNoTracking()
        where ownership.UserId == userId
        join game in db.Games.AsNoTracking() on ownership.GameId equals game.Id
        select new OwnedGame { Ownership = ownership, Game = game };

    private sealed class OwnedGame
    {
        public required UserLibrary Ownership { get; init; }
        public required Game Game { get; init; }
    }
}
