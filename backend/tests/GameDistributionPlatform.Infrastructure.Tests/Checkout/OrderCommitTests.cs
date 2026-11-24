using System.Data.Common;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
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

public sealed class OrderCommitTests
{
    private static readonly CheckoutAvailability Available = new(true);

    [Fact]
    public async Task ExceptionBeforeCommitRollsBackAllPaymentWrites()
    {
        using var fixture = new SqliteAuthTestDb();
        var (userId, orderId) = await SeedPendingOrderAsync(fixture);
        var fault = new CommitProbe(before: () => throw new InvalidOperationException("Test failure at commit"));
        await using var db = CreateInterceptedContext(fixture, fault);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderService(db, Available).CompleteMockPaymentAsync(userId, orderId, MockPaymentOutcome.Success, default));
        Assert.True(fault.ReachedCommit);
        await AssertPendingAsync(fixture);
    }

    [Fact]
    public async Task CancellationAfterSaveAndBeforeCommitRollsBackAllPaymentWrites()
    {
        using var fixture = new SqliteAuthTestDb();
        using var cancellation = new CancellationTokenSource();
        var (userId, orderId) = await SeedPendingOrderAsync(fixture);
        var fault = new CommitProbe(before: cancellation.Cancel);
        await using var db = CreateInterceptedContext(fixture, fault);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OrderService(db, Available).CompleteMockPaymentAsync(userId, orderId, MockPaymentOutcome.Success, cancellation.Token));
        Assert.True(fault.ReachedCommit);
        await AssertPendingAsync(fixture);
    }

    [Fact]
    public async Task LostResponseAfterCommitCanBeReadAndRetriedWithoutASecondPayment()
    {
        using var fixture = new SqliteAuthTestDb();
        var (userId, orderId) = await SeedPendingOrderAsync(fixture);
        var fault = new CommitProbe(after: () => throw new TimeoutException("Test loss of response after commit"));
        await using var db = CreateInterceptedContext(fixture, fault);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new OrderService(db, Available).CompleteMockPaymentAsync(userId, orderId, MockPaymentOutcome.Success, default));
        Assert.True(fault.ReachedCommit);
        await using var check = fixture.CreateContext();
        var reader = new OrderService(check, Available);
        var order = await reader.GetAsync(userId, orderId, default);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, order.PaymentStatus);
        Assert.True(order.OwnershipGranted);

        var retried = await reader.CompleteMockPaymentAsync(userId, orderId, MockPaymentOutcome.Success, default);
        Assert.Equal(PaymentStatus.Succeeded, retried.PaymentStatus);
        Assert.Single(await check.Payments.ToListAsync());
        Assert.Single(await check.UserLibraries.ToListAsync());
    }

    [Fact]
    public async Task FreeClaimRollsBackOrderAndItemIfOwnershipCannotBeSaved()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var (user, game) = await SeedGameAsync(db, price: 0);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER block_claim BEFORE INSERT ON UserLibraries BEGIN SELECT RAISE(ABORT, 'test claim fault'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() => new OrderService(db, Available).CreateAsync(user.Id, game.Id, default));
        await using var check = fixture.CreateContext();
        Assert.Empty(await check.Orders.ToListAsync());
        Assert.Empty(await check.OrderItems.ToListAsync());
        Assert.Empty(await check.UserLibraries.ToListAsync());
        Assert.Empty(await check.Payments.ToListAsync());
    }

    private static async Task<(Guid UserId, Guid OrderId)> SeedPendingOrderAsync(SqliteAuthTestDb fixture)
    {
        await using var db = fixture.CreateContext();
        var (user, game) = await SeedGameAsync(db, price: 10);
        var order = await new OrderService(db, Available).CreateAsync(user.Id, game.Id, default);
        return (user.Id, order.Order.Id);
    }

    private static async Task<(ApplicationUser User, Game Game)> SeedGameAsync(AppDbContext db, decimal price)
    {
        var user = new ApplicationUser();
        var game = new Game { Title = "Commit fixture", Slug = "commit-fixture", Price = price, Status = GameStatus.Published };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        return (user, game);
    }

    private static AppDbContext CreateInterceptedContext(SqliteAuthTestDb fixture, IInterceptor interceptor)
    {
        using var shared = fixture.CreateContext();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(shared.Database.GetDbConnection())
            .AddInterceptors(interceptor)
            .Options);
    }

    private static async Task AssertPendingAsync(SqliteAuthTestDb fixture)
    {
        await using var check = fixture.CreateContext();
        var order = await check.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.PaidAt);
        Assert.Empty(await check.Payments.ToListAsync());
        Assert.Empty(await check.UserLibraries.ToListAsync());
    }

    private sealed class CommitProbe(Action? before = null, Action? after = null) : DbTransactionInterceptor
    {
        public bool ReachedCommit { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            ReachedCommit = true;
            before?.Invoke();
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            after?.Invoke();
            return Task.CompletedTask;
        }
    }
}
