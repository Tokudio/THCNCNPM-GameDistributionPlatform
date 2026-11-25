using System.Globalization;
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

namespace GameDistributionPlatform.WebApi.Tests.Admin;

public sealed class AdminApiTests : IDisposable
{
    private const string Dashboard = "/api/v1/admin/dashboard/summary";
    private const string Orders = "/api/v1/admin/orders";
    private const string Users = "/api/v1/admin/users";
    private readonly CustomWebApplicationFactory _factory = new ConcurrentFactory();

    [Theory]
    [InlineData("guest", HttpStatusCode.Unauthorized)]
    [InlineData("invalid", HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("inactive", HttpStatusCode.Unauthorized)]
    public async Task AdminEndpointsRequireAnActiveAdminInIdentity(string kind, HttpStatusCode expected)
    {
        using var client = kind is "user" or "inactive"
            ? Client(await CreateUserAsync(kind, kind == "user" ? "User" : "Admin", kind != "inactive"), roleClaim: "Admin")
            : _factory.CreateClient();
        if (kind == "invalid") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        foreach (var route in new[] { Dashboard, Orders, $"{Orders}/{Guid.NewGuid()}", Users, $"{Users}/{Guid.NewGuid()}" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(expected, response.StatusCode);
        }
        using var change = await client.PatchAsJsonAsync($"{Users}/{Guid.NewGuid()}/status", new { isActive = false });
        Assert.Equal(expected, change.StatusCode);
    }

    [Fact]
    public async Task AdminReadsMultipleCustomersAndExactRevenueWithoutChangingOrdersOrOwnership()
    {
        using var admin = Client(await CreateUserAsync("manage", "Admin"));
        var firstId = await CreateUserAsync("lan", "User");
        using var first = Client(firstId);
        using var second = Client(await CreateUserAsync("minh", "User"));
        var empty = (await admin.GetFromJsonAsync<DashboardSummaryResponse>(Dashboard))!;
        Assert.Equal(0, empty.Orders.Total);
        Assert.Empty(empty.Revenue);

        var gameId = await CreateGameAsync();
        var paid = await CreateOrderAsync(first, gameId);
        var pending = await CreateOrderAsync(second, gameId);
        using var payment = await first.PostAsJsonAsync($"/api/v1/orders/{paid.Id}/mock-payment", new { outcome = MockPaymentOutcome.Success });
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);

        using var report = await admin.GetAsync(Dashboard);
        Assert.True(report.Headers.CacheControl?.NoStore);
        using var summary = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        Assert.Equal(2, summary.RootElement.GetProperty("orders").GetProperty("total").GetInt32());
        Assert.All(summary.RootElement.GetProperty("recentOrders").EnumerateArray(), order =>
            Assert.False(order.TryGetProperty("customerEmail", out _)));
        var amount = summary.RootElement.GetProperty("revenue")[0].GetProperty("amount").GetString();
        Assert.Equal(9_007_199_254_740_992m, decimal.Parse(amount!, CultureInfo.InvariantCulture));

        var list = (await admin.GetFromJsonAsync<PageResponse<AdminOrderSummaryResponse>>(Orders))!;
        Assert.Contains(list.Items, order => order.Id == pending.Id && order.CustomerEmail == "minh@example.test");
        var filtered = (await admin.GetFromJsonAsync<PageResponse<AdminOrderSummaryResponse>>(
            $"{Orders}?search=LAN%40EXAMPLE&status=1&paymentStatus=1&pageSize=1"))!;
        Assert.Equal(paid.Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(PaymentStatus.Succeeded, filtered.Items[0].PaymentStatus);
        Assert.Equal(1, filtered.TotalPages);

        using var detailResponse = await admin.GetAsync($"{Orders}/{paid.Id}");
        var json = await detailResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("idempotencyKey", json);
        Assert.DoesNotContain("transactionReference", json);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<AdminOrderDetailResponse>())!;
        Assert.Equal("lan@example.test", detail.Order.CustomerEmail);
        Assert.Equal("Game mẫu", Assert.Single(detail.Items).GameTitle);
        Assert.Equal(PaymentStatus.Succeeded, Assert.Single(detail.Payments).Status);
        Assert.Equal(detail.Order.TotalAmount, detail.OriginalAmount);
        using var missing = await admin.GetAsync($"{Orders}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var mutation = await admin.PatchAsJsonAsync($"{Orders}/{paid.Id}", new { status = 2 });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, mutation.StatusCode);
        using var foreign = await second.GetAsync($"/api/v1/orders/{paid.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var customer = (await admin.GetFromJsonAsync<AdminUserDetailResponse>($"{Users}/{firstId}"))!;
        Assert.Equal(1, customer.TotalOrders);
        Assert.Equal(1, customer.OwnedGames);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Orders.CountAsync());
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync(order => order.Id == paid.Id)).Status);
        Assert.Equal(OrderStatus.Pending, (await db.Orders.SingleAsync(order => order.Id == pending.Id)).Status);
        Assert.Equal(PaymentStatus.Succeeded, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(firstId, (await db.UserLibraries.SingleAsync()).UserId);
    }

    [Fact]
    public async Task UserSearchReturnsSafeDtosAndStatusChangesRejectPreviouslyIssuedJwtWhileInactive()
    {
        var adminId = await CreateUserAsync("admin", "Admin");
        var memberId = await CreateUserAsync("member", "User");
        await CreateUserAsync("other", "User");
        using var admin = Client(adminId);
        using var member = Client(memberId);
        using var userResponse = await admin.GetAsync($"{Users}?search=MEMBER&role=user&isActive=true&pageSize=1");
        var json = await userResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json);
        Assert.DoesNotContain("securityStamp", json);
        Assert.DoesNotContain("concurrencyStamp", json);
        var page = (await userResponse.Content.ReadFromJsonAsync<PageResponse<AdminUserSummaryResponse>>())!;
        Assert.Equal(memberId, Assert.Single(page.Items).Id);
        Assert.Equal(new[] { "User" }, page.Items[0].Roles);

        using var self = await admin.PatchAsJsonAsync($"{Users}/{adminId}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        using var incomplete = await admin.PatchAsJsonAsync($"{Users}/{memberId}/status", new { });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        using var deactivate = await admin.PatchAsJsonAsync($"{Users}/{memberId}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        foreach (var route in new[] { "/api/v1/library/my", "/api/v1/orders/my", "/api/v1/auth/me" })
        {
            using var rejected = await member.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        using var rejectedPurchase = await member.PostAsJsonAsync("/api/v1/orders", new { gameId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedPurchase.StatusCode);
        var inactive = (await admin.GetFromJsonAsync<AdminUserDetailResponse>($"{Users}/{memberId}"))!;
        Assert.False(inactive.User.IsActive);
        using var reactivate = await admin.PatchAsJsonAsync($"{Users}/{memberId}/status", new { isActive = true });
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        using var restored = await member.GetAsync("/api/v1/library/my");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var missing = await admin.GetAsync($"{Users}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(Users + "?role=Owner")]
    [InlineData(Users + "?pageSize=51")]
    [InlineData(Orders + "?status=999")]
    public async Task InvalidFiltersReturnProblemDetails(string route)
    {
        using var admin = Client(await CreateUserAsync("admin", "Admin"));
        using var response = await admin.GetAsync(route);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ConcurrentRequestsFromTwoAdminsCannotDisableBothAccounts()
    {
        var firstId = await CreateUserAsync("first", "Admin");
        var secondId = await CreateUserAsync("second", "Admin");
        using var first = Client(firstId);
        using var second = Client(secondId);
        var responses = await Task.WhenAll(
            Task.Run(() => first.PatchAsJsonAsync($"{Users}/{secondId}/status", new { isActive = false })),
            Task.Run(() => second.PatchAsJsonAsync($"{Users}/{firstId}/status", new { isActive = false })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        var disabled = responses[0].StatusCode == HttpStatusCode.NoContent ? second : first;
        using var denied = await disabled.GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        foreach (var response in responses) response.Dispose();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(user => user.IsActive));
    }

    [Fact]
    public async Task ConcurrentAdminRequestsForTheSameMemberAreIdempotent()
    {
        using var first = Client(await CreateUserAsync("first", "Admin"));
        using var second = Client(await CreateUserAsync("second", "Admin"));
        var memberId = await CreateUserAsync("member", "User");
        var route = $"{Users}/{memberId}/status";
        var responses = await Task.WhenAll(
            Task.Run(() => first.PatchAsJsonAsync(route, new { isActive = false })),
            Task.Run(() => second.PatchAsJsonAsync(route, new { isActive = false })));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var actual = (await first.GetFromJsonAsync<AdminUserDetailResponse>($"{Users}/{memberId}"))!;
        Assert.False(actual.User.IsActive);
        Assert.Equal(new[] { "User" }, actual.User.Roles);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IdentityValidationFailureLeavesStoredStatusUnchanged(bool hasSecurityStamp)
    {
        using var admin = Client(await CreateUserAsync("admin", "Admin"));
        var memberId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            // Bản ghi cũ thiếu email vẫn đọc được. Identity từ chối lưu khi yêu cầu email hợp lệ.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new ApplicationUser
            {
                Id = memberId, UserName = "legacy", NormalizedUserName = "LEGACY",
                SecurityStamp = hasSecurityStamp ? Guid.NewGuid().ToString("N") : null
            });
            await db.SaveChangesAsync();
        }

        using var response = await admin.PatchAsJsonAsync($"{Users}/{memberId}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("SecurityStamp", await response.Content.ReadAsStringAsync());
        var actual = (await admin.GetFromJsonAsync<AdminUserDetailResponse>($"{Users}/{memberId}"))!;
        Assert.True(actual.User.IsActive);
    }

    private async Task<Guid> CreateUserAsync(string name, string role, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = $"{name}@example.test", Email = $"{name}@example.test", DisplayName = name, IsActive = active };
        Assert.True((await identity.CreateAsync(user)).Succeeded);
        Assert.True((await identity.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private async Task<Guid> CreateGameAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new Game { Title = "Game mẫu", Slug = "game-mau", Price = 9_007_199_254_740_992m, Status = GameStatus.Published };
        db.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private HttpClient Client(Guid id, string? roleClaim = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            IntegrationTestHelper.CreateTestJwtToken(id, role: roleClaim));
        return client;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, Guid gameId)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/orders", new { gameId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    public void Dispose() => _factory.Dispose();

    private sealed class ConcurrentFactory : CustomWebApplicationFactory
    {
        protected override bool IndependentConnections => true;
    }
}
