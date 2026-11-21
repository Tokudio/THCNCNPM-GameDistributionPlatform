using System.Data.Common;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Catalog;

public sealed class CatalogServiceTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task CategoryCanBeCreatedUpdatedHiddenAndDeleted()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var categories = new CategoryService(db);

        var created = await categories.CreateAsync(CategoryRequest("  Tactics  ", "tactics"), Ct);
        Assert.Equal("Tactics", created.Name);
        Assert.True(created.IsActive);
        var updated = await categories.UpdateAsync(created.Id, CategoryRequest("Strategy", "tactics", false), Ct);
        Assert.Equal("Strategy", updated.Name);
        Assert.Empty(await categories.GetAllAsync(false, Ct));
        Assert.Single(await categories.GetAllAsync(true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => categories.GetByIdAsync(created.Id, false, Ct));

        await categories.DeleteAsync(created.Id, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => categories.GetByIdAsync(created.Id, true, Ct));
    }

    [Fact]
    public async Task CategoryConflictsAreBasedOnSlugAndCheckUpdates()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var categories = new CategoryService(db);
        var first = await categories.CreateAsync(CategoryRequest("Same name", "first"), Ct);
        var second = await categories.CreateAsync(CategoryRequest("Same name", "second"), Ct);

        await Assert.ThrowsAsync<ConflictException>(() => categories.CreateAsync(CategoryRequest("Other name", first.Slug), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => categories.UpdateAsync(second.Id, CategoryRequest("Other name", first.Slug), Ct));
        Assert.Equal(2, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task SeededCategoryCanBeDeactivatedAndRetainsItsSlug()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var seeded = SeedConstants.DefaultCategories[0];
        db.Categories.Add(seeded);
        await db.SaveChangesAsync();
        var categories = new CategoryService(db);

        await Assert.ThrowsAsync<ConflictException>(() => categories.DeleteAsync(seeded.Id, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => categories.UpdateAsync(seeded.Id, CategoryRequest(seeded.Name, "new-slug"), Ct));
        Assert.False((await categories.UpdateAsync(seeded.Id, CategoryRequest(seeded.Name, seeded.Slug, false), Ct)).IsActive);
    }

    [Fact]
    public async Task AssignedCategoryCannotBeDeletedAndGameAndLinkRemain()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var categories = new CategoryService(db);
        var category = await categories.CreateAsync(CategoryRequest("Action", "new-action"), Ct);
        await new GameService(db).CreateAsync(GameRequest([category.Id]), Ct);

        await Assert.ThrowsAsync<ConflictException>(() => categories.DeleteAsync(category.Id, Ct));
        Assert.Equal(1, await db.Categories.CountAsync());
        Assert.Equal(1, await db.Games.CountAsync());
        Assert.Equal(1, await db.GameCategories.CountAsync());
    }

    [Fact]
    public async Task GameUpdateReplacesLinksAndMediaAndPreservesUnrelatedData()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var categories = new CategoryService(db);
        var oldCategory = await categories.CreateAsync(CategoryRequest("First", "first"), Ct);
        var newCategory = await categories.CreateAsync(CategoryRequest("Second", "second"), Ct);
        var gameService = new GameService(db);
        var draft = await gameService.CreateAsync(GameRequest([oldCategory.Id],
            [Image("https://example.com/old.webp", 3), Image("https://example.com/kept.webp", 2)]), Ct);
        Assert.Equal(GameStatus.Draft, draft.Status);
        Assert.Equal(oldCategory.Id, Assert.Single(draft.Categories).Id);
        var keptMedia = draft.Media.Single(item => item.MediaUrl.EndsWith("kept.webp"));
        var stored = await db.Games.SingleAsync();
        stored.DemoDownloadUrl = "https://example.com/demo";
        await db.SaveChangesAsync();

        var changed = await gameService.UpdateAsync(draft.Id, GameRequest([newCategory.Id],
            [Image("https://example.com/kept.webp", 0), Image("https://example.com/new.webp", 1)], title: "Updated"), Ct);
        Assert.Equal("Updated", changed.Title);
        Assert.Equal(draft.CreatedAt, changed.CreatedAt);
        Assert.NotNull(changed.UpdatedAt);
        Assert.Equal(newCategory.Id, Assert.Single(changed.Categories).Id);
        Assert.Equal(keptMedia.Id, changed.Media[0].Id);
        Assert.Equal([0, 1], changed.Media.Select(item => item.DisplayOrder).ToArray());
        await using var verifier = fixture.CreateContext();
        Assert.Equal(2, await verifier.Categories.CountAsync());
        Assert.Equal(newCategory.Id, (await verifier.GameCategories.SingleAsync()).CategoryId);
        Assert.Equal(2, await verifier.GameMedia.CountAsync());
        Assert.Equal("https://example.com/demo", (await verifier.Games.SingleAsync()).DemoDownloadUrl);
    }

    [Fact]
    public async Task GameStatusControlsPublicVisibilityAndHidesInactiveCategoryMetadata()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var category = await new CategoryService(db).CreateAsync(CategoryRequest("Hidden", "hidden", false), Ct);
        var games = new GameService(db);
        var draft = await games.CreateAsync(GameRequest([category.Id]), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => games.GetByIdAsync(draft.Id, false, Ct));

        await games.UpdateStatusAsync(draft.Id, new UpdateGameStatusRequest { Status = GameStatus.Published }, Ct);
        Assert.Empty((await games.GetByIdAsync(draft.Id, false, Ct)).Categories);
        Assert.Single((await games.GetGamesAsync(new GameQuery(), Ct)).Items);
        Assert.Empty((await games.GetGamesAsync(new GameQuery { CategoryId = category.Id }, Ct)).Items);

        await games.UpdateStatusAsync(draft.Id, new UpdateGameStatusRequest { Status = GameStatus.Archived }, Ct);
        Assert.Empty((await games.GetGamesAsync(new GameQuery(), Ct)).Items);
        Assert.Single((await games.GetAdminGamesAsync(new AdminGameQuery { Status = GameStatus.Archived, CategoryId = category.Id }, Ct)).Items);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            games.UpdateStatusAsync(draft.Id, new UpdateGameStatusRequest { Status = (GameStatus)100 }, Ct));
    }

    [Fact]
    public async Task InvalidCategoriesAndInvalidUpdateLeaveNoPartialChanges()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var games = new GameService(db);
        var missing = Guid.NewGuid();
        await Assert.ThrowsAsync<BadRequestException>(() => games.CreateAsync(GameRequest([missing]), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => games.CreateAsync(GameRequest([missing, missing]), Ct));
        var game = await games.CreateAsync(GameRequest(), Ct);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            games.UpdateAsync(game.Id, GameRequest([missing], [Image("https://example.com/new", 0)], title: "Unexpected update"), Ct));
        Assert.Equal(game.Title, (await db.Games.SingleAsync()).Title);
        Assert.Empty(await db.GameMedia.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaWriteFailureRollsBackTheGameAndItsRelationships(bool update)
    {
        using var fixture = new SqliteAuthTestDb();
        await using var setup = fixture.CreateContext();
        var categories = new CategoryService(setup);
        var originalCategory = await categories.CreateAsync(CategoryRequest("Original", "original"), Ct);
        var replacementCategory = await categories.CreateAsync(CategoryRequest("Replacement", "replacement"), Ct);
        var original = update
            ? await new GameService(setup).CreateAsync(GameRequest([originalCategory.Id],
                [Image("https://example.com/original.png", 0)]), Ct)
            : null;

        var failure = new FailingMediaInsert();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(setup.Database.GetDbConnection()).AddInterceptors(failure).Options;
        await using (var db = new AppDbContext(options))
        {
            var games = new GameService(db);
            var request = GameRequest([replacementCategory.Id], [Image("https://example.com/replacement.png", 1)], title: "Replacement game");
            var error = await Record.ExceptionAsync(() => original is null
                ? games.CreateAsync(request, Ct)
                : games.UpdateAsync(original.Id, request, Ct));
            Assert.NotNull(error);
            Assert.Same(failure.Error, error.GetBaseException());
            Assert.True(failure.MediaInsertExecuted);
        }

        await using var verifier = fixture.CreateContext();
        if (original is null)
        {
            Assert.Empty(await verifier.Games.ToListAsync());
            Assert.Empty(await verifier.GameCategories.ToListAsync());
            Assert.Empty(await verifier.GameMedia.ToListAsync());
        }
        else
        {
            var persisted = await new GameService(verifier).GetByIdAsync(original.Id, true, Ct);
            Assert.Equal(original.Title, persisted.Title);
            Assert.Equal(originalCategory.Id, Assert.Single(persisted.Categories).Id);
            Assert.Equal(Assert.Single(original.Media), Assert.Single(persisted.Media));
        }
    }

    [Fact]
    public async Task DuplicateGameSlugsAreRejectedForCreateAndUpdate()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var games = new GameService(db);
        var first = await games.CreateAsync(GameRequest(slug: "first"), Ct);
        var second = await games.CreateAsync(GameRequest(slug: "second"), Ct);
        await Assert.ThrowsAsync<ConflictException>(() => games.CreateAsync(GameRequest(slug: first.Slug), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => games.UpdateAsync(second.Id, GameRequest(slug: first.Slug), Ct));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("4.321")]
    [InlineData("10000000000000000")]
    public async Task PriceMustFitTheDatabasePrecisionAndRange(string invalidPrice)
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var price = decimal.Parse(invalidPrice, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.ThrowsAsync<BadRequestException>(() => new GameService(db).CreateAsync(GameRequest(price: price), Ct));
        Assert.Equal(0, await db.Games.CountAsync());
    }

    [Fact]
    public async Task MalformedAndDuplicateMediaFailValidation()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var games = new GameService(db);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            games.CreateAsync(GameRequest(media: [Image("file:///game.png", 0)]), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            games.CreateAsync(GameRequest(media: [Image("https://example.com/a.png", 0), Image("https://EXAMPLE.com/a.png", 1)]), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            games.CreateAsync(GameRequest(media: [new SaveGameMediaRequest { MediaUrl = "https://example.com/a", Type = (MediaType)123 }]), Ct));
    }

    [Fact]
    public async Task QueriesSearchSortAndPageWithDecimalRanges()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var games = new GameService(db);
        await games.CreateAsync(GameRequest(slug: "cheap", price: 4.5m, title: "Arcade One"), Ct);
        await games.CreateAsync(GameRequest(slug: "second", price: 19.5m, title: "Arcade Two"), Ct);
        await games.CreateAsync(GameRequest(slug: "high", price: 22.25m, title: "Arcade Three"), Ct);
        await games.CreateAsync(GameRequest(slug: "different", price: 22m, title: "Racing"), Ct);

        var result = await games.GetAdminGamesAsync(new AdminGameQuery
        {
            Search = "ARCADE", MinPrice = 10m, MaxPrice = 23m, Sort = GameSort.PriceDesc, PageSize = 1, PageNumber = 2
        }, Ct);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal("second", Assert.Single(result.Items).Slug);
    }

    [Fact]
    public async Task UnexpectedPersistenceErrorsPropagate()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var connectionOwner = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionOwner.Database.GetDbConnection()).Options;
        await using var db = new FailingSaveDb(options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CategoryService(db).CreateAsync(CategoryRequest("Broken", "broken"), Ct));
        Assert.Equal("Simulated persistence failure", error.Message);
        Assert.Equal(0, await connectionOwner.Categories.CountAsync());
    }

    [Fact]
    public async Task SqliteModelEnforcesUniqueGameSlugAndCategoryForeignKeys()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        await new GameService(db).CreateAsync(GameRequest(), Ct);
        db.Games.Add(new Game { Title = "Duplicate", Slug = "test-game", Description = "Test" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var id = await db.Games.Select(item => item.Id).SingleAsync();
        db.GameCategories.Add(new GameCategory { GameId = id, CategoryId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static SaveCategoryRequest CategoryRequest(string name, string slug, bool active = true) =>
        new() { Name = name, Slug = slug, IsActive = active };

    private static SaveGameRequest GameRequest(Guid[]? categories = null, SaveGameMediaRequest[]? media = null,
        string slug = "test-game", decimal? price = 12.34m, string title = "Test Game") =>
        new() { Title = title, Slug = slug, Description = "Test description", Price = price, CategoryIds = categories ?? [], Media = media ?? [] };

    private static SaveGameMediaRequest Image(string url, int order) => new() { MediaUrl = url, DisplayOrder = order };

    private sealed class FailingMediaInsert : DbCommandInterceptor
    {
        public InvalidOperationException Error { get; } = new("Simulated failure after inserting new media");
        public bool MediaInsertExecuted { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"GameMedia\"", StringComparison.Ordinal))
            {
                // Step the reader so SQLite completes the write before simulating failure.
                MediaInsertExecuted = await result.ReadAsync(cancellationToken) || result.RecordsAffected == 1;
                await result.DisposeAsync();
                throw Error;
            }

            return result;
        }
    }

    private sealed class FailingSaveDb(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated persistence failure");
    }
}
