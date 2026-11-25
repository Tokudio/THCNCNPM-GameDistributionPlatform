using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Admin;

public sealed class AdminUserServiceTests
{
    [Fact]
    public async Task ListsOnlyMatchingUsersWithRolesAndAccurateCounts()
    {
        using var fixture = new SqliteAuthTestDb();
        await using var db = fixture.CreateContext();
        using var identity = new RealIdentityTestHelper(db);
        await identity.SeedRolesAsync();
        var users = new AdminUserService(db, identity.UserManager);
        var active = new ApplicationUser { DisplayName = "Linh", UserName = "linh@example.test", Email = "linh@example.test" };
        var inactive = new ApplicationUser { DisplayName = "Minh", UserName = "minh@example.test", Email = "minh@example.test", IsActive = false };
        Assert.True((await identity.UserManager.CreateAsync(active)).Succeeded);
        Assert.True((await identity.UserManager.CreateAsync(inactive)).Succeeded);
        Assert.True((await identity.UserManager.AddToRoleAsync(active, "Admin")).Succeeded);
        Assert.True((await identity.UserManager.AddToRoleAsync(inactive, "User")).Succeeded);

        var result = await users.GetUsersAsync(new AdminUserQuery { Search = "EXAMPLE", IsActive = false, Role = "user", PageSize = 1 }, default);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(inactive.Id, Assert.Single(result.Items).Id);
        var detail = await users.GetUserAsync(active.Id, default);
        Assert.Equal(new[] { "Admin" }, detail.User.Roles);
        Assert.Equal(0, detail.OwnedGames);
        Assert.Equal(0, detail.TotalOrders);
        await Assert.ThrowsAsync<NotFoundException>(() => users.GetUserAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<BadRequestException>(() => users.GetUsersAsync(new AdminUserQuery { Role = "Owner" }, default));
    }
}
