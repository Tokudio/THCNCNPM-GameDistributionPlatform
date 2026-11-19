using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService;

/// <summary>
/// Kiểm thử toàn diện cho phương thức AuthService.LoginAsync.
/// Sử dụng xUnit và Moq, bao phủ các kịch bản: Successful Login, Failed Login (Security), Dependency Failures.
/// </summary>
public sealed class LoginAsyncTests : IDisposable
{
    private readonly MockUserManagerBuilder _userManagerBuilder = MockUserManagerBuilder.Create();
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly Mock<ILogger<GameDistributionPlatform.Infrastructure.Services.AuthService>> _loggerMock = new();

    // DbContext dummy không kết nối DB (LoginAsync không sử dụng _dbContext)
    private readonly AppDbContext _dummyDbContext = new(new DbContextOptionsBuilder<AppDbContext>().Options);

    public void Dispose()
    {
        _dummyDbContext.Dispose();
    }

    private GameDistributionPlatform.Infrastructure.Services.AuthService CreateAuthService()
    {
        return new GameDistributionPlatform.Infrastructure.Services.AuthService(
            _userManagerBuilder.Build(),
            _dummyDbContext,
            _jwtServiceMock.Object,
            _loggerMock.Object);
    }

    #region 6.1. Successful Login

    [Fact]
    public async Task LoginAsync_WithValidCredentialsAndActiveUser_ReturnsAuthResponseAndInvokesJwtService()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "gamer@example.com",
            UserName = "gamer@example.com",
            DisplayName = "EpicGamer",
            IsActive = true
        };

        var roles = new List<string> { "User", "Admin" };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByEmailAsync("gamer@example.com"))
            .ReturnsAsync(user);

        _userManagerBuilder.Mock
            .Setup(m => m.CheckPasswordAsync(user, "StrongP@ss1"))
            .ReturnsAsync(true);

        _userManagerBuilder.Mock
            .Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(roles);

        var expectedAuthResponse = new AuthResponse
        {
            AccessToken = "valid.jwt.token",
            TokenType = "Bearer",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60)
        };

        _jwtServiceMock
            .Setup(j => j.GenerateToken(It.Is<UserInfoResponse>(u =>
                u.Id == userId &&
                u.Email == "gamer@example.com" &&
                u.DisplayName == "EpicGamer" &&
                u.Roles.SequenceEqual(roles))))
            .Returns(expectedAuthResponse);

        var authService = CreateAuthService();
        var request = new LoginRequest
        {
            Email = "gamer@example.com",
            Password = "StrongP@ss1"
        };

        // Act
        var result = await authService.LoginAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("valid.jwt.token", result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(expectedAuthResponse.ExpiresAt, result.ExpiresAt);

        _userManagerBuilder.Mock.Verify(m => m.FindByEmailAsync("gamer@example.com"), Times.Once);
        _userManagerBuilder.Mock.Verify(m => m.CheckPasswordAsync(user, "StrongP@ss1"), Times.Once);
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(user), Times.Once);
        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Once);
    }

    #endregion

    #region 6.2. Failed Login & Security Logging

    [Theory]
    [InlineData("notfound@example.com", "Password123!", false, false, "User not found")]
    [InlineData("wrongpass@example.com", "WrongPassword", true, false, "Password incorrect")]
    [InlineData("inactive@example.com", "Password123!", true, true, "User inactive")]
    public async Task LoginAsync_WhenCredentialsOrStatusInvalid_ThrowsUnauthorizedExceptionAndLogsWarning(
        string email,
        string password,
        bool userExists,
        bool passwordMatches,
        string scenario)
    {
        // Arrange
        ApplicationUser? user = userExists
            ? new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                DisplayName = "Test User",
                IsActive = scenario != "User inactive" // false if scenario is inactive
            }
            : null;

        _userManagerBuilder.Mock
            .Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);

        if (user != null)
        {
            _userManagerBuilder.Mock
                .Setup(m => m.CheckPasswordAsync(user, password))
                .ReturnsAsync(passwordMatches);
        }

        var authService = CreateAuthService();
        var request = new LoginRequest
        {
            Email = email,
            Password = password
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => authService.LoginAsync(request));
        Assert.Equal("Invalid email or password.", ex.Message);

        // Verify that JWT token is NEVER generated
        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Never);

        // Verify CheckPasswordAsync is NEVER called if user does not exist
        if (!userExists)
        {
            _userManagerBuilder.Mock.Verify(m => m.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        }

        // Verify GetRolesAsync is NEVER called on invalid auth
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);

        // Verify Warning Log occurs without sensitive information (chứa thông điệp chung, không chứa email hoặc password)
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Authentication failed for login request.") &&
                    !v.ToString()!.Contains(email) &&
                    !v.ToString()!.Contains(password)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region 6.3. Dependency Failures

    [Fact]
    public async Task LoginAsync_WhenFindByEmailAsyncThrows_PropagatesException()
    {
        // Arrange
        _userManagerBuilder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Database connection timeout during user lookup."));

        var authService = CreateAuthService();
        var request = new LoginRequest { Email = "error@example.com", Password = "Password123!" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.LoginAsync(request));
        Assert.Equal("Database connection timeout during user lookup.", ex.Message);

        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenCheckPasswordAsyncThrows_PropagatesException()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", IsActive = true };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);

        _userManagerBuilder.Mock
            .Setup(m => m.CheckPasswordAsync(user, It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Hasher failure."));

        var authService = CreateAuthService();
        var request = new LoginRequest { Email = "user@example.com", Password = "Password123!" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.LoginAsync(request));
        Assert.Equal("Hasher failure.", ex.Message);

        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenGetRolesAsyncThrows_PropagatesException()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", IsActive = true };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);

        _userManagerBuilder.Mock
            .Setup(m => m.CheckPasswordAsync(user, "Password123!"))
            .ReturnsAsync(true);

        _userManagerBuilder.Mock
            .Setup(m => m.GetRolesAsync(user))
            .ThrowsAsync(new InvalidOperationException("Roles query failed."));

        var authService = CreateAuthService();
        var request = new LoginRequest { Email = "user@example.com", Password = "Password123!" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.LoginAsync(request));
        Assert.Equal("Roles query failed.", ex.Message);

        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenJwtServiceThrows_PropagatesException()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", IsActive = true };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);

        _userManagerBuilder.Mock
            .Setup(m => m.CheckPasswordAsync(user, "Password123!"))
            .ReturnsAsync(true);

        _userManagerBuilder.Mock
            .Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _jwtServiceMock
            .Setup(j => j.GenerateToken(It.IsAny<UserInfoResponse>()))
            .Throws(new InvalidOperationException("JWT signing key expired or invalid."));

        var authService = CreateAuthService();
        var request = new LoginRequest { Email = "user@example.com", Password = "Password123!" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.LoginAsync(request));
        Assert.Equal("JWT signing key expired or invalid.", ex.Message);
    }

    #endregion
}
