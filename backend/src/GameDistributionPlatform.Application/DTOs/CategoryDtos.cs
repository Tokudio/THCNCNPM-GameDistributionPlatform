using System.ComponentModel.DataAnnotations;

namespace GameDistributionPlatform.Application.DTOs;

public sealed record CategoryResponse(
    Guid Id, string Name, string Slug, string? Description, bool IsActive, bool IsDefault);

public sealed class SaveCategoryRequest
{
    [Required, StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(150), RegularExpression(CatalogInput.SlugPattern)]
    public string Slug { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    public bool IsActive { get; init; } = true;
}
