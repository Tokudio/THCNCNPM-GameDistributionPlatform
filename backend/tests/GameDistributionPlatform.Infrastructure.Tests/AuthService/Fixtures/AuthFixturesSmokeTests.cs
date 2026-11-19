using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;

/// <summary>
/// Smoke test xác thực tính hợp lệ của các Fixtures và Helpers theo mô hình Resource Ownership mới:
/// - MockUserManagerBuilder khởi tạo mock đúng chuẩn không phát sinh exception.
/// - SqliteAuthTestDb khởi tạo SQLite schema in-memory thành công và quản lý connection.
/// - RealIdentityTestHelper quản lý Identity stores và seed roles hệ thống thành công.
/// - Caller quản lý vòng đời DbContext (Dispose theo thứ tự: IdentityHelper -> DbContext -> SqliteAuthTestDb).
/// </summary>
public sealed class AuthFixturesSmokeTests
{
    [Fact]
    public async Task FixturesAndHelpers_ShouldInitializeAndFunctionCorrectly()
    {
        // 1. Verify MockUserManagerBuilder
        var builder = MockUserManagerBuilder.Create();
        var userManager = builder.Build();

        Assert.NotNull(builder.Mock);
        Assert.NotNull(builder.UserStoreMock);
        Assert.NotNull(userManager);

        // 2. Verify SqliteAuthTestDb and RealIdentityTestHelper với Resource Ownership phân định rõ ràng
        using var db = new SqliteAuthTestDb();

        // Thứ tự disposal chuẩn: Helper -> Context -> Db
        using (var initialContext = db.CreateContext())
        {
            using (var identityHelper = new RealIdentityTestHelper(initialContext))
            {
                Assert.NotNull(identityHelper.UserManager);
                Assert.NotNull(identityHelper.RoleManager);

                // Seed roles
                await identityHelper.SeedRolesAsync();

                var userRoleExists = await identityHelper.RoleManager.RoleExistsAsync(SeedConstants.AppRoles.User);
                var adminRoleExists = await identityHelper.RoleManager.RoleExistsAsync(SeedConstants.AppRoles.Admin);

                Assert.True(userRoleExists);
                Assert.True(adminRoleExists);
            } // identityHelper được dispose tại đây (giải phóng Scope & ServiceProvider), initialContext vẫn chưa bị dispose
        } // initialContext được dispose tại đây bởi Caller

        // 3. Verify cross-context persistence on same SQLite in-memory connection
        // Caller tạo context mới, xác nhận dữ liệu đã được lưu trên SQLite connection, sau đó tự dispose context này
        using (var verifyContext = db.CreateContext())
        {
            var roleCount = await verifyContext.Roles.CountAsync();
            Assert.Equal(2, roleCount);

            var roles = await verifyContext.Roles.Select(r => r.Name).ToListAsync();
            Assert.Contains(SeedConstants.AppRoles.User, roles);
            Assert.Contains(SeedConstants.AppRoles.Admin, roles);
        } // verifyContext được dispose tại đây bởi Caller
    } // db (SqliteAuthTestDb) được dispose sau cùng tại đây
}
