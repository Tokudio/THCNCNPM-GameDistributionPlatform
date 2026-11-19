using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Authentication;

public sealed class CurrentUserEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CurrentUserEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCurrentUser_WithoutAuthorizationHeader_Returns401UnauthorizedChallenge()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");

        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(response);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemDetails.Status);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidJwtToken_Returns200OkWithUserInfoResponse()
    {
        // Arrange - Đăng ký và login để lấy JWT chuẩn
        var email = $"me_valid_{Guid.NewGuid():N}@example.com";
        var password = "Password123!";
        var regRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = "Current Player"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // Act - Gọi /api/v1/auth/me với Bearer token
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userInfo = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
        Assert.NotNull(userInfo);
        Assert.Equal(email, userInfo.Email);
        Assert.Equal("Current Player", userInfo.DisplayName);
        Assert.Single(userInfo.Roles);
        Assert.Contains("User", userInfo.Roles);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUserIsDeletedFromDatabase_Returns401Unauthorized()
    {
        // Arrange - Đăng ký và lấy token
        var email = $"me_deleted_{Guid.NewGuid():N}@example.com";
        var password = "Password123!";
        var regRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = "Deleted Player"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // Xóa user khỏi Database SQLite
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }

        // Act - Gửi request với token của user vừa bị xóa
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);

        // Assert - Middleware OnTokenValidated phát hiện user không tồn tại trong DB -> Fail() -> 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUserIsInactiveInDatabase_Returns401Unauthorized()
    {
        // Arrange - Đăng ký và lấy token
        var email = $"me_inactive_{Guid.NewGuid():N}@example.com";
        var password = "Password123!";
        var regRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = "Banned Player"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // Vô hiệu hóa user trong Database SQLite
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        // Act - Gửi request
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);

        // Assert - Middleware OnTokenValidated phát hiện user inactive -> Fail() -> 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
