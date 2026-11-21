using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<CategoryResponse> GetByIdAsync(Guid id, bool includeInactive, CancellationToken cancellationToken);
    Task<CategoryResponse> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken);
    Task<CategoryResponse> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
