using System.Net;
using System.Net.Http.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Authentication;

public sealed class LoginEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoginEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200OkWithAuthResponse()
    {
        // Arrange - Đăng ký user trước
        var email = $"login_valid_{Guid.NewGuid():N}@example.com";
        var password = "Password123!";
        var registerRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = "Login Gamer"
        };
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        // Act - Đăng nhập
        var loginRequest = new LoginRequest
        {
            Email = email,
            Password = password
        };
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.False(string.IsNullOrWhiteSpace(authResponse.AccessToken));
        Assert.Equal("Bearer", authResponse.TokenType);
        Assert.True(authResponse.ExpiresAt > DateTimeOffset.UtcNow);

        var rawContent = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, rawContent);
        Assert.DoesNotContain("passwordHash", rawContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401UnauthorizedWithGenericMessage()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = $"nonexistent_{Guid.NewGuid():N}@example.com",
            Password = "Password123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(response);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemDetails.Status);
        Assert.Equal("Invalid email or password.", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401UnauthorizedWithGenericMessage()
    {
        // Arrange - Đăng ký user
        var email = $"wrongpass_{Guid.NewGuid():N}@example.com";
        var regRequest = new RegisterRequest
        {
            Email = email,
            Password = "CorrectPassword123!",
            ConfirmPassword = "CorrectPassword123!",
            DisplayName = "User"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);

        // Act - Đăng nhập với mật khẩu sai
        var request = new LoginRequest
        {
            Email = email,
            Password = "WrongPassword999!"
        };
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(response);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemDetails.Status);
        Assert.Equal("Invalid email or password.", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithInactiveUser_Returns401UnauthorizedWithGenericMessage()
    {
        // Arrange - Đăng ký user sau đó set IsActive = false
        var email = $"inactive_{Guid.NewGuid():N}@example.com";
        var password = "Password123!";
        var regRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = "Inactive User"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        // Act - Thử đăng nhập với tài khoản bị vô hiệu hóa
        var loginRequest = new LoginRequest
        {
            Email = email,
            Password = password
        };
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(response);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemDetails.Status);
        // Không tiết lộ rằng tài khoản bị khóa, sử dụng cùng thông điệp chung
        Assert.Equal("Invalid email or password.", problemDetails.Detail);
    }

    [Theory]
    [InlineData("", "Password123!")]
    [InlineData("not-an-email", "Password123!")]
    [InlineData("user@example.com", "")]
    public async Task Login_WithInvalidDto_Returns400BadRequestValidationProblemDetails(string email, string password)
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = email,
            Password = password
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
