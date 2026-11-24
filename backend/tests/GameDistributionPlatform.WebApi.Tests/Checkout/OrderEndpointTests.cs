using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Checkout;

public sealed class OrderEndpointTests : IDisposable
{
    private const string Orders = "/api/v1/orders";
    private readonly CheckoutFactory _factory = new();

    [Fact]
    public async Task CredentialsAreRequiredAndInactiveAccountsAreDenied()
    {
        using var guest = _factory.CreateClient();
        using var response = await guest.PostAsJsonAsync(Orders, new { gameId = Guid.NewGuid() });
        await AssertProblem(response, HttpStatusCode.Unauthorized);
        using var guestPayment = await guest.PostAsJsonAsync(PaymentUrl(Guid.NewGuid()), new { outcome = 0 });
        await AssertProblem(guestPayment, HttpStatusCode.Unauthorized);

        var subject = await SeedUser(_factory, false);
        using var inactive = Client(_factory, subject);
        using var denied = await inactive.PostAsJsonAsync(Orders, new { gameId = Guid.NewGuid() });
        await AssertProblem(denied, HttpStatusCode.Unauthorized);
        guest.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var invalid = await guest.GetAsync(Orders + "/my");
        await AssertProblem(invalid, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectsInvalidAndClientSuppliedMoneyAndNonPublishedGames()
    {
        using var client = Client(_factory, await SeedUser(_factory));
        using var invalid = await client.PostAsJsonAsync(Orders, new { gameId = Guid.Empty });
        await AssertProblem(invalid, HttpStatusCode.BadRequest);
        using var tampered = await client.PostAsJsonAsync(Orders, new { gameId = Guid.NewGuid(), totalAmount = 1m });
        await AssertProblem(tampered, HttpStatusCode.BadRequest);
        foreach (var id in new[] { Guid.NewGuid(), await SeedGame(_factory, status: GameStatus.Draft), await SeedGame(_factory, status: GameStatus.Archived) })
        {
            using var response = await client.PostAsJsonAsync(Orders, new { gameId = id });
            await AssertProblem(response, HttpStatusCode.NotFound);
        }
        using var malformedPayment = await client.PostAsJsonAsync(PaymentUrl(Guid.NewGuid()), new { outcome = 99 });
        await AssertProblem(malformedPayment, HttpStatusCode.BadRequest);
        using var missing = await Pay(client, Guid.NewGuid(), MockPaymentOutcome.Success);
        await AssertProblem(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SuccessUsesSnapshotAndOnlyOwnerCanSeeOrPayTheirOrder()
    {
        var user = await SeedUser(_factory);
        using var owner = Client(_factory, user);
        using var other = Client(_factory, await SeedUser(_factory, role: "Admin"));
        var gameId = await SeedGame(_factory);
        var order = await Create(owner, gameId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(7.50m, order.TotalAmount);
        Assert.Equal(10m, order.OriginalAmount);

        using var snoop = await other.GetAsync(Orders + "/" + order.Id);
        await AssertProblem(snoop, HttpStatusCode.NotFound);
        using var wrongPayer = await Pay(other, order.Id, MockPaymentOutcome.Success);
        await AssertProblem(wrongPayer, HttpStatusCode.NotFound);
        var unrelated = await other.GetFromJsonAsync<PageResponse<OrderSummaryResponse>>(Orders + "/my");
        Assert.Empty(unrelated!.Items);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await db.Games.SingleAsync(game => game.Id == gameId);
        game.Price = 100;
        game.DiscountPercent = 0;
        await db.SaveChangesAsync();

        using var paid = await Pay(owner, order.Id, MockPaymentOutcome.Success);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var payment = await paid.Content.ReadFromJsonAsync<MockPaymentResponse>();
        Assert.Equal(7.50m, payment!.Amount);
        Assert.Equal(PaymentStatus.Succeeded, payment.PaymentStatus);
        var final = await owner.GetFromJsonAsync<OrderResponse>(Orders + "/" + order.Id);
        Assert.Equal(OrderStatus.Paid, final!.Status);
        Assert.True(final.OwnershipGranted);
        Assert.Equal(2.50m, final.DiscountAmount);
        var ownership = Assert.Single(await db.UserLibraries.ToListAsync());
        Assert.Equal(user, ownership.UserId);
        Assert.Equal(gameId, ownership.GameId);
        Assert.Equal(order.Id, (await db.OrderItems.FindAsync(ownership.OrderItemId))!.OrderId);
        Assert.Equal(order.Id, Assert.Single((await owner.GetFromJsonAsync<PageResponse<OrderSummaryResponse>>(Orders + "/my"))!.Items).Id);

        using var inconsistent = await Pay(owner, order.Id, MockPaymentOutcome.Failure);
        await AssertProblem(inconsistent, HttpStatusCode.Conflict);
        using var duplicate = await owner.PostAsJsonAsync(Orders, new { gameId });
        await AssertProblem(duplicate, HttpStatusCode.Conflict, "game_already_owned");
    }

    [Fact]
    public async Task FailurePersistsOnceAndAllowsANewOrderWithoutOwnership()
    {
        using var client = Client(_factory, await SeedUser(_factory));
        var gameId = await SeedGame(_factory);
        var order = await Create(client, gameId);
        using var failed = await Pay(client, order.Id, MockPaymentOutcome.Failure);
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        using var replay = await Pay(client, order.Id, MockPaymentOutcome.Failure);
        Assert.Equal(await failed.Content.ReadFromJsonAsync<MockPaymentResponse>(), await replay.Content.ReadFromJsonAsync<MockPaymentResponse>());
        using var success = await Pay(client, order.Id, MockPaymentOutcome.Success);
        await AssertProblem(success, HttpStatusCode.Conflict);
        var failedOrder = await client.GetFromJsonAsync<OrderResponse>(Orders + "/" + order.Id);
        Assert.Equal(OrderStatus.Cancelled, failedOrder!.Status);
        Assert.Equal(PaymentStatus.Failed, failedOrder.PaymentStatus);
        Assert.False(failedOrder.OwnershipGranted);
        Assert.NotEqual(order.Id, (await Create(client, gameId)).Id);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.UserLibraries);
        Assert.Single(db.Payments);
    }

    [Fact]
    public async Task FreeGameHasPaidOrderAndOwnershipWithoutPaymentRecord()
    {
        using var client = Client(_factory, await SeedUser(_factory));
        var game = await SeedGame(_factory, price: 0);
        var order = await Create(client, game);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.True(order.OwnershipGranted);
        Assert.Equal(0m, order.TotalAmount);
        Assert.Null(order.PaymentStatus);
        using var payment = await Pay(client, order.Id, MockPaymentOutcome.Success);
        await AssertProblem(payment, HttpStatusCode.Conflict);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.UserLibraries);
        Assert.Empty(db.Payments);
    }

    [Fact]
    public async Task ConcurrentCreatesAndPaymentsUseOneOrderOneAttemptAndOneOwnership()
    {
        var user = await SeedUser(_factory);
        var gameId = await SeedGame(_factory);
        using var first = Client(_factory, user);
        using var second = Client(_factory, user);
        var created = await Task.WhenAll(Task.Run(() => first.PostAsJsonAsync(Orders, new { gameId })), Task.Run(() => second.PostAsJsonAsync(Orders, new { gameId })));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Created }, created.Select(response => response.StatusCode).OrderBy(status => status));
        var orders = await Task.WhenAll(created.Select(response => response.Content.ReadFromJsonAsync<OrderResponse>()));
        var orderId = orders[0]!.Id;
        Assert.All(orders, order => Assert.Equal(orderId, order!.Id));

        var payments = await Task.WhenAll(Task.Run(() => Pay(first, orderId, MockPaymentOutcome.Success)), Task.Run(() => Pay(second, orderId, MockPaymentOutcome.Success)));
        Assert.All(payments, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        using var replay = await Pay(first, orderId, MockPaymentOutcome.Success);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Orders);
        Assert.Single(db.Payments);
        Assert.Single(db.UserLibraries);
        foreach (var response in created.Concat(payments)) response.Dispose();
    }

    [Fact]
    public async Task OppositeConcurrentOutcomesSelectOneConsistentFinalState()
    {
        var user = await SeedUser(_factory);
        var gameId = await SeedGame(_factory);
        using var first = Client(_factory, user);
        using var second = Client(_factory, user);
        var order = await Create(first, gameId);
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var db = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var otherDb = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotSame(db.Database.GetDbConnection(), otherDb.Database.GetDbConnection());
        Assert.Equal(db.Database.GetDbConnection().ConnectionString, otherDb.Database.GetDbConnection().ConnectionString);
        Assert.True(await otherDb.Orders.AnyAsync(entry => entry.Id == order.Id));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> SendAsync(HttpClient client, MockPaymentOutcome outcome)
        {
            await start.Task;
            return await Pay(client, order.Id, outcome);
        }

        var success = SendAsync(first, MockPaymentOutcome.Success);
        var failure = SendAsync(second, MockPaymentOutcome.Failure);
        start.SetResult();
        await Task.WhenAll(success, failure);
        using var succeeded = await success;
        using var failed = await failure;
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            new[] { succeeded.StatusCode, failed.StatusCode }.OrderBy(status => status));

        var saved = await first.GetFromJsonAsync<OrderResponse>(Orders + "/" + order.Id);
        var paid = succeeded.IsSuccessStatusCode;
        Assert.NotNull(saved);
        Assert.Equal(paid ? OrderStatus.Paid : OrderStatus.Cancelled, saved.Status);
        Assert.Equal(paid ? PaymentStatus.Succeeded : PaymentStatus.Failed, saved.PaymentStatus);
        Assert.Equal(paid, saved.OwnershipGranted);
        Assert.Single(db.Payments);
        Assert.Equal(paid ? 1 : 0, await db.UserLibraries.CountAsync());
    }

