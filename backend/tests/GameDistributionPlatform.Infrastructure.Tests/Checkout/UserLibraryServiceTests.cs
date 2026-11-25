using System.Data.Common;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Checkout;

public sealed class UserLibraryServiceTests
{
    private static readonly DateTime Acquired = new(2026, 11, 25, 0, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProjectsOnlyOwnedGamesWithStablePaginationSearchAndUtcDate()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var first = new ApplicationUser();
        var other = new ApplicationUser();
        db.AddRange(first, other);
        await db.SaveChangesAsync();

        var oldest = AddOwned(db, first.Id, 1, "Old Signal", Acquired.AddDays(-1));
        var left = AddOwned(db, first.Id, 2, "Signal Tower", Acquired, GameStatus.Archived);
        var right = AddOwned(db, first.Id, 3, "Signal Bridge", Acquired);
        var foreign = AddOwned(db, other.Id, 4, "Signal Outside", Acquired.AddDays(1));
        var active = new Category { Name = "Puzzle", Slug = "puzzle" };
        var inactive = new Category { Name = "Hidden", Slug = "hidden", IsActive = false };
        left.Game.GameCategories.Add(new GameCategory { Category = inactive });
        left.Game.GameCategories.Add(new GameCategory { Category = active });
        await db.SaveChangesAsync();

        await using var reader = fixture.CreateContext();
        var service = new UserLibraryService(reader);
        var page1 = await service.GetMyLibraryAsync(first.Id, new LibraryQuery { PageSize = 2 }, default);
        var page2 = await service.GetMyLibraryAsync(first.Id, new LibraryQuery { PageNumber = 2, PageSize = 2 }, default);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(new[] { left.GameId, right.GameId }, page1.Items.Select(item => item.GameId));
        Assert.Equal(oldest.GameId, Assert.Single(page2.Items).GameId);
        Assert.Equal(new[] { "Puzzle" }, page1.Items[0].Categories);
        Assert.Equal(Acquired, page1.Items[0].AcquiredAt);
        Assert.Equal(DateTimeKind.Utc, page1.Items[0].AcquiredAt.Kind);
        Assert.Equal(GameStatus.Archived, page1.Items[0].Status);
        Assert.True(page1.Items[0].IsOwned);

        var search = await service.GetMyLibraryAsync(first.Id, new LibraryQuery { Search = "  sIGnaL tOwEr " }, default);
        Assert.Equal(left.GameId, Assert.Single(search.Items).GameId);
        Assert.Empty((await service.GetMyLibraryAsync(first.Id, new LibraryQuery { Search = "Outside" }, default)).Items);
        Assert.Equal(foreign.GameId, Assert.Single((await service.GetMyLibraryAsync(other.Id, new LibraryQuery(), default)).Items).GameId);
        Assert.Empty((await service.GetMyLibraryAsync(Guid.NewGuid(), new LibraryQuery(), default)).Items);
        Assert.Empty(reader.ChangeTracker.Entries());
        Assert.Equal(4, await reader.UserLibraries.CountAsync());
    }

    [Fact]
    public async Task LoadsArchivedDetailInOneQueryAndRejectsOtherOwnership()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var game = AddOwned(db, user.Id, 1, "Archived Game", Acquired, GameStatus.Archived);
        game.Game.Developer = "Fixture studio";
        game.Game.MinimumRequirements = "8 GB RAM";
        await db.SaveChangesAsync();

        var counter = new QueryCounter();
        await using var reader = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetDbConnection()).AddInterceptors(counter).Options);
        var service = new UserLibraryService(reader);
        var result = await service.GetMyGameAsync(user.Id, game.GameId, default);
        Assert.Equal(GameStatus.Archived, result.Status);
        Assert.Equal("Fixture studio", result.Developer);
        Assert.Equal("8 GB RAM", result.MinimumRequirements);
        Assert.True(result.IsOwned);
        Assert.Equal(1, counter.Count);
        await service.GetMyLibraryAsync(user.Id, new LibraryQuery(), default);
        Assert.Equal(3, counter.Count);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetMyGameAsync(Guid.NewGuid(), game.GameId, default));
        Assert.Empty(reader.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SkipsOrphanedMetadataWithoutDroppingOtherGamesOrOwnership()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var missing = AddOwned(db, user.Id, 1, "Imported orphan", Acquired);
        var valid = AddOwned(db, user.Id, 2, "Valid game", Acquired);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Games WHERE Id = {missing.GameId}");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
        }

        var service = new UserLibraryService(db);
        var result = await service.GetMyLibraryAsync(user.Id, new LibraryQuery(), default);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(valid.GameId, Assert.Single(result.Items).GameId);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetMyGameAsync(user.Id, missing.GameId, default));
        Assert.Equal(2, await db.UserLibraries.CountAsync());
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(1_000_001, 12)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task RejectsInvalidPagination(int page, int size)
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var service = new UserLibraryService(db);
        await Assert.ThrowsAsync<BadRequestException>(() => service.GetMyLibraryAsync(Guid.NewGuid(),
            new LibraryQuery { PageNumber = page, PageSize = size }, default));
    }

    private static UserLibrary AddOwned(AppDbContext db, Guid userId, int number, string title, DateTime acquiredAt,
        GameStatus status = GameStatus.Published)
    {
        var game = new Game { Id = Guid.Parse($"10000000-0000-4000-8000-{number:D12}"), Title = title, Slug = $"fixture-{number}", Status = status };
        var order = new Order { UserId = userId, OrderCode = $"fixture-{number}", Status = OrderStatus.Paid };
        var item = new OrderItem { Game = game, GameTitle = title };
        order.Items.Add(item);
        var ownership = new UserLibrary { UserId = userId, Game = game, OrderItem = item, AcquiredAt = acquiredAt };
        item.UserLibrary = ownership;
        db.Orders.Add(order);
        return ownership;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
