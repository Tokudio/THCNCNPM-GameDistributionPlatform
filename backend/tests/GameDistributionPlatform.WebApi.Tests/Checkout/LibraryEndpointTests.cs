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

public sealed class LibraryEndpointTests : IDisposable
{
    private const string Library = "/api/v1/library/my";
    private const string Orders = "/api/v1/orders";
    private readonly CustomWebApplicationFactory _factory = new();

    [Fact]
    public async Task RequiresValidJwtAndActiveAccountForListAndDetail()
    {
        using var guest = _factory.CreateClient();
        using var invalid = _factory.CreateClient();
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var inactive = await CustomerAsync(active: false);
        foreach (var client in new[] { guest, invalid, inactive })
        {
            using var list = await client.GetAsync(Library);
            using var detail = await client.GetAsync($"{Library}/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
            Assert.Equal("application/problem+json", detail.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task PendingAndFailedOrdersStayOutOfLibraryWhileSuccessAndFreeClaimGrantOwnership()
    {
        using var buyer = await CustomerAsync();
        var paid = await GameAsync("Signal Tower", 100);
        var free = await GameAsync("Signal Night", 0);
        var failed = await GameAsync("Failed Signal", 100);
        Assert.Empty((await buyer.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library))!.Items);

        var pending = await OrderAsync(buyer, paid);
        Assert.Empty((await buyer.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"{Library}/{paid}")).StatusCode);

        await PayAsync(buyer, pending.Id, MockPaymentOutcome.Success);
        var failedOrder = await OrderAsync(buyer, failed);
        await PayAsync(buyer, failedOrder.Id, MockPaymentOutcome.Failure);
        await OrderAsync(buyer, free);

        var library = (await buyer.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library + "?search=signal"))!;
        Assert.Equal(2, library.TotalCount);
        Assert.Equal(new[] { free, paid }, library.Items.Select(item => item.GameId));
        Assert.All(library.Items, item => Assert.True(item.IsOwned));
        using var paidResult = await buyer.GetAsync($"{Library}/{paid}");
        Assert.Equal(HttpStatusCode.OK, paidResult.StatusCode);
        Assert.True(paidResult.Headers.CacheControl!.NoStore);
        var json = await paidResult.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.True(body.RootElement.GetProperty("isOwned").GetBoolean());
        Assert.EndsWith("Z", body.RootElement.GetProperty("acquiredAt").GetString());
        foreach (var field in new[] { "userId", "orderItemId", "passwordHash", "idempotencyKey", "transactionReference", "demoDownloadUrl" })
            Assert.False(body.RootElement.TryGetProperty(field, out _));
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"{Library}/{failed}")).StatusCode);
    }

    [Fact]
    public async Task ArchivedGameStaysInOwnersLibraryWithoutBecomingPublicOrVisibleToOtherBuyers()
    {
        using var owner = await CustomerAsync();
        using var other = await CustomerAsync();
        using var admin = await CustomerAsync(role: "Admin");
        using var guest = _factory.CreateClient();
        var game = await GameAsync("Archived Signal", 100);
        var otherGame = await GameAsync("Other game", 0);
        var order = await OrderAsync(owner, game);
        await PayAsync(owner, order.Id, MockPaymentOutcome.Success);
        await OrderAsync(other, otherGame);

        using var archive = await admin.PatchAsJsonAsync($"/api/v1/admin/games/{game}/status",
            new UpdateGameStatusRequest { Status = GameStatus.Archived });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/games/{game}")).StatusCode);
        var publicGames = (await guest.GetFromJsonAsync<PageResponse<GameSummaryResponse>>("/api/v1/games?search=Archived"))!;
        Assert.Empty(publicGames.Items);

        var owned = (await owner.GetFromJsonAsync<LibraryGameDetailResponse>($"{Library}/{game}"))!;
        Assert.Equal(GameStatus.Archived, owned.Status);
        Assert.True(owned.IsOwned);
        Assert.Equal(game, Assert.Single((await owner.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library))!.Items).GameId);
        Assert.Equal(otherGame, Assert.Single((await other.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library))!.Items).GameId);
        foreach (var denied in new[] { other, admin })
        {
            using var response = await denied.GetAsync($"{Library}/{game}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Archived Signal", json);
            using var body = JsonDocument.Parse(json);
            Assert.Equal("Game was not found in your library.", body.RootElement.GetProperty("detail").GetString());
            Assert.False(body.RootElement.TryGetProperty("stackTrace", out _));
            Assert.False(body.RootElement.TryGetProperty("exception", out _));
        }
    }

    [Fact]
    public async Task OutOfRangePageKeepsFilteredTotalsAndPrivateCacheHeaders()
    {
        using var buyer = await CustomerAsync();
        var matching = await GameAsync("Signal Tower", 0);
        var unrelated = await GameAsync("Night Circuit", 0);
        await OrderAsync(buyer, matching);
        await OrderAsync(buyer, unrelated);

        using var response = await buyer.GetAsync(Library + "?search=signal&pageNumber=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<PageResponse<LibraryGameResponse>>())!;
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task LibraryDetailUsesTheSameGameIdAsPublicDetailAndOwnedList()
    {
        using var owner = await CustomerAsync();
        using var other = await CustomerAsync();
        using var guest = _factory.CreateClient();
        var gameId = await GameAsync("Library navigation fixture", 100);
        var publicGame = (await guest.GetFromJsonAsync<GameResponse>($"/api/v1/games/{gameId}"))!;
        var order = await OrderAsync(owner, gameId);
        await PayAsync(owner, order.Id, MockPaymentOutcome.Success);

        var item = Assert.Single((await owner.GetFromJsonAsync<PageResponse<LibraryGameResponse>>(Library))!.Items);
        var detailUrl = $"{Library}/{publicGame.Id}";
        var detail = (await owner.GetFromJsonAsync<LibraryGameDetailResponse>(detailUrl))!;
        Assert.Equal(gameId, publicGame.Id);
        Assert.Equal(gameId, Assert.Single(order.Items).GameId);
        Assert.Equal(gameId, item.GameId);
        Assert.Equal(gameId, detail.GameId);
        Assert.Equal(item.Slug, detail.Slug);
        Assert.True(detail.IsOwned);

        using var denied = await other.GetAsync(detailUrl);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var deniedProblem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.Equal("library_game_not_found", deniedProblem.RootElement.GetProperty("code").GetString());

        using var wrongId = await owner.GetAsync($"{Library}/{order.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongId.StatusCode);
        using var wrongIdProblem = JsonDocument.Parse(await wrongId.Content.ReadAsStringAsync());
        Assert.Equal("library_game_not_found", wrongIdProblem.RootElement.GetProperty("code").GetString());

        using var slug = await owner.GetAsync($"{Library}/{publicGame.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, slug.StatusCode);
        Assert.DoesNotContain("library_game_not_found", await slug.Content.ReadAsStringAsync());

        using var unauthorized = await guest.GetAsync(detailUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Theory]
    [InlineData("?pageNumber=0")]
    [InlineData("?pageSize=51")]
    public async Task InvalidPaginationUsesProblemDetails(string suffix)
    {
        using var buyer = await CustomerAsync();
        using var response = await buyer.GetAsync(Library + suffix);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<HttpClient> CustomerAsync(bool active = true, string role = "User")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@example.test", IsActive = active };
        db.Users.Add(user);
        var roleId = await db.Roles.Where(record => record.Name == role).Select(record => record.Id).SingleAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IntegrationTestHelper.CreateTestJwtToken(userId: user.Id));
        return client;
    }

    private async Task<Guid> GameAsync(string title, decimal price)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new Game { Title = title, Slug = $"fixture-{Guid.NewGuid():N}", Price = price, Status = GameStatus.Published };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private static async Task<OrderResponse> OrderAsync(HttpClient client, Guid gameId)
    {
        using var response = await client.PostAsJsonAsync(Orders, new { gameId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task PayAsync(HttpClient client, Guid orderId, MockPaymentOutcome outcome)
    {
        using var payment = await client.PostAsJsonAsync($"{Orders}/{orderId}/mock-payment", new { outcome });
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
