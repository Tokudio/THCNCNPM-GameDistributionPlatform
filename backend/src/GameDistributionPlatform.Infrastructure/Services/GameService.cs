using System.Data;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class GameService(AppDbContext db) : IGameService
{
    private const string DuplicateSlug = "This game slug is already in use.";

    public Task<PageResponse<GameSummaryResponse>> GetGamesAsync(GameQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, GameStatus.Published, false, cancellationToken);

    public Task<PageResponse<GameSummaryResponse>> GetAdminGamesAsync(AdminGameQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, query.Status, true, cancellationToken);

    private async Task<PageResponse<GameSummaryResponse>> GetPageAsync(
        GameQuery query, GameStatus? status, bool includeInactiveCategories, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(query);
        var games = db.Games.AsNoTracking();
        if (status is GameStatus filterStatus)
        {
            games = games.Where(game => game.Status == filterStatus);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            games = games.Where(game => game.Title.ToLower().Contains(search));
        }
        if (query.CategoryId is Guid categoryId)
        {
            games = games.Where(game => game.GameCategories.Any(link =>
                link.CategoryId == categoryId && (includeInactiveCategories || link.Category.IsActive)));
        }
        if (query.MinPrice is decimal minPrice)
        {
            games = games.Where(game => game.Price >= minPrice);
        }
        if (query.MaxPrice is decimal maxPrice)
        {
            games = games.Where(game => game.Price <= maxPrice);
        }

        var count = await games.CountAsync(cancellationToken);
        // SQLite stores EF Core decimal values as text and needs a numeric projection for ORDER BY.
        var sqlite = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
        var ordered = query.Sort switch
        {
            GameSort.Title => games.OrderBy(game => game.Title),
            GameSort.PriceAsc when sqlite => games.OrderBy(game => (double)game.Price),
            GameSort.PriceDesc when sqlite => games.OrderByDescending(game => (double)game.Price),
            GameSort.PriceAsc => games.OrderBy(game => game.Price),
            GameSort.PriceDesc => games.OrderByDescending(game => game.Price),
            _ => games.OrderByDescending(game => game.CreatedAt)
        };

        var items = await ordered.ThenBy(game => game.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(game => new GameSummaryResponse(
                game.Id, game.Title, game.Slug, game.Price, game.Currency, game.DiscountPercent,
                game.Status, game.CoverImageUrl, game.CreatedAt, game.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PageResponse<GameSummaryResponse>(query.PageNumber, query.PageSize, count,
            (int)Math.Ceiling((decimal)count / query.PageSize), items);
    }

    public async Task<GameResponse> GetByIdAsync(Guid id, bool includeUnpublished, CancellationToken cancellationToken)
    {
        var game = await WithRelations(db.Games.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == id && (includeUnpublished || item.Status == GameStatus.Published), cancellationToken)
            ?? throw new NotFoundException("Game was not found.");

        return ToResponse(game, includeUnpublished);
    }

    public async Task<GameResponse> CreateAsync(SaveGameRequest request, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(request);
        await EnsureUniqueSlugAsync(request.Slug, null, cancellationToken);
        var categories = await LoadCategoriesAsync(request.CategoryIds, cancellationToken);

        var game = new Game { Status = GameStatus.Draft };
        ApplyDetails(game, request);
        db.Games.Add(game);
        SyncCategories(game, categories);
        SyncMedia(game, request.Media);
        await CatalogStore.SaveAsync(db, DuplicateSlug, cancellationToken);
        return ToResponse(game, true);
    }

    public async Task<GameResponse> UpdateAsync(Guid id, SaveGameRequest request, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(request);
        // A PUT replaces both collections. Serialize the read and replacement to avoid mixing concurrent edits.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var game = await WithRelations(db.Games).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new NotFoundException("Game was not found.");
            await EnsureUniqueSlugAsync(request.Slug, id, cancellationToken);
            var categories = await LoadCategoriesAsync(request.CategoryIds, cancellationToken);

            ApplyDetails(game, request);
            SyncCategories(game, categories);
            SyncMedia(game, request.Media);
            game.UpdatedAt = DateTime.UtcNow;
            await CatalogStore.SaveAsync(db, DuplicateSlug, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(game, true);
        }
        catch (Exception error) when (CatalogStore.IsSqlError(error, 1205))
        {
            throw new ConflictException("The game changed during this edit. Reload and try again.");
        }
    }

    public async Task<GameResponse> UpdateStatusAsync(Guid id, UpdateGameStatusRequest request, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(request);
        var game = await WithRelations(db.Games).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Game was not found.");

        if (game.Status != request.Status!.Value)
        {
            game.Status = request.Status.Value;
            game.UpdatedAt = DateTime.UtcNow;
            await CatalogStore.SaveAsync(db, DuplicateSlug, cancellationToken);
        }

        return ToResponse(game, true);
    }

    private async Task EnsureUniqueSlugAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await db.Games.AnyAsync(item => item.Slug == slug && item.Id != exceptId, cancellationToken))
        {
            throw new ConflictException(DuplicateSlug);
        }
    }

    private async Task<List<Category>> LoadCategoriesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var categories = await db.Categories.Where(category => ids.Contains(category.Id)).ToListAsync(cancellationToken);
        if (categories.Count != ids.Count)
        {
            throw new BadRequestException("One or more selected categories do not exist.");
        }

        return categories;
    }

    private void SyncCategories(Game game, IReadOnlyList<Category> categories)
    {
        var selected = categories.Select(category => category.Id).ToHashSet();
        foreach (var link in game.GameCategories.Where(link => !selected.Contains(link.CategoryId)).ToArray())
        {
            game.GameCategories.Remove(link);
            db.GameCategories.Remove(link);
        }

        var existing = game.GameCategories.Select(link => link.CategoryId).ToHashSet();
        foreach (var category in categories.Where(category => !existing.Contains(category.Id)))
        {
            db.GameCategories.Add(new GameCategory { Game = game, GameId = game.Id, CategoryId = category.Id, Category = category });
        }
    }

    private void SyncMedia(Game game, IReadOnlyList<SaveGameMediaRequest> media)
    {
        var previous = game.Media.ToArray();
        var lookup = previous.ToLookup(row => MediaKey(row.Type, row.MediaUrl));
        var retained = new HashSet<Guid>();
        foreach (var item in media)
        {
            var existing = lookup[MediaKey(item.Type, item.MediaUrl)].FirstOrDefault();
            if (existing is not null)
            {
                retained.Add(existing.Id);
                existing.DisplayOrder = item.DisplayOrder;
                existing.MediaUrl = item.MediaUrl;
                continue;
            }

            db.GameMedia.Add(new GameMedia
            {
                Game = game,
                GameId = game.Id,
                MediaUrl = item.MediaUrl,
                Type = item.Type,
                DisplayOrder = item.DisplayOrder
            });
        }

        foreach (var row in previous.Where(item => !retained.Contains(item.Id)))
        {
            game.Media.Remove(row);
            db.GameMedia.Remove(row);
        }
    }

    private static (MediaType Type, string Url) MediaKey(MediaType type, string url) =>
        (type, CatalogInput.WebUri(url)?.AbsoluteUri ?? url);

    private static void ApplyDetails(Game game, SaveGameRequest request)
    {
        game.Title = request.Title.Trim();
        game.Slug = request.Slug;
        game.Description = request.Description.Trim();
        game.Price = request.Price!.Value;
        game.Currency = request.Currency;
        game.DiscountPercent = request.DiscountPercent;
        game.CoverImageUrl = request.CoverImageUrl;
        game.BannerImageUrl = request.BannerImageUrl;
        game.TrailerUrl = request.TrailerUrl;
        game.Developer = request.Developer?.Trim();
        game.Publisher = request.Publisher?.Trim();
        game.ReleaseDate = request.ReleaseDate;
        game.MinimumRequirements = request.MinimumRequirements?.Trim();
        game.RecommendedRequirements = request.RecommendedRequirements?.Trim();
    }

    private static IQueryable<Game> WithRelations(IQueryable<Game> games) =>
        games.Include(game => game.GameCategories).ThenInclude(link => link.Category)
            .Include(game => game.Media).AsSplitQuery();

    private static GameResponse ToResponse(Game game, bool includeInactiveCategories) =>
        new(game.Id, game.Title, game.Slug, game.Description, game.Price, game.Currency, game.DiscountPercent,
            game.Status, game.CoverImageUrl, game.BannerImageUrl, game.TrailerUrl, game.Publisher, game.Developer,
            game.ReleaseDate, game.MinimumRequirements, game.RecommendedRequirements, game.CreatedAt, game.UpdatedAt,
            game.GameCategories.Select(link => link.Category)
                .Where(category => includeInactiveCategories || category.IsActive)
                .OrderBy(category => category.Name).ThenBy(category => category.Id).Select(CategoryService.ToResponse).ToArray(),
            game.Media.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
                .Select(item => new GameMediaResponse(item.Id, item.MediaUrl, item.Type, item.DisplayOrder)).ToArray());
}
