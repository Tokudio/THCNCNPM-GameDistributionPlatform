using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
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
    private static readonly Guid PackageGame = Guid.Parse("25408626-4e57-4cdf-ae05-f3abd1d2f341");
    private static readonly Guid MissingGame = Guid.Parse("0366e81a-dc3c-4ebb-aecc-d2c5cffc65e4");
    private const string PackageFile = "demo-signal-path.zip";
    private readonly CustomWebApplicationFactory _factory = new DownloadFactory();

    [Theory]
    [InlineData("paid", true)]
    [InlineData("free", true)]
    [InlineData("archived", true)]
    [InlineData("failed", false)]
    [InlineData("pending", false)]
    public async Task DownloadUsesActualOrderEntitlementAndRejectsOtherAccounts(string scenario, bool allowed)
    {
        using var buyer = await CustomerAsync();
        using var other = await CustomerAsync();
        var gameId = await GameAsync("Download fixture", scenario == "free" ? 0 : 100, PackageGame);
        var order = await OrderAsync(buyer, gameId);
        if (scenario is "paid" or "archived") await PayAsync(buyer, order.Id, MockPaymentOutcome.Success);
        if (scenario == "failed") await PayAsync(buyer, order.Id, MockPaymentOutcome.Failure);
        if (scenario == "archived")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Games.SingleAsync(game => game.Id == gameId)).Status = GameStatus.Archived;
            await db.SaveChangesAsync();
        }

        var url = $"{Library}/{gameId}";
        using var info = await buyer.GetAsync(url + "/download-info");
        using var download = await buyer.GetAsync(url + "/download");
        using var otherInfo = await other.GetAsync(url + "/download-info");
        using var otherDownload = await other.GetAsync(url + "/download");
        Assert.Equal(HttpStatusCode.NotFound, otherInfo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherDownload.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, info.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, download.StatusCode);
        if (!allowed) return;

        var metadata = await info.Content.ReadFromJsonAsync<LibraryDownloadInfoResponse>();
        Assert.NotNull(metadata);
        Assert.True(metadata.IsOwned);
        Assert.True(metadata.IsDownloadAvailable);
        Assert.Equal(gameId, metadata.GameId);
        Assert.Equal(PackageFile, metadata.FileName);
        Assert.DoesNotContain("DemoPackages", await info.Content.ReadAsStringAsync());
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(PackageFile, download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(metadata.FileSize, bytes.LongLength);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("signal-path.json"));
        using var staticResponse = await buyer.GetAsync("/DemoPackages/" + PackageFile);
        Assert.Equal(HttpStatusCode.NotFound, staticResponse.StatusCode);
    }

    [Fact]
    public async Task DownloadRoutesRejectGuestInvalidJwtInactiveAccountAndInvalidGameId()
    {
        using var guest = _factory.CreateClient();
        using var invalid = _factory.CreateClient();
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var inactive = await CustomerAsync(active: false);
        using var active = await CustomerAsync();
        foreach (var action in new[] { "download-info", "download" })
        {
            foreach (var client in new[] { guest, invalid, inactive })
            {
                using var denied = await client.GetAsync($"{Library}/{PackageGame}/{action}");
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            }
            using var malformed = await active.GetAsync($"{Library}/invalid-guid/{action}");
            Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
            using var empty = await active.GetAsync($"{Library}/{Guid.Empty}/{action}");
            Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        }
    }

    [Fact]
    public async Task MissingPackageHasSafeMetadataAndAControlledDownloadError()
    {
        using var owner = await CustomerAsync();
        var gameId = await GameAsync("Missing fixture", 0, MissingGame);
        await OrderAsync(owner, gameId);
        var url = $"{Library}/{gameId}";
        var info = await owner.GetFromJsonAsync<LibraryDownloadInfoResponse>(url + "/download-info");
        Assert.NotNull(info);
        Assert.True(info.IsOwned);
        Assert.False(info.IsDownloadAvailable);
        Assert.Null(info.FileName);
        Assert.Null(info.FileSize);
        using var failure = await owner.GetAsync(url + "/download");
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.Equal("application/problem+json", failure.Content.Headers.ContentType?.MediaType);
        var json = await failure.Content.ReadAsStringAsync();
        Assert.Contains("game_package_unavailable", json);
        Assert.DoesNotContain("missing-fixture.zip", json);
        Assert.DoesNotContain("DemoPackages", json);
    }

    [Theory]
    [InlineData("../demo.zip")]
    [InlineData("other\\demo.zip")]
    [InlineData("demo.exe")]
    [InlineData("readme.zip:stream")]
    [InlineData("C:\\demo.zip")]
    public void StorageRejectsInvalidPackageMappings(string name)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new LocalGamePackageStorage(AppContext.BaseDirectory, new Dictionary<string, string> { [PackageGame.ToString()] = name }));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void DemoPackageMappingIsDisabledOutsideDevelopmentAndTesting(string environment)
    {
        using var factory = new DownloadFactory(environment);
        Assert.Null(factory.Services.GetRequiredService<IGamePackageStorage>().Open(PackageGame));
    }

    [Fact]
    public async Task DevelopmentOpenApiDescribesBothDownloadRoutes()
    {
        using var factory = new DownloadFactory("Development");
        using var client = factory.CreateClient();
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        Assert.NotNull(document);
        var root = document.RootElement;
        Assert.StartsWith("3.0.", root.GetProperty("openapi").GetString());
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var paths = root.GetProperty("paths");
        foreach (var action in new[] { "download-info", "download" })
        {
            var endpoint = paths.GetProperty($"{Library}/{{gameId}}/{action}").GetProperty("get");
            var requirement = Assert.Single(endpoint.GetProperty("security").EnumerateArray());
            Assert.Empty(requirement.GetProperty("Bearer").EnumerateArray());
        }
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out var loginSecurity) && loginSecurity.GetArrayLength() > 0);
    }

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

    private async Task<Guid> GameAsync(string title, decimal price, Guid? id = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new Game { Title = title, Slug = $"fixture-{Guid.NewGuid():N}", Price = price, Status = GameStatus.Published };
        if (id is Guid gameId) game.Id = gameId;
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

    private sealed class DownloadFactory(string environment = "Testing") : CustomWebApplicationFactory
    {
        protected override string TestEnvironment => environment;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting($"GameDownloads:Packages:{PackageGame:D}", PackageFile);
            builder.UseSetting($"GameDownloads:Packages:{MissingGame:D}", "missing-fixture.zip");
        }
    }
}
