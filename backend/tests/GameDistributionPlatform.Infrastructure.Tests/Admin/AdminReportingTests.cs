using System.Data;
using System.Data.Common;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
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

namespace GameDistributionPlatform.Infrastructure.Tests.Admin;

public sealed class AdminReportingTests
{
    [Fact]
    public async Task EmptyDashboardReturnsZerosAndNoRevenue()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var result = await new AdminReportingService(db).GetDashboardAsync(default);
        Assert.Equal(new DashboardGameCounts(0, 0, 0, 0), result.Games);
        Assert.Equal(new DashboardUserCounts(0, 0, 0), result.Users);
        Assert.Equal(new DashboardOrderCounts(0, 0, 0, 0), result.Orders);
        Assert.Equal(0, result.TotalCategories);
        Assert.Empty(result.Revenue);
        Assert.Empty(result.DailyRevenue);
        Assert.Empty(result.RecentOrders);
        Assert.Equal(0, result.UnverifiedPaidOrders);
        Assert.Equal(0, result.RevenueOrdersMissingPaidAt);
    }

    [Fact]
    public async Task DashboardCountsRealStatusesAndOnePaidSnapshotPerOrderInItsCurrency()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Người mua" };
        var paidAt = DateTime.UtcNow.Date.AddDays(-2).AddHours(23);
        // Giá trị này vượt giới hạn số nguyên an toàn của JavaScript và SQLite vẫn biểu diễn chính xác.
        const decimal large = 9_007_199_254_740_992m;
        var paid = Order(user.Id, "PAID", OrderStatus.Paid, large, paidAt, PaymentStatus.Succeeded, PaymentStatus.Succeeded);
        var old = Order(user.Id, "OLD", OrderStatus.Paid, 1.10m, paidAt.AddDays(-15), PaymentStatus.Succeeded);
        var foreign = Order(user.Id, "USD", OrderStatus.Paid, 22.33m, paidAt, PaymentStatus.Succeeded);
        foreign.Currency = "USD";
        Assert.Single(foreign.Payments).Currency = foreign.Currency;
        db.AddRange(user, new ApplicationUser { IsActive = false }, new Category { Name = "Hành động", Slug = "action" });
        db.AddRange(Enum.GetValues<GameStatus>().Select(status => new Game { Title = status.ToString(), Slug = status.ToString(), Status = status }));
        db.AddRange(paid, old, foreign, Order(user.Id, "FREE", OrderStatus.Paid, 0, paidAt),
            Order(user.Id, "WAIT", OrderStatus.Pending, 99, null, PaymentStatus.Pending),
            Order(user.Id, "CANCEL", OrderStatus.Cancelled, 100, null, PaymentStatus.Failed),
            Order(user.Id, "NO-SUCCESS", OrderStatus.Paid, 150, paidAt, PaymentStatus.Failed));
        await db.SaveChangesAsync();

        await using var reader = fixture.CreateContext();
        var result = await new AdminReportingService(reader).GetDashboardAsync(default);
        Assert.Equal(new DashboardGameCounts(3, 1, 1, 1), result.Games);
        Assert.Equal(new DashboardUserCounts(2, 1, 1), result.Users);
        Assert.Equal(new DashboardOrderCounts(7, 1, 5, 1), result.Orders);
        Assert.Equal(1, result.TotalCategories);
        Assert.Equal([new AdminRevenueResponse("USD", 22.33m), new AdminRevenueResponse("VND", large + 1.10m)], result.Revenue);
        Assert.Equal(large, Assert.Single(result.DailyRevenue, item => item.Currency == "VND").Amount);
        Assert.All(result.DailyRevenue, item => Assert.Equal(DateOnly.FromDateTime(paidAt), item.Date));
        Assert.Equal(5, result.RecentOrders.Count);
        Assert.Empty(reader.ChangeTracker.Entries());
    }

    [Fact]
    public async Task FiltersAndPaginatesOrdersAndReadsStoredDetailSnapshots()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var customer = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Lan", Email = "lan@example.test" };
        var game = new Game { Title = "Tên game hiện tại", Slug = "game", Price = 9999 };
        var nextDay = DateTime.UtcNow.Date.AddDays(-1);
        var earlier = Order(customer.Id, "ORD-EARLY", OrderStatus.Paid, 199, nextDay.AddDays(-1), PaymentStatus.Failed, PaymentStatus.Succeeded);
        earlier.CreatedAt = nextDay.AddDays(-1);
        earlier.Items.Add(new OrderItem { Game = game, GameTitle = "Tên game khi đặt", UnitPrice = 250 });
        var newer = Order(customer.Id, "ORD-LATE", OrderStatus.Paid, 300, nextDay, PaymentStatus.Succeeded);
        newer.CreatedAt = nextDay;
        db.AddRange(customer, earlier, newer, Order(customer.Id, "ORD-FAILED", OrderStatus.Cancelled, 99, null, PaymentStatus.Failed));
        await db.SaveChangesAsync();

        var service = new AdminReportingService(db);
        var page = await service.GetOrdersAsync(new AdminOrderQuery
        {
            Search = " LAN@EXAMPLE.TEST ", Status = OrderStatus.Paid, PaymentStatus = PaymentStatus.Succeeded,
            OldestFirst = true, PageSize = 1, PageNumber = 2
        }, default);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(newer.Id, Assert.Single(page.Items).Id);
        var found = await service.GetOrderAsync(earlier.Id, default);
        Assert.Equal("Lan", found.Order.CustomerName);
        Assert.Equal("lan@example.test", found.Order.CustomerEmail);
        Assert.Equal(DateTimeKind.Utc, found.Order.CreatedAt.Kind);
        Assert.Equal("Tên game khi đặt", Assert.Single(found.Items).GameTitle);
        Assert.Equal(250m, found.OriginalAmount);
        Assert.Equal(51m, found.DiscountAmount);
        Assert.Equal([PaymentStatus.Succeeded, PaymentStatus.Failed], found.Payments.Select(item => item.Status));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetOrderAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task CountsOnlySettledAmountsAndUsesInclusiveStartAndExclusiveEndInUtc()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var customer = new ApplicationUser { Id = Guid.NewGuid() };
        var today = DateTime.UtcNow.Date;
        var start = today.AddDays(-6);
        var end = today.AddDays(1);
        var badAmount = Order(customer.Id, "AMOUNT", OrderStatus.Paid, 80, today, PaymentStatus.Succeeded);
        Assert.Single(badAmount.Payments).Amount = 75;
        var badCurrency = Order(customer.Id, "CURRENCY", OrderStatus.Paid, 70, today, PaymentStatus.Succeeded);
        Assert.Single(badCurrency.Payments).Currency = "USD";
        db.AddRange(customer, badAmount, badCurrency,
            Order(customer.Id, "MISSING-DATE", OrderStatus.Paid, 50, null, PaymentStatus.Succeeded),
            Order(customer.Id, "BEFORE", OrderStatus.Paid, 5, start.AddSeconds(-1), PaymentStatus.Succeeded),
            Order(customer.Id, "FIRST", OrderStatus.Paid, 10, start, PaymentStatus.Succeeded),
            Order(customer.Id, "LAST", OrderStatus.Paid, 20, end.AddSeconds(-1), PaymentStatus.Succeeded),
            Order(customer.Id, "AFTER", OrderStatus.Paid, 25, end, PaymentStatus.Succeeded));
        await db.SaveChangesAsync();

        var report = await new AdminReportingService(db).GetDashboardAsync(default);
        Assert.Equal(7, report.Orders.Paid);
        Assert.Equal(110, Assert.Single(report.Revenue).Amount);
        Assert.Equal(2, report.UnverifiedPaidOrders);
        Assert.Equal(1, report.RevenueOrdersMissingPaidAt);
        Assert.Equal(DateOnly.FromDateTime(start), report.RevenueStartDate);
        Assert.Equal([
            new AdminDailyRevenueResponse(DateOnly.FromDateTime(start), "VND", 10),
            new AdminDailyRevenueResponse(DateOnly.FromDateTime(today), "VND", 20)
        ], report.DailyRevenue);
    }

    [Fact]
    public async Task SearchesByOrderGuidAsWellAsOrderCode()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        var customer = new ApplicationUser { Id = Guid.NewGuid() };
        var matching = Order(customer.Id, "ORD-MAU", OrderStatus.Pending, 100, null);
        db.AddRange(customer, matching, Order(customer.Id, "ORD-KHAC", OrderStatus.Pending, 200, null));
        await db.SaveChangesAsync();

        var reports = new AdminReportingService(db);
        var byId = await reports.GetOrdersAsync(new AdminOrderQuery { Search = $" {matching.Id:D} " }, default);
        var byCode = await reports.GetOrdersAsync(new AdminOrderQuery { Search = "ord-mau" }, default);
        Assert.Equal(matching.Id, Assert.Single(byId.Items).Id);
        Assert.Equal(matching.Id, Assert.Single(byCode.Items).Id);
    }

    [Fact]
    public async Task OrderDetailDoesNotMixPendingOrderWithPaymentCommittedBetweenReads()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var setup = fixture.CreateContext();
        var customer = new ApplicationUser { Id = Guid.NewGuid() };
        var pending = Order(customer.Id, "ORD-PENDING", OrderStatus.Pending, 100, null);
        var game = new Game { Title = "Game", Slug = "game", Status = GameStatus.Published };
        pending.Items.Add(new OrderItem { Game = game, GameTitle = "Game", UnitPrice = 100 });
        setup.AddRange(customer, pending);
        await setup.SaveChangesAsync();

        await using var writer = fixture.CreateContext();
        await writer.Users.AnyAsync();
        async Task PayAsync()
        {
            await new OrderService(writer, new CheckoutAvailability(true))
                .CompleteMockPaymentAsync(customer.Id, pending.Id, MockPaymentOutcome.Success, default);
        }

        var interleaving = new CheckoutBetweenReads(PayAsync);
        await using var reader = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(setup.Database.GetDbConnection()).AddInterceptors(interleaving).Options);
        var detail = await new AdminReportingService(reader).GetOrderAsync(pending.Id, default);

        Assert.Equal(OrderStatus.Pending, detail.Order.Status);
        Assert.Null(detail.Order.PaymentStatus);
        Assert.Empty(detail.Payments);
        Assert.Equal(IsolationLevel.Serializable, interleaving.Isolation);
        await PayAsync();
        var refreshed = await new AdminReportingService(reader).GetOrderAsync(pending.Id, default);
        Assert.Equal(OrderStatus.Paid, refreshed.Order.Status);
        Assert.Equal(PaymentStatus.Succeeded, Assert.Single(refreshed.Payments).Status);
    }

    private static Order Order(Guid userId, string code, OrderStatus status, decimal total, DateTime? paidAt,
        params PaymentStatus[] payments) => new()
    {
        UserId = userId, OrderCode = code, Status = status, TotalAmount = total, PaidAt = paidAt,
        Payments = payments.Select((state, index) => new Payment
        {
            Status = state, AttemptNumber = index + 1, Amount = total, IdempotencyKey = $"fixture-{index}", CompletedAt = paidAt
        }).ToArray()
    };

    private sealed class CheckoutBetweenReads(Func<Task> pay) : DbCommandInterceptor
    {
        private int _reads;
        public IsolationLevel? Isolation { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // Tái hiện checkout hoàn tất giữa phần thông tin đơn và phần đọc các collection của split query.
            if (++_reads == 2)
            {
                Isolation = command.Transaction?.IsolationLevel;
                if (Isolation is null) await pay();
            }
            return result;
        }
    }
}
