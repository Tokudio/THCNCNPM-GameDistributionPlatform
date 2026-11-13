using System.ComponentModel.DataAnnotations;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDistributionPlatform.Infrastructure.Data.Seeding;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(DatabaseSeeder));

        var context = serviceProvider.GetRequiredService<AppDbContext>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        logger.LogInformation("Starting database seeding process...");

        // 1. Kiểm tra kết nối Database
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            logger.LogCritical("Database connection check failed. Ensure SQL Server is accessible and database migrations have been applied.");
            throw new InvalidOperationException("Database connection check failed. Ensure SQL Server is accessible and database migrations have been applied.");
        }

        // 2. Seed Roles
        await SeedRolesAsync(roleManager, logger);

        // 3. Seed Categories
        await SeedCategoriesAsync(context, logger);

        // 4. Bootstrap Admin
        await BootstrapAdminAsync(userManager, roleManager, configuration, logger);

        logger.LogInformation("Database seeding process completed successfully.");
    }

    public static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager, ILogger logger)
    {
        string[] roles = [SeedConstants.AppRoles.Admin, SeedConstants.AppRoles.User];

        foreach (var roleName in roles)
        {
            var exists = await roleManager.RoleExistsAsync(roleName);
            if (!exists)
            {
                logger.LogInformation("Role '{RoleName}' does not exist. Creating...", roleName);
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                    logger.LogError("Failed to create role '{RoleName}'. Errors: {Errors}", roleName, errors);
                    throw new InvalidOperationException($"Failed to create role '{roleName}': {errors}");
                }
                logger.LogInformation("Role '{RoleName}' created successfully.", roleName);
            }
            else
            {
                logger.LogInformation("Role '{RoleName}' already exists. Skipping.", roleName);
            }
        }
    }

    public static async Task SeedCategoriesAsync(AppDbContext context, ILogger logger)
    {
        var existingSlugs = await context.Categories
            .Select(c => c.Slug)
            .ToListAsync();

        var categoriesToSeed = SeedConstants.DefaultCategories
            .Where(c => !existingSlugs.Contains(c.Slug))
            .ToList();

        if (categoriesToSeed.Count > 0)
        {
            logger.LogInformation("Seeding {Count} new categories...", categoriesToSeed.Count);
            await context.Categories.AddRangeAsync(categoriesToSeed);
            await context.SaveChangesAsync();
            logger.LogInformation("Categories seeded successfully.");
        }
        else
        {
            logger.LogInformation("All default categories already exist. Skipping category seeding.");
        }
    }

    public static async Task BootstrapAdminAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        var displayName = configuration["BootstrapAdmin:DisplayName"];

        // Nếu thiếu bất kỳ cấu hình nào -> bỏ qua tạo Admin
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
        {
            logger.LogInformation("Bootstrap Admin configuration is missing or incomplete (BootstrapAdmin:Email, Password, DisplayName). Skipping Admin bootstrap.");
            return;
        }

        // 1. Kiểm tra tính hợp lệ của định dạng email
        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            logger.LogWarning("BootstrapAdmin:Email format '{Email}' is invalid. Skipping Admin bootstrap.", email);
            return;
        }

        // 2. Kiểm tra tài khoản đã tồn tại chưa
        var user = await userManager.FindByEmailAsync(email);

        if (user == null)
        {
            // Tài khoản chưa tồn tại -> Tạo mới
            logger.LogInformation("Bootstrap Admin account '{Email}' does not exist. Creating...", email);

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                logger.LogError("Failed to create Bootstrap Admin user '{Email}'. Errors: {Errors}", email, errors);
                throw new InvalidOperationException($"Failed to create Bootstrap Admin: {errors}");
            }

            logger.LogInformation("Bootstrap Admin user '{Email}' created successfully. Assigning '{Role}' role...", email, SeedConstants.AppRoles.Admin);

            var addRoleResult = await userManager.AddToRoleAsync(user, SeedConstants.AppRoles.Admin);
            if (!addRoleResult.Succeeded)
            {
                var errors = string.Join("; ", addRoleResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                logger.LogError("Failed to assign '{Role}' role to Bootstrap Admin '{Email}'. Errors: {Errors}", SeedConstants.AppRoles.Admin, email, errors);
                throw new InvalidOperationException($"Failed to assign '{SeedConstants.AppRoles.Admin}' role to '{email}': {errors}");
            }

            logger.LogInformation("Bootstrap Admin account '{Email}' successfully configured with '{Role}' role.", email, SeedConstants.AppRoles.Admin);
        }
        else
        {
            // Tài khoản đã tồn tại -> Kiểm tra Role Admin
            var hasAdminRole = await userManager.IsInRoleAsync(user, SeedConstants.AppRoles.Admin);
            if (hasAdminRole)
            {
                logger.LogInformation("Admin account '{Email}' already exists and possesses the '{Role}' role. No action taken.", email, SeedConstants.AppRoles.Admin);
            }
            else
            {
                // Tài khoản tồn tại nhưng chưa có Role Admin -> Xác minh mật khẩu trước khi cấp quyền
                logger.LogWarning("Account '{Email}' exists but lacks '{Role}' role. Verifying bootstrap credentials...", email, SeedConstants.AppRoles.Admin);

                var passwordValid = await userManager.CheckPasswordAsync(user, password);
                if (passwordValid)
                {
                    var addRoleResult = await userManager.AddToRoleAsync(user, SeedConstants.AppRoles.Admin);
                    if (!addRoleResult.Succeeded)
                    {
                        var errors = string.Join("; ", addRoleResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                        logger.LogError("Failed to elevate user '{Email}' to '{Role}'. Errors: {Errors}", email, SeedConstants.AppRoles.Admin, errors);
                        throw new InvalidOperationException($"Failed to assign '{SeedConstants.AppRoles.Admin}' role to '{email}': {errors}");
                    }

                    logger.LogInformation("User '{Email}' verified with matching bootstrap credentials and successfully elevated to '{Role}' role.", email, SeedConstants.AppRoles.Admin);
                }
                else
                {
                    logger.LogWarning("User '{Email}' exists but the provided BootstrapAdmin password does not match. Refusing automatic elevation to '{Role}' role.", email, SeedConstants.AppRoles.Admin);
                }
            }
        }
    }
}

