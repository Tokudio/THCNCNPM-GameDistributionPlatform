using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.SqlServer;

[Collection(SqlServerCollection.Name)]
[Trait("Database", "SqlServer")]
public sealed class SqlServerCheckoutTests(SqlServerFixture fixture)
{
    private const string Library = "/api/v1/library/my";
    private static readonly CheckoutAvailability Available = new(true);

    [SqlServerFact]
    public async Task ConcurrentCreatesUseOnePendingOrderAndPreserveTheServerPriceSnapshot()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var first = data.Client(user);
        using var second = data.Client(user);
        using var tampered = await first.PostAsJsonAsync(LiveApi.Orders, new { gameId = game, totalAmount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);

        var orders = await LiveApi.RaceAsync(
            () => LiveApi.CreateAsync(first, game), () => LiveApi.CreateAsync(second, game));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Created }, orders.Select(result => result.Status).Order());
        var order = orders[0].Order;
        Assert.Equal(order.Id, orders[1].Order.Id);
        Assert.Equal(19.99m, order.OriginalAmount);
        Assert.Equal(14.99m, order.TotalAmount);
        Assert.Equal(OrderStatus.Pending, order.Status);

        await using var db = fixture.NewContext();
        var saved = await db.Orders.Include(entry => entry.Items).SingleAsync(entry => entry.UserId == user);
        Assert.Equal(order.Id, saved.Id);
        Assert.Equal(game, Assert.Single(saved.Items).GameId);
        Assert.Equal(19.99m, Assert.Single(saved.Items).UnitPrice);
        Assert.Equal(14.99m, saved.TotalAmount);
        Assert.Empty(await db.UserLibraries.Where(entry => entry.UserId == user).ToListAsync());

        var currentGame = await db.Games.SingleAsync(entry => entry.Id == game);
        currentGame.Price = 50m;
        currentGame.DiscountPercent = 0;
        await db.SaveChangesAsync();
        var reused = await LiveApi.CreateAsync(first, game);
        Assert.Equal(HttpStatusCode.OK, reused.Status);
        Assert.Equal(order.Id, reused.Order.Id);
        Assert.Equal(14.99m, reused.Order.TotalAmount);
        using var response = await LiveApi.PayAsync(first, order.Id);
        var payment = await LiveApi.ReadPaymentAsync(response);
        Assert.Equal(14.99m, payment.Amount);
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Paid, PaymentStatus.Succeeded);
    }

    [SqlServerFact]
    public async Task ConcurrentSuccessAndReplayUseOnePaymentAndOwnershipAndRejectDuplicatePurchase()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var first = data.Client(user);
        using var second = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(first, game);

        var responses = await LiveApi.RaceAsync(
            () => LiveApi.PayAsync(first, order.Id), () => LiveApi.PayAsync(second, order.Id));
        using var initial = responses[0];
        using var concurrent = responses[1];
        using var replay = await LiveApi.PayAsync(first, order.Id);
        var firstPayment = await LiveApi.ReadPaymentAsync(initial);
        Assert.Equal(firstPayment, await LiveApi.ReadPaymentAsync(concurrent));
        Assert.Equal(firstPayment, await LiveApi.ReadPaymentAsync(replay));

        using var duplicate = await first.PostAsJsonAsync(LiveApi.Orders, new { gameId = game });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("game_already_owned", problem.RootElement.GetProperty("code").GetString());
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Paid, PaymentStatus.Succeeded);
        await using var db = fixture.NewContext();
        Assert.Equal(1, await db.Orders.CountAsync(row => row.UserId == user));
        var payment = await db.Payments.SingleAsync(row => row.OrderId == order.Id);
        Assert.Equal($"mock:{order.Id:N}", payment.IdempotencyKey);
        Assert.Equal(1, payment.AttemptNumber);
    }

    [SqlServerFact]
    public async Task OppositeConcurrentPaymentOutcomesCommitOnlyOneConsistentResult()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var first = data.Client(user);
        using var second = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(first, game);
        var responses = await LiveApi.RaceAsync(() => LiveApi.PayAsync(first, order.Id),
            () => LiveApi.PayAsync(second, order.Id, MockPaymentOutcome.Failure));
        using var paid = responses[0];
        using var failed = responses[1];
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(result => result.StatusCode).Order());
        await AssertOrderStateAsync(user, order.Id,
            paid.IsSuccessStatusCode ? OrderStatus.Paid : OrderStatus.Cancelled,
            paid.IsSuccessStatusCode ? PaymentStatus.Succeeded : PaymentStatus.Failed);
    }

    [SqlServerFact]
    public async Task LostResponsesAfterRealCommitAllowOrderAndPaymentRetryWithoutExtraRows()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var client = data.Client(user);
        var afterCommit = new CommitHook(after: () => throw new TimeoutException("Fixture mô phỏng mất phản hồi sau khi commit."));
        await using (var db = fixture.NewContext(afterCommit))
        {
            await Assert.ThrowsAsync<TimeoutException>(() => new OrderService(db, Available).CreateAsync(user, game, default));
            Assert.True(afterCommit.Reached);
        }
        var reuse = await LiveApi.CreateAsync(client, game);
        Assert.Equal(HttpStatusCode.OK, reuse.Status);
        await using (var db = fixture.NewContext(afterCommit))
            await Assert.ThrowsAsync<TimeoutException>(() =>
                new OrderService(db, Available).CompleteMockPaymentAsync(user, reuse.Order.Id, MockPaymentOutcome.Success, default));
        using var replay = await LiveApi.PayAsync(client, reuse.Order.Id);
        Assert.Equal(PaymentStatus.Succeeded, (await LiveApi.ReadPaymentAsync(replay)).PaymentStatus);
        await AssertOrderStateAsync(user, reuse.Order.Id, OrderStatus.Paid, PaymentStatus.Succeeded);
        await using var check = fixture.NewContext();
        Assert.Equal(1, await check.Orders.CountAsync(order => order.UserId == user));
    }

    [SqlServerFact]
    public async Task ExceptionAfterPaymentWritesBeforeCommitRollsBackAllThreeChanges()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        using var client = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(client, await data.GameAsync());
        var stop = new CommitHook(before: (transaction, token) =>
            throw new InvalidOperationException("Fixture dừng sau SaveChanges và trước Commit."));
        await using (var db = fixture.NewContext(stop))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new OrderService(db, Available).CompleteMockPaymentAsync(user, order.Id, MockPaymentOutcome.Success, default));
        Assert.True(stop.Reached);
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Pending, null);
    }

    [SqlServerFact]
    public async Task CancellationBeforeCommitRollsBackPaymentAndFreshRequestCanRetry()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        using var client = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(client, await data.GameAsync());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stop = new CommitHook(before: (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        await using (var db = fixture.NewContext(stop))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new OrderService(db, Available).CompleteMockPaymentAsync(user, order.Id, MockPaymentOutcome.Success, cancellation.Token));
        Assert.True(stop.Reached);
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Pending, null);

        using var retry = await LiveApi.PayAsync(client, order.Id);
        Assert.Equal(PaymentStatus.Succeeded, (await LiveApi.ReadPaymentAsync(retry)).PaymentStatus);
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Paid, PaymentStatus.Succeeded);
    }

    [SqlServerFact]
    public async Task LibraryReturnsPaidFreeAndArchivedGamesForTheirOwnerOnly()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        using var owner = data.Client(user);
        using var other = data.Client(await data.UserAsync());
        using var admin = data.Client(await data.UserAsync("Admin"));
        var paidGame = await data.GameAsync();
        var freeGame = await data.GameAsync(price: 0);
        var (_, paid) = await LiveApi.CreateAsync(owner, paidGame);
        using var payment = await LiveApi.PayAsync(owner, paid.Id);
        Assert.Equal(PaymentStatus.Succeeded, (await LiveApi.ReadPaymentAsync(payment)).PaymentStatus);

        var freeClaims = await LiveApi.RaceAsync(
            () => owner.PostAsJsonAsync(LiveApi.Orders, new { gameId = freeGame }),
            () => owner.PostAsJsonAsync(LiveApi.Orders, new { gameId = freeGame }));
        using var firstClaim = freeClaims[0];
        using var secondClaim = freeClaims[1];
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
            freeClaims.Select(result => result.StatusCode).Order());
        var free = (await freeClaims.Single(result => result.IsSuccessStatusCode).Content.ReadFromJsonAsync<OrderResponse>())!;
        await AssertOrderStateAsync(user, free.Id, OrderStatus.Paid, null);
        using var archived = await admin.PatchAsJsonAsync($"/api/v1/admin/games/{paidGame}/status", new { status = GameStatus.Archived });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);

        var library = await owner.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library);
        Assert.Equal(new[] { paidGame, freeGame }.Order(), library!.Items.Select(entry => entry.GameId).Order());
        var record = await owner.GetFromJsonAsync<LibraryGameDetailResponse>($"{Library}/{paidGame}");
        Assert.Equal(paidGame, record!.GameId);
        Assert.Equal(GameStatus.Archived, record.Status);
        using var denied = await other.GetAsync($"{Library}/{paidGame}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var orderDenied = await other.GetAsync($"{LiveApi.Orders}/{paid.Id}");
        Assert.Equal(HttpStatusCode.NotFound, orderDenied.StatusCode);
        using var paymentDenied = await LiveApi.PayAsync(other, paid.Id);
        Assert.Equal(HttpStatusCode.NotFound, paymentDenied.StatusCode);
        await AssertOrderStateAsync(user, paid.Id, OrderStatus.Paid, PaymentStatus.Succeeded);
    }

    [SqlServerFact]
    public async Task PendingAndFailedPaymentsDoNotGrantLibraryAccess()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var owner = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(owner, game);
        using var pending = await owner.GetAsync($"{Library}/{game}");
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Pending, null);

        using var failure = await LiveApi.PayAsync(owner, order.Id, MockPaymentOutcome.Failure);
        using var failureReplay = await LiveApi.PayAsync(owner, order.Id, MockPaymentOutcome.Failure);
        Assert.Equal(await LiveApi.ReadPaymentAsync(failure), await LiveApi.ReadPaymentAsync(failureReplay));
        using var denied = await owner.GetAsync($"{Library}/{game}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library))!.Items);
        await AssertOrderStateAsync(user, order.Id, OrderStatus.Cancelled, PaymentStatus.Failed);
    }

    private async Task AssertOrderStateAsync(Guid user, Guid orderId, OrderStatus status, PaymentStatus? paymentStatus)
    {
        await using var db = fixture.NewContext();
        var order = await db.Orders.Include(entry => entry.Items).ThenInclude(item => item.UserLibrary)
            .Include(entry => entry.Payments).AsSplitQuery().SingleAsync(entry => entry.Id == orderId);
        Assert.Equal(user, order.UserId);
        Assert.Equal(status, order.Status);
        var item = Assert.Single(order.Items);
        if (status == OrderStatus.Paid)
        {
            Assert.NotNull(order.PaidAt);
            Assert.NotNull(item.UserLibrary);
            Assert.Equal(user, item.UserLibrary.UserId);
            Assert.Equal(item.GameId, item.UserLibrary.GameId);
            Assert.Equal(1, await db.UserLibraries.CountAsync(entry => entry.UserId == user && entry.GameId == item.GameId));
        }
        else
        {
            Assert.Null(order.PaidAt);
            Assert.Null(item.UserLibrary);
            Assert.False(await db.UserLibraries.AnyAsync(entry => entry.UserId == user && entry.GameId == item.GameId));
        }

        if (paymentStatus is null) Assert.Empty(order.Payments);
        else
        {
            var payment = Assert.Single(order.Payments);
            Assert.Equal(paymentStatus, payment.Status);
            Assert.Equal(order.TotalAmount, payment.Amount);
            Assert.Equal(order.Currency, payment.Currency);
        }
    }
}

