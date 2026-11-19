using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Authorization;

public sealed class RoleAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RoleAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RoleRefresh_InOnTokenValidated_DirectlyUpdatesClaimsPrincipalFromDatabase()
    {
        // Arrange - Tạo user có role "User" trong Database
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var jwtOptions = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        var email = $"refresh_comp_{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = "Refresh Component User",
            IsActive = true
        };
        await userManager.CreateAsync(user, "Password123!");
        await userManager.AddToRoleAsync(user, SeedConstants.AppRoles.User);

        // Tạo HttpContext và TokenValidatedContext
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };

        // Giả lập ClaimsPrincipal ban đầu với role CŨ ("OldStaleRole") đã lỗi thời trong token
        var initialClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("role", "OldStaleRole")
        };
        var identity = new ClaimsIdentity(initialClaims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);

        var authScheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var tokenValidatedContext = new TokenValidatedContext(httpContext, authScheme, jwtOptions)
        {
            Principal = principal
        };

        // Act 1 - Thực thi OnTokenValidated khi user mới chỉ có role "User"
        Assert.NotNull(jwtOptions.Events?.OnTokenValidated);
        await jwtOptions.Events.OnTokenValidated(tokenValidatedContext);

        // Assert 1 - Role "OldStaleRole" đã bị xóa bỏ, thay bằng role "User" từ database
        var currentRoles1 = tokenValidatedContext.Principal!.FindAll("role").Select(c => c.Value).ToList();
        Assert.DoesNotContain("OldStaleRole", currentRoles1);
        Assert.Single(currentRoles1);
        Assert.Contains(SeedConstants.AppRoles.User, currentRoles1);

        // Act 2 - Cấp thêm role "Admin" trong Database sau khi token đã phát hành
        await userManager.AddToRoleAsync(user, SeedConstants.AppRoles.Admin);
        await jwtOptions.Events.OnTokenValidated(tokenValidatedContext);

        // Assert 2 - ClaimsPrincipal được refresh trực tiếp: có cả "User" và "Admin"
        var currentRoles2 = tokenValidatedContext.Principal!.FindAll("role").Select(c => c.Value).ToList();
        Assert.Equal(2, currentRoles2.Count);
        Assert.Contains(SeedConstants.AppRoles.User, currentRoles2);
        Assert.Contains(SeedConstants.AppRoles.Admin, currentRoles2);

        // Act 3 - Thu hồi role "Admin" trong Database
        await userManager.RemoveFromRoleAsync(user, SeedConstants.AppRoles.Admin);
        await jwtOptions.Events.OnTokenValidated(tokenValidatedContext);

        // Assert 3 - ClaimsPrincipal ngay lập tức mất role "Admin"
        var currentRoles3 = tokenValidatedContext.Principal!.FindAll("role").Select(c => c.Value).ToList();
        Assert.Single(currentRoles3);
        Assert.Contains(SeedConstants.AppRoles.User, currentRoles3);
        Assert.DoesNotContain(SeedConstants.AppRoles.Admin, currentRoles3);
    }

    [Fact]
    public async Task RoleRefresh_ViaHttpEndpoint_ReflectsDynamicDatabaseRoleChanges()
    {
        // Arrange - Đăng ký user thông thường (có role "User")
        var email = $"dynamic_role_{Guid.NewGuid():N}@example.com";
        var password = "Password123!";
        var regRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = "Dynamic Role Player"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);

        // Login để nhận AccessToken mang role "User" ban đầu
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // 1. Kiểm tra /me lần 1: chỉ có role "User"
        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me"))
        {
            req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            var res1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
            var user1 = await res1.Content.ReadFromJsonAsync<UserInfoResponse>();
            Assert.NotNull(user1);
            Assert.Single(user1.Roles);
            Assert.Contains(SeedConstants.AppRoles.User, user1.Roles);
        }

        // 2. Cấp thêm quyền "Admin" trong Database
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            await userManager.AddToRoleAsync(user, SeedConstants.AppRoles.Admin);
        }

        // 3. Sử dụng NGUYÊN TOKEN CŨ gọi lại /me: Middleware OnTokenValidated tự động refresh role
        using (var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me"))
        {
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            var res2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
            var user2 = await res2.Content.ReadFromJsonAsync<UserInfoResponse>();
            Assert.NotNull(user2);
            Assert.Equal(2, user2.Roles.Count);
            Assert.Contains(SeedConstants.AppRoles.User, user2.Roles);
            Assert.Contains(SeedConstants.AppRoles.Admin, user2.Roles);
        }

        // 4. Thu hồi quyền "Admin" trong Database
        using (var revokeScope = _factory.Services.CreateScope())
        {
            var userManager = revokeScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            await userManager.RemoveFromRoleAsync(user, SeedConstants.AppRoles.Admin);
        }

        // 5. Dùng nguyên token cũ gọi lại /me: role đã thu hồi phải biến mất khỏi ClaimsPrincipal
        using (var req3 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me"))
        {
            req3.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            var res3 = await _client.SendAsync(req3);
            Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
            var user3 = await res3.Content.ReadFromJsonAsync<UserInfoResponse>();
            Assert.NotNull(user3);
            Assert.Single(user3.Roles);
            Assert.Contains(SeedConstants.AppRoles.User, user3.Roles);
            Assert.DoesNotContain(SeedConstants.AppRoles.Admin, user3.Roles);
        }
    }
}

