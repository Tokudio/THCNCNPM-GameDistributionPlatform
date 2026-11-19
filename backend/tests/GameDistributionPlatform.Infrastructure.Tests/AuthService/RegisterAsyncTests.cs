using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService;

/// <summary>
/// Kiểm thử toàn diện cho phương thức AuthService.RegisterAsync.
/// Bao gồm Unit Tests và Relational Integration Tests với SQLite In-Memory.
/// </summary>
public sealed class RegisterAsyncTests
{
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly Mock<ILogger<GameDistributionPlatform.Infrastructure.Services.AuthService>> _loggerMock = new();

    #region 5.1. Successful Registration (Relational Integration Test)

    [Fact]
    public async Task RegisterAsync_WithValidRequest_ReturnsUserInfoResponseAndPersistsUserWithRoleUser()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();
        using var identityHelper = new RealIdentityTestHelper(context);

        await identityHelper.SeedRolesAsync();

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            identityHelper.UserManager,
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "gamer@example.com",
            Password = "Password123!",
            DisplayName = "  ProGamer99  "
        };

        var beforeCall = DateTime.UtcNow.AddSeconds(-2);

        // Act
        var result = await authService.RegisterAsync(request);

        var afterCall = DateTime.UtcNow.AddSeconds(2);

        // Assert - 1. Return DTO
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("gamer@example.com", result.Email);
        Assert.Equal("ProGamer99", result.DisplayName); // Trimmed
        Assert.Single(result.Roles);
        Assert.Contains(SeedConstants.AppRoles.User, result.Roles);
        Assert.DoesNotContain(SeedConstants.AppRoles.Admin, result.Roles);

        // Assert - 2. Persistent Database State (Xác minh qua AppDbContext mới độc lập)
        using var verifyContext = db.CreateContext();
        var savedUser = await verifyContext.Users.FirstOrDefaultAsync(u => u.Email == "gamer@example.com");

        Assert.NotNull(savedUser);
        Assert.Equal(result.Id, savedUser.Id);
        Assert.Equal("gamer@example.com", savedUser.UserName);
        Assert.Equal("gamer@example.com", savedUser.Email);
        Assert.Equal("ProGamer99", savedUser.DisplayName);
        Assert.True(savedUser.IsActive);
        Assert.False(savedUser.EmailConfirmed);
        Assert.True(savedUser.CreatedAt >= beforeCall && savedUser.CreatedAt <= afterCall);

        // Password hash must not be plaintext and must verify with PasswordHasher
        Assert.False(string.IsNullOrWhiteSpace(savedUser.PasswordHash));
        Assert.DoesNotContain("Password123!", savedUser.PasswordHash);
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var verifyPassResult = passwordHasher.VerifyHashedPassword(savedUser, savedUser.PasswordHash, "Password123!");
        Assert.Equal(PasswordVerificationResult.Success, verifyPassResult);

        // Verify Identity UserRole relationship in DB: user has exactly one role and it is 'User'
        var userRoles = await verifyContext.UserRoles.Where(ur => ur.UserId == savedUser.Id).ToListAsync();
        var singleUserRole = Assert.Single(userRoles);

        var userRoleEntity = await verifyContext.Roles.FirstOrDefaultAsync(r => r.Id == singleUserRole.RoleId);
        Assert.NotNull(userRoleEntity);
        Assert.Equal(SeedConstants.AppRoles.User, userRoleEntity.Name);

        // Verify Admin role is NOT assigned in Database
        var adminRole = await verifyContext.Roles.FirstOrDefaultAsync(r => r.Name == SeedConstants.AppRoles.Admin);
        if (adminRole != null)
        {
            Assert.DoesNotContain(userRoles, ur => ur.RoleId == adminRole.Id);
        }

        // Verify JWT Service is NOT called during Registration
        _jwtServiceMock.Verify(j => j.GenerateToken(It.IsAny<UserInfoResponse>()), Times.Never);
    }

    #endregion

    #region 5.2. Duplicate Email Pre-Check

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ThrowsConflictExceptionAndDoesNotProceedToCreate()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            UserName = "existing@example.com",
            DisplayName = "Existing User"
        };

        builder.Mock
            .Setup(m => m.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(existingUser);

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "existing@example.com",
            Password = "Password123!",
            DisplayName = "Existing User"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() => authService.RegisterAsync(request));
        Assert.Equal("Email is already registered.", ex.Message);

        builder.Mock.Verify(m => m.FindByEmailAsync("existing@example.com"), Times.Once);
        builder.Mock.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        builder.Mock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region 5.3. Identity CreateAsync Failure

    [Fact]
    public async Task RegisterAsync_WhenCreateAsyncReturnsDuplicateEmailError_ThrowsConflictException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        builder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        builder.Mock
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateEmail",
                Description = "Email 'duplicate@example.com' is already taken."
            }));

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "duplicate@example.com",
            Password = "Password123!",
            DisplayName = "Duplicate"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() => authService.RegisterAsync(request));
        Assert.Equal("Email is already registered.", ex.Message);

        builder.Mock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenCreateAsyncReturnsDuplicateUserNameError_ThrowsConflictException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        builder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        builder.Mock
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = "Username 'duplicate@example.com' is already taken."
            }));

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "duplicate@example.com",
            Password = "Password123!",
            DisplayName = "Duplicate"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() => authService.RegisterAsync(request));
        Assert.Equal("Email is already registered.", ex.Message);

        builder.Mock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenCreateAsyncReturnsPasswordPolicyErrors_ThrowsBadRequestException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        builder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        builder.Mock
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(
                new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." },
                new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." }));

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "user@example.com",
            Password = "weak",
            DisplayName = "User"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => authService.RegisterAsync(request));
        Assert.Contains("User creation failed:", ex.Message);
        Assert.Contains("Passwords must be at least 8 characters.", ex.Message);
        Assert.Contains("Passwords must have at least one digit ('0'-'9').", ex.Message);

        builder.Mock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenPasswordFailsRealIdentityPolicy_ThrowsBadRequestExceptionAndPersistsNoUser()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();
        using var identityHelper = new RealIdentityTestHelper(context);

        await identityHelper.SeedRolesAsync();

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            identityHelper.UserManager,
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        // Mật khẩu "123" vi phạm Identity policy thực tế (độ dài < 6, thiếu chữ hoa, ký tự thường, số theo policy)
        var request = new RegisterRequest
        {
            Email = "weakpass@example.com",
            Password = "123",
            DisplayName = "Weak Password User"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => authService.RegisterAsync(request));
        Assert.Contains("User creation failed:", ex.Message);

        // Xác minh không có User hay UserRole nào được ghi xuống database
        using var verifyContext = db.CreateContext();
        var userInDb = await verifyContext.Users.FirstOrDefaultAsync(u => u.Email == "weakpass@example.com");
        Assert.Null(userInDb);

        var anyUserRoles = await verifyContext.UserRoles.AnyAsync();
        Assert.False(anyUserRoles);
    }

    #endregion

    #region 5.4. Role Assignment Failure & Rollback (Relational Fault Injection)

    [Fact]
    public async Task RegisterAsync_WhenRoleAssignmentFails_RollsBackTransactionAndThrowsInternalServerException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();
        using var identityHelper = new RealIdentityTestHelper(context);

        await identityHelper.SeedRolesAsync();

        // Fault Injection bằng Mock với CallBase = true:
        // - CreateAsync và FindByEmailAsync chạy BASE method thực tế, sử dụng real UserStore & SQLite connection.
        // - Dữ liệu User thực sự được INSERT vào database bên trong transaction.
        // - Chỉ riêng AddToRoleAsync bị can thiệp để trả về IdentityResult.Failed nhằm kiểm thử nhánh logic gán role thất bại.
        var realStore = identityHelper.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();

        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            realStore,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions { User = { RequireUniqueEmail = true } }),
            new PasswordHasher<ApplicationUser>(),
            new List<IUserValidator<ApplicationUser>>(),
            new List<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            identityHelper.ServiceProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            CallBase = true
        };

        var userExistedDuringTransaction = false;

        userManagerMock
            .Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), SeedConstants.AppRoles.User))
            .Callback<ApplicationUser, string>((u, r) =>
            {
                // Xác minh trực tiếp trong database connection của transaction:
                // Thao tác CreateAsync đã thực sự lưu row của User vào AspNetUsers trước khi AddToRoleAsync được gọi!
                userExistedDuringTransaction = context.Users.AsNoTracking().Any(dbUser => dbUser.Id == u.Id && dbUser.Email == "norole@example.com");
            })
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "RoleAssignmentFailed",
                Description = "Simulated role assignment failure."
            }));

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            userManagerMock.Object,
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "norole@example.com",
            Password = "Password123!",
            DisplayName = "No Role User"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InternalServerException>(() => authService.RegisterAsync(request));
        Assert.Equal("User created but role assignment failed.", ex.Message);

        // Xác nhận thao tác tạo user đã thực sự diễn ra bên trong Transaction trước khi gán role thất bại
        Assert.True(userExistedDuringTransaction);
        userManagerMock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), SeedConstants.AppRoles.User), Times.Once);

        // Xác minh Transaction Rollback:
        // Dù CreateAsync đã thực sự insert user vào DB trong transaction, nhưng vì rollback nên User KHÔNG CÒN trong DB!
        using var verifyContext = db.CreateContext();
        var userInDb = await verifyContext.Users.FirstOrDefaultAsync(u => u.Email == "norole@example.com");
        Assert.Null(userInDb);

        var anyUserRoles = await verifyContext.UserRoles.AnyAsync();
        Assert.False(anyUserRoles);
    }

    #endregion

    #region 5.5. Transaction Rollback On Unexpected Exceptions

    [Fact]
    public async Task RegisterAsync_WhenUnexpectedExceptionOccursInTransaction_RollsBackTransactionAndRethrowsOriginalException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        builder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var expectedEx = new InvalidOperationException("Simulated unexpected crash during user creation.");
        builder.Mock
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ThrowsAsync(expectedEx);

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "crash@example.com",
            Password = "Password123!",
            DisplayName = "Crash Test"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(request));
        Assert.Same(expectedEx, ex);

        // Transaction đã được mở và rollback an toàn
        using var verifyContext = db.CreateContext();
        var userInDb = await verifyContext.Users.FirstOrDefaultAsync(u => u.Email == "crash@example.com");
        Assert.Null(userInDb);
    }

    [Fact]
    public async Task RegisterAsync_WhenDbUpdateExceptionOccursNotMatchingSqlException_RollsBackAndRethrowsOriginalDbUpdateException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        builder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var expectedEx = new DbUpdateException("Simulated generic DbUpdateException (e.g. timeout or disk error).");
        builder.Mock
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ThrowsAsync(expectedEx);

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "dbcrash@example.com",
            Password = "Password123!",
            DisplayName = "Db Crash Test"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => authService.RegisterAsync(request));
        Assert.Same(expectedEx, ex);

        using var verifyContext = db.CreateContext();
        var userInDb = await verifyContext.Users.FirstOrDefaultAsync(u => u.Email == "dbcrash@example.com");
        Assert.Null(userInDb);
    }

    #endregion

    #region 5.6. CancellationToken

    [Fact]
    public async Task RegisterAsync_WhenCancellationTokenAlreadyCanceled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var db = new SqliteAuthTestDb();
        using var context = db.CreateContext();

        var builder = MockUserManagerBuilder.Create();
        builder.Mock
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var authService = new GameDistributionPlatform.Infrastructure.Services.AuthService(
            builder.Build(),
            context,
            _jwtServiceMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequest
        {
            Email = "cancel@example.com",
            Password = "Password123!",
            DisplayName = "Cancelled"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // SQLite / EF Core BeginTransactionAsync throws OperationCanceledException when token is already cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authService.RegisterAsync(request, cts.Token));

        builder.Mock.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    #endregion
}
