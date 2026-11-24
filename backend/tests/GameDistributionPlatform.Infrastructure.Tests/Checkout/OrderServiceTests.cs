using System.Globalization;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Application.Orders;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Checkout;

public sealed class OrderServiceTests
{
    [Theory]
    [InlineData("19.99", 0, "19.99")]
    [InlineData("459000", 20, "367200")]
    [InlineData("0.03", 50, "0.02")]
    [InlineData("0.01", 50, "0.01")]
    [InlineData("119.95", 100, "0")]
    [InlineData("0", 0, "0")]
    public async Task CalculatesExactSnapshotAndClaimsOnlyZeroTotals(string price, int discount, string expected)
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser();
        var game = new Game
        {
            Title = "Fixture game", Slug = "fixture-game", Price = Parse(price),
            DiscountPercent = discount, Status = GameStatus.Published
        };
        db.AddRange(user, game);
        await db.SaveChangesAsync();

        var service = new OrderService(db, new CheckoutAvailability(true));
        var order = (await service.CreateAsync(user.Id, game.Id, default)).Order;
        var total = Parse(expected);
        Assert.Equal(game.Price, order.OriginalAmount);
        Assert.Equal(game.Price, Assert.Single(order.Items).UnitPrice);
        Assert.Equal(total, order.TotalAmount);
        Assert.Equal(game.Price - total, order.DiscountAmount);
        Assert.Equal(total == 0 ? OrderStatus.Paid : OrderStatus.Pending, order.Status);
        Assert.Equal(total == 0, order.OwnershipGranted);
        Assert.Equal(total == 0 ? 1 : 0, await db.UserLibraries.CountAsync());
        Assert.Empty(db.Payments);
    }

    [Theory]
    [InlineData("9999999999999999.99", 0, "9999999999999999.99")]
    [InlineData("9999999999999999.99", 1, "9899999999999999.99")]
    public void PricingRemainsExactAtSqlServerDecimalLimit(string price, int discount, string expected)
    {
        // SQLite's numeric affinity loses cents at these magnitudes, so test the decimal rule before persistence.
        Assert.Equal(Parse(expected), OrderPricing.Total(Parse(price), discount));
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("OrderItems")]
    public async Task OrderAndOrderItemRollBackIfInsertFails(string table)
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser();
        var game = new Game { Title = "Fixture", Slug = "fixture", Price = 10, Status = GameStatus.Published };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        if (table == "Orders")
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER block_order BEFORE INSERT ON Orders BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        else
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER block_item BEFORE INSERT ON OrderItems BEGIN SELECT RAISE(ABORT, 'test failure'); END;");

        var service = new OrderService(db, new CheckoutAvailability(true));
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(user.Id, game.Id, default));
        Assert.Empty(db.Orders);
        Assert.Empty(db.OrderItems);
        Assert.Empty(db.UserLibraries);
    }

    [Theory]
    [InlineData("Payments")]
    [InlineData("UserLibraries")]
    [InlineData("Orders")]
    public async Task PaymentOrderAndOwnershipRollBackIfAnyWriteFails(string table)
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser();
        var game = new Game { Title = "Fixture", Slug = "fixture", Price = 10, Status = GameStatus.Published };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var service = new OrderService(db, new CheckoutAvailability(true));
        var order = (await service.CreateAsync(user.Id, game.Id, default)).Order;

        switch (table)
        {
            case "Payments":
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER block_payment BEFORE INSERT ON Payments BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
                break;
            case "UserLibraries":
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER block_ownership BEFORE INSERT ON UserLibraries BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
                break;
            default:
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER block_paid BEFORE UPDATE ON Orders BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
                break;
        }

        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CompleteMockPaymentAsync(user.Id, order.Id, MockPaymentOutcome.Success, default));
        var persisted = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatus.Pending, persisted.Status);
        Assert.Null(persisted.PaidAt);
        Assert.Empty(db.Payments);
        Assert.Empty(db.UserLibraries);
    }

    [Fact]
    public async Task ArchivedGameBlocksSuccessAndLeavesOrderPending()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser();
        var game = new Game { Title = "Fixture", Slug = "fixture", Price = 10, Status = GameStatus.Published };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var service = new OrderService(db, new CheckoutAvailability(true));
        var order = (await service.CreateAsync(user.Id, game.Id, default)).Order;
        game.Status = GameStatus.Archived;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => service.CompleteMockPaymentAsync(user.Id, order.Id, MockPaymentOutcome.Success, default));
        Assert.Equal(OrderStatus.Pending, (await service.GetAsync(user.Id, order.Id, default)).Status);
        Assert.Empty(db.Payments);
        Assert.Empty(db.UserLibraries);
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