    [Fact]
    public async Task OtherCustomerCannotReadOrPayAnOrder()
    {
        using var owner = Client(_factory, await SeedUser(_factory));
        using var other = Client(_factory, await SeedUser(_factory));
        var order = await Create(owner, await SeedGame(_factory));
        using var read = await other.GetAsync(Orders + "/" + order.Id);
        await AssertProblem(read, HttpStatusCode.NotFound);
        using var pay = await Pay(other, order.Id, MockPaymentOutcome.Success);
        await AssertProblem(pay, HttpStatusCode.NotFound);
        var pending = await owner.GetFromJsonAsync<OrderResponse>(Orders + "/" + order.Id);
        Assert.Equal(OrderStatus.Pending, pending!.Status);
        Assert.Null(pending.PaymentStatus);
        Assert.False(pending.OwnershipGranted);
    }

    [Fact]
    public async Task ArchivedGameCannotFinishPaymentUntilPublishedAgain()
    {
        using var buyer = Client(_factory, await SeedUser(_factory));
        using var admin = Client(_factory, await SeedUser(_factory, role: "Admin"));
        var gameId = await SeedGame(_factory);
        var order = await Create(buyer, gameId);
        var gameStatusUrl = $"/api/v1/admin/games/{gameId}/status";
        using var archived = await admin.PatchAsJsonAsync(gameStatusUrl, new UpdateGameStatusRequest { Status = GameStatus.Archived });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var blocked = await Pay(buyer, order.Id, MockPaymentOutcome.Success);
        await AssertProblem(blocked, HttpStatusCode.Conflict);
        var waiting = await buyer.GetFromJsonAsync<OrderResponse>(Orders + "/" + order.Id);
        Assert.Equal(OrderStatus.Pending, waiting!.Status);
        Assert.Null(waiting.PaymentStatus);
        Assert.False(waiting.OwnershipGranted);

        using var republished = await admin.PatchAsJsonAsync(gameStatusUrl, new UpdateGameStatusRequest { Status = GameStatus.Published });
        Assert.Equal(HttpStatusCode.OK, republished.StatusCode);
        using var paid = await Pay(buyer, order.Id, MockPaymentOutcome.Success);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(order.TotalAmount, (await paid.Content.ReadFromJsonAsync<MockPaymentResponse>())!.Amount);
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("Payments")]
    [InlineData("UserLibraries")]
    public async Task PersistenceFailureRollsBackAndReturnsGenericProblemDetails(string table)
    {
        var user = await SeedUser(_factory);
        var gameId = await SeedGame(_factory);
        using var client = Client(_factory, user);
        var order = table == "Orders" ? null : await Create(client, gameId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trigger = table switch
        {
            "Orders" => "CREATE TRIGGER block_order BEFORE INSERT ON Orders BEGIN SELECT RAISE(ABORT, 'fixture-write-failure'); END;",
            "Payments" => "CREATE TRIGGER block_payment BEFORE INSERT ON Payments BEGIN SELECT RAISE(ABORT, 'fixture-write-failure'); END;",
            _ => "CREATE TRIGGER block_entitlement BEFORE INSERT ON UserLibraries BEGIN SELECT RAISE(ABORT, 'fixture-write-failure'); END;"
        };
        await db.Database.ExecuteSqlRawAsync(trigger);

        using var failed = order is null ? await client.PostAsJsonAsync(Orders, new { gameId })
            : await Pay(client, order.Id, MockPaymentOutcome.Success);
        await AssertProblem(failed, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("fixture-write-failure", await failed.Content.ReadAsStringAsync());
        Assert.Empty(db.Payments);
        Assert.Empty(db.UserLibraries);
        if (order is null)
        {
            Assert.Empty(db.Orders);
            Assert.Empty(db.OrderItems);
        }
        else
        {
            var persisted = Assert.Single(db.Orders);
            Assert.Equal(OrderStatus.Pending, persisted.Status);
            Assert.Null(persisted.PaidAt);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task MockCheckoutIsBlockedOutsideDevelopmentAndTesting(string environment)
    {
        using var factory = new CheckoutFactory(environment);
        var subject = await SeedUser(factory);
        var gameId = await SeedGame(factory);
        using var client = Client(factory, subject);
        var game = await client.GetFromJsonAsync<GameResponse>($"/api/v1/games/{gameId}");
        Assert.False(game!.MockPaymentAvailable);
        using var create = await client.PostAsJsonAsync(Orders, new { gameId });
        await AssertProblem(create, HttpStatusCode.Forbidden, "checkout_unavailable");
        using var payment = await Pay(client, Guid.NewGuid(), MockPaymentOutcome.Success);
        await AssertProblem(payment, HttpStatusCode.Forbidden, "checkout_unavailable");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.Orders);
        Assert.Empty(db.Payments);
        Assert.Empty(db.UserLibraries);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task FreeClaimUsesOnlyTheHostEnvironmentAndCannotBeDuplicated(string environment, bool available)
    {
        using var factory = new CheckoutFactory(environment);
        using var buyer = Client(factory, await SeedUser(factory));
        var gameId = await SeedGame(factory, price: 0);
        var game = await buyer.GetFromJsonAsync<GameResponse>($"/api/v1/games/{gameId}");
        Assert.Equal(available, game!.MockPaymentAvailable);
        using var response = await buyer.PostAsJsonAsync(Orders + "?environment=Development", new { gameId });
        if (available)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var order = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
            Assert.Equal(OrderStatus.Paid, order.Status);
            Assert.True(order.OwnershipGranted);
            using var duplicate = await buyer.PostAsJsonAsync(Orders, new { gameId });
            await AssertProblem(duplicate, HttpStatusCode.Conflict, "game_already_owned");
        }
        else
        {
            await AssertProblem(response, HttpStatusCode.Forbidden, "checkout_unavailable");
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(available ? 1 : 0, await db.Orders.CountAsync());
        Assert.Equal(available ? 1 : 0, await db.UserLibraries.CountAsync());
        Assert.Empty(db.Payments);
    }

    private sealed class CheckoutFactory(string environment = "Testing") : CustomWebApplicationFactory
    {
        protected override string TestEnvironment => environment;
        protected override bool IndependentConnections => true;
    }

    private static async Task<Guid> SeedUser(CustomWebApplicationFactory factory, bool active = true, string role = "User")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new ApplicationUser { IsActive = active, UserName = $"{Guid.NewGuid():N}@example.test" };
        db.Users.Add(user);
        var roleId = await db.Roles.Where(record => record.Name == role).Select(record => record.Id).SingleAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> SeedGame(CustomWebApplicationFactory factory, decimal price = 10m, GameStatus status = GameStatus.Published)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new Game { Title = "Game fixture", Slug = $"fixture-{Guid.NewGuid():N}", Price = price, DiscountPercent = 25, Status = status };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private static HttpClient Client(CustomWebApplicationFactory factory, Guid user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IntegrationTestHelper.CreateTestJwtToken(userId: user));
        return client;
    }

    private static async Task<OrderResponse> Create(HttpClient client, Guid gameId)
    {
        using var response = await client.PostAsJsonAsync(Orders, new { gameId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.EndsWith($"{Orders}/{order.Id}", response.Headers.Location?.ToString());
        return order;
    }

    private static string PaymentUrl(Guid id) => $"{Orders}/{id}/mock-payment";
    private static Task<HttpResponseMessage> Pay(HttpClient client, Guid id, MockPaymentOutcome outcome) =>
        client.PostAsJsonAsync(PaymentUrl(id), new { outcome });

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}: {json}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        if (code is not null) Assert.Equal(code, JsonDocument.Parse(json).RootElement.GetProperty("code").GetString());
    }

    public void Dispose() => _factory.Dispose();
}
