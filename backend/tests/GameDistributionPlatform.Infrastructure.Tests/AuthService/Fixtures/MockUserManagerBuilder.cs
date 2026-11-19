using GameDistributionPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;

/// <summary>
/// Builder cung cấp Mock UserManager<ApplicationUser> với đầy đủ constructor dependencies hợp lệ.
/// Không kết nối Database, không chứa business logic của test cases.
/// </summary>
public sealed class MockUserManagerBuilder
{
    public Mock<IUserStore<ApplicationUser>> UserStoreMock { get; private set; } = null!;
    public Mock<UserManager<ApplicationUser>> Mock { get; private set; } = null!;

    public MockUserManagerBuilder()
    {
        Initialize();
    }

    public static MockUserManagerBuilder Create() => new();

    /// <summary>
    /// Khởi tạo lại Mock instances mới hoàn toàn để tránh rò rỉ cấu hình giữa các test cases.
    /// </summary>
    public MockUserManagerBuilder Reset()
    {
        Initialize();
        return this;
    }

    /// <summary>
    /// Trả về instance UserManager<ApplicationUser> đã được mock.
    /// </summary>
    public UserManager<ApplicationUser> Build() => Mock.Object;

    private void Initialize()
    {
        UserStoreMock = new Mock<IUserStore<ApplicationUser>>();

        var options = Microsoft.Extensions.Options.Options.Create(new IdentityOptions());
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var keyNormalizer = new UpperInvariantLookupNormalizer();
        var errorDescriber = new IdentityErrorDescriber();
        var services = new Mock<IServiceProvider>();
        var logger = NullLogger<UserManager<ApplicationUser>>.Instance;

        Mock = new Mock<UserManager<ApplicationUser>>(
            UserStoreMock.Object,
            options,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errorDescriber,
            services.Object,
            logger);
    }
}
