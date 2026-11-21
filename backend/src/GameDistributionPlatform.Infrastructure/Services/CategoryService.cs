using System.Data;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class CategoryService(AppDbContext db) : ICategoryService
{
    private const string DuplicateSlug = "This category slug is already in use.";
    private static readonly HashSet<string> DefaultSlugs =
        SeedConstants.DefaultCategories.Select(category => category.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(category => includeInactive || category.IsActive)
            .OrderBy(category => category.Name).ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);

        return categories.Select(ToResponse).ToArray();
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, bool includeInactive, CancellationToken cancellationToken)
    {
        var category = await db.Categories.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && (includeInactive || item.IsActive), cancellationToken)
            ?? throw new NotFoundException("Category was not found.");

        return ToResponse(category);
    }

    public async Task<CategoryResponse> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(request);
        await EnsureUniqueSlugAsync(request.Slug, null, cancellationToken);

        var category = new Category();
        Apply(category, request);
        db.Categories.Add(category);
        await CatalogStore.SaveAsync(db, DuplicateSlug, cancellationToken);
        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(request);
        var category = await FindAsync(id, cancellationToken);
        if (DefaultSlugs.Contains(category.Slug) &&
            !string.Equals(category.Slug, request.Slug, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("The slug of a default category is fixed. You can deactivate the category.");
        }

        await EnsureUniqueSlugAsync(request.Slug, id, cancellationToken);
        Apply(category, request);
        category.UpdatedAt = DateTime.UtcNow;
        await CatalogStore.SaveAsync(db, DuplicateSlug, cancellationToken);
        return ToResponse(category);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // Keep the assignment check and delete together because the existing FK cascades links.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var category = await FindAsync(id, cancellationToken);
            if (DefaultSlugs.Contains(category.Slug))
            {
                throw new ConflictException("Default categories can be deactivated.");
            }
            if (await db.GameCategories.AnyAsync(link => link.CategoryId == id, cancellationToken))
            {
                throw new ConflictException("This category is assigned to a game. Remove its assignments before deleting it.");
            }

            db.Categories.Remove(category);
            await CatalogStore.SaveAsync(db, DuplicateSlug, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error) when (CatalogStore.IsSqlError(error, 1205))
        {
            throw new ConflictException("Category assignments changed during this operation. Reload and try again.");
        }
    }

    internal static CategoryResponse ToResponse(Category category) =>
        new(category.Id, category.Name, category.Slug, category.Description, category.IsActive, DefaultSlugs.Contains(category.Slug));

    private async Task<Category> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Categories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundException("Category was not found.");

    private async Task EnsureUniqueSlugAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await db.Categories.AnyAsync(item => item.Slug == slug && item.Id != exceptId, cancellationToken))
        {
            throw new ConflictException(DuplicateSlug);
        }
    }

    private static void Apply(Category category, SaveCategoryRequest request)
    {
        category.Name = request.Name.Trim();
        category.Slug = request.Slug;
        category.Description = request.Description?.Trim();
        category.IsActive = request.IsActive;
    }
}
