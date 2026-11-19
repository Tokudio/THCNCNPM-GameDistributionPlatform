using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Fixtures;

/// <summary>
/// Smoke Test xác thực CustomWebApplicationFactory khởi tạo thành công môi trường tích hợp WebApi:
/// - SQLite In-Memory thay thế hoàn toàn SQL Server.
/// - DatabaseSeeder khởi tạo Roles và Categories thành công mà không chạm SQL Server thật.
/// - HttpClient kết nối và nhận phản hồi bình thường.
/// </summary>
public sealed class WebApiIntegrationSmokeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public WebApiIntegrationSmokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Factory_ShouldInitializeSqliteDatabaseAndRunSeederSuccessfully()
    {
        // 1. Kiểm tra HttpClient hoạt động
        using var client = _factory.CreateClient();
        Assert.NotNull(client);

        // 2. Kiểm tra Database Scope và Provider
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Fail-fast guard: Xác nhận Provider là SQLite
        Assert.True(context.Database.IsSqlite());

        // 3. Xác nhận DatabaseSeeder đã seed Roles thành công
        var userRoleExists = await roleManager.RoleExistsAsync(SeedConstants.AppRoles.User);
        var adminRoleExists = await roleManager.RoleExistsAsync(SeedConstants.AppRoles.Admin);
        Assert.True(userRoleExists);
        Assert.True(adminRoleExists);

        // 4. Xác nhận DatabaseSeeder đã seed đúng 8 categories mặc định
        var categoriesCount = await context.Categories.CountAsync();
        Assert.Equal(8, categoriesCount);
    }
}

