using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService;

/// <summary>
/// Kiểm thử toàn diện cho phương thức AuthService.GetCurrentUserAsync.
/// Bao phủ các kịch bản: Successful Retrieval, Invalid User (null/inactive), CancellationToken handling, Dependency Failures.
/// </summary>
public sealed class GetCurrentUserAsyncTests : IDisposable
{
    private readonly MockUserManagerBuilder _userManagerBuilder = MockUserManagerBuilder.Create();
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly Mock<ILogger<GameDistributionPlatform.Infrastructure.Services.AuthService>> _loggerMock = new();

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

    #region 7.1. Successful Retrieval

    [Fact]
    public async Task GetCurrentUserAsync_WhenUserExistsAndIsActive_ReturnsUserInfoResponseWithRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "active@example.com",
            UserName = "active@example.com",
            DisplayName = "Active Player",
            IsActive = true
        };

        var roles = new List<string> { "User", "Admin" };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerBuilder.Mock
            .Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(roles);

        var authService = CreateAuthService();

        // Act
        var result = await authService.GetCurrentUserAsync(userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.Id);
        Assert.Equal("active@example.com", result.Email);
        Assert.Equal("Active Player", result.DisplayName);
        Assert.Equal(2, result.Roles.Count);
        Assert.Contains("User", result.Roles);
        Assert.Contains("Admin", result.Roles);

        _userManagerBuilder.Mock.Verify(m => m.FindByIdAsync(userId.ToString()), Times.Once);
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(user), Times.Once);
        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Never);
    }

    #endregion

    #region 7.2. Invalid User (NotFound or Inactive)

    [Fact]
    public async Task GetCurrentUserAsync_WhenUserDoesNotExist_ReturnsNullAndDoesNotFetchRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerBuilder.Mock
            .Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var authService = CreateAuthService();

        // Act
        var result = await authService.GetCurrentUserAsync(userId);

        // Assert
        Assert.Null(result);

        _userManagerBuilder.Mock.Verify(m => m.FindByIdAsync(userId.ToString()), Times.Once);
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WhenUserIsInactive_ReturnsNullAndDoesNotFetchRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "inactive@example.com",
            UserName = "inactive@example.com",
            DisplayName = "Banned User",
            IsActive = false
        };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        var authService = CreateAuthService();

        // Act
        var result = await authService.GetCurrentUserAsync(userId);

        // Assert
        Assert.Null(result);

        _userManagerBuilder.Mock.Verify(m => m.FindByIdAsync(userId.ToString()), Times.Once);
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    #endregion

    #region 7.3. CancellationToken Handling

    [Fact]
    public async Task GetCurrentUserAsync_WhenTokenCanceledBeforeLookup_ThrowsOperationCanceledExceptionWithoutQueryingUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var authService = CreateAuthService();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel ngay từ đầu trước khi gọi

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => authService.GetCurrentUserAsync(userId, cts.Token));

        // Phải dừng ngay tại điểm kiểm tra token đầu tiên, không được gọi FindByIdAsync
        _userManagerBuilder.Mock.Verify(m => m.FindByIdAsync(It.IsAny<string>()), Times.Never);
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WhenTokenCanceledDuringLookup_ThrowsOperationCanceledExceptionWithoutFetchingRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "user@example.com",
            IsActive = true
        };

        using var cts = new CancellationTokenSource();

        // Dùng Callback deterministic: khi FindByIdAsync đang thực thi, trigger cancel token
        _userManagerBuilder.Mock
            .Setup(m => m.FindByIdAsync(userId.ToString()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(user);

        var authService = CreateAuthService();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => authService.GetCurrentUserAsync(userId, cts.Token));

        // FindByIdAsync đã được gọi, nhưng token bị cancel nên điểm check thứ 2 phát hiện và chặn GetRolesAsync
        _userManagerBuilder.Mock.Verify(m => m.FindByIdAsync(userId.ToString()), Times.Once);
        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    #endregion

    #region 7.4. Dependency Failures

    [Fact]
    public async Task GetCurrentUserAsync_WhenFindByIdAsyncThrows_PropagatesException()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerBuilder.Mock
            .Setup(m => m.FindByIdAsync(userId.ToString()))
            .ThrowsAsync(new InvalidOperationException("User store connection error."));

        var authService = CreateAuthService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.GetCurrentUserAsync(userId));
        Assert.Equal("User store connection error.", ex.Message);

        _userManagerBuilder.Mock.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WhenGetRolesAsyncThrows_PropagatesException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "user@example.com",
            IsActive = true
        };

        _userManagerBuilder.Mock
            .Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerBuilder.Mock
            .Setup(m => m.GetRolesAsync(user))
            .ThrowsAsync(new InvalidOperationException("User role store query failed."));

        var authService = CreateAuthService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.GetCurrentUserAsync(userId));
        Assert.Equal("User role store query failed.", ex.Message);
    }

    #endregion
}

