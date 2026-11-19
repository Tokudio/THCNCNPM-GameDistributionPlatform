using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;

/// <summary>
/// Helper cấu hình và quản lý ASP.NET Core Identity pipeline thực tế trên SQLite AppDbContext.
/// Đảm bảo tính nhất quán cấu hình với Program.cs (RequireUniqueEmail = true).
/// Đăng ký AppDbContext dưới dạng Singleton instance từ bên ngoài để Caller kiểm soát vòng đời,
/// tránh việc DI container tự ý Dispose instance DbContext dùng chung với Caller.
/// </summary>
public sealed class RealIdentityTestHelper : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private bool _disposed;

    public AppDbContext DbContext { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public RoleManager<IdentityRole<Guid>> RoleManager { get; }
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;

    public RealIdentityTestHelper(AppDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

        var services = new ServiceCollection();

        // Logging
        services.AddLogging();

        // Đăng ký instance DbContext từ bên ngoài để DI container không chịu trách nhiệm Dispose nó,
        // đồng thời đảm bảo UserManager và AuthService luôn dùng chung một instance DbContext
        services.AddSingleton<AppDbContext>(DbContext);

        // Cấu hình ASP.NET Core Identity tương đương Program.cs
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>();

        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        IServiceScope? scope = null;
        try
        {
            scope = serviceProvider.CreateScope();
            UserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            RoleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            _serviceProvider = serviceProvider;
            _scope = scope;
        }
        catch
        {
            scope?.Dispose();
            serviceProvider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Seeding các roles mặc định của hệ thống (User và Admin).
    /// </summary>
    public async Task SeedRolesAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var roles = new[] { SeedConstants.AppRoles.User, SeedConstants.AppRoles.Admin };

        foreach (var roleName in roles)
        {
            if (!await RoleManager.RoleExistsAsync(roleName))
            {
                var result = await RoleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Failed to seed role '{roleName}': {errors}");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _scope.Dispose();
        _serviceProvider.Dispose();
    }
}
