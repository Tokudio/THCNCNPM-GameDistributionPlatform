using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        IJwtService jwtService,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<UserInfoResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Kiểm tra Email trước khi mở Transaction
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            throw new ConflictException("Email is already registered.");
        }

        // 2. Mở Database Transaction
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var isRolledBack = false;

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName.Trim(),
            IsActive = true,
            EmailConfirmed = false,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            // 3. Tạo User qua UserManager
            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                await SafeRollbackAsync(transaction);
                isRolledBack = true;

                if (createResult.Errors.Any(e => e.Code == "DuplicateEmail" || e.Code == "DuplicateUserName"))
                {
                    throw new ConflictException("Email is already registered.");
                }

                var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                throw new BadRequestException($"User creation failed: {errors}");
            }

            // 4. Gán Role User mặc định
            var roleResult = await _userManager.AddToRoleAsync(user, SeedConstants.AppRoles.User);
            if (!roleResult.Succeeded)
            {
                await SafeRollbackAsync(transaction);
                isRolledBack = true;

                _logger.LogCritical("Failed to assign 'User' role to user {UserId}.", user.Id);
                throw new InternalServerException("User created but role assignment failed.");
            }

            // 5. Commit Transaction khi cả hai thao tác thành công
            await transaction.CommitAsync(cancellationToken);

            return new UserInfoResponse
            {
                Id = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                Roles = new[] { SeedConstants.AppRoles.User }
            };
        }
        catch (DbUpdateException ex)
        {
            if (!isRolledBack)
            {
                await SafeRollbackAsync(transaction);
                isRolledBack = true;
            }

            // Xử lý vi phạm Unique Constraint từ SQL Server (Error 2601 hoặc 2627)
            if (FindSqlException(ex) is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
            {
                // Sau khi Rollback, kiểm tra lại Email có thực sự tồn tại trong Database hay không
                var duplicateCheck = await _userManager.FindByEmailAsync(request.Email);
                if (duplicateCheck != null)
                {
                    _logger.LogWarning("Concurrent registration detected: Email is already registered in database.");
                    throw new ConflictException("Email is already registered.");
                }
            }

            // Không phải lỗi trùng Email -> ném lại Exception gốc để bảo toàn lỗi hệ thống
            _logger.LogError(ex, "Unexpected database error occurred during registration.");
            throw;
        }
        catch (Exception)
        {
            if (!isRolledBack)
            {
                await SafeRollbackAsync(transaction);
                isRolledBack = true;
            }

            throw;
        }
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password) || !user.IsActive)
        {
            // Không ghi Password, JWT hoặc Email người dùng vào log
            _logger.LogWarning("Authentication failed for login request.");
            throw new UnauthorizedException("Invalid email or password.");
        }

        var roles = await _userManager.GetRolesAsync(user);

        var userInfo = new UserInfoResponse
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Roles = roles.ToList()
        };

        return _jwtService.GenerateToken(userInfo);
    }

    public async Task<UserInfoResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null || !user.IsActive)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);

        return new UserInfoResponse
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Roles = roles.ToList()
        };
    }

    private async Task SafeRollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Ghi log lỗi rollback nhưng không ném lại để tránh che khuất ngoại lệ gốc
            _logger.LogError(ex, "An error occurred while rolling back the database transaction.");
        }
    }

    private static SqlException? FindSqlException(Exception? ex)
    {
        while (ex != null)
        {
            if (ex is SqlException sqlEx)
            {
                return sqlEx;
            }
            ex = ex.InnerException;
        }
        return null;
    }
}