internal static class LiveApi
{
    internal const string Orders = "/api/v1/orders";

    internal static async Task<(HttpStatusCode Status, OrderResponse Order)> CreateAsync(HttpClient client, Guid game)
    {
        using var response = await client.PostAsJsonAsync(Orders, new { gameId = game });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        return (response.StatusCode, order);
    }

    internal static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid order, MockPaymentOutcome outcome = MockPaymentOutcome.Success) =>
        client.PostAsJsonAsync($"{Orders}/{order}/mock-payment", new { outcome });

    internal static async Task<MockPaymentResponse> ReadPaymentAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<MockPaymentResponse>();
        Assert.NotNull(payment);
        return payment;
    }

    internal static async Task<T[]> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = SendAsync(first);
        var two = SendAsync(second);
        start.SetResult();
        return await Task.WhenAll(one, two);

        async Task<T> SendAsync(Func<Task<T>> request)
        {
            await start.Task;
            return await request();
        }
    }
}

internal sealed class CommitHook(Func<DbTransaction, CancellationToken, Task>? before = null, Action? after = null)
    : DbTransactionInterceptor
{
    internal bool Reached { get; private set; }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Reached = true;
        if (before is not null) await before(transaction, cancellationToken);
        return result;
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        after?.Invoke();
        return Task.CompletedTask;
    }
}
