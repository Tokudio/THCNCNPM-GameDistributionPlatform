using System.ComponentModel.DataAnnotations;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Application.DTOs;

public sealed record GameSummaryResponse(
    Guid Id, string Title, string Slug, decimal Price, string Currency, int DiscountPercent,
    GameStatus Status, string? CoverImageUrl, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record GameMediaResponse(Guid Id, string MediaUrl, MediaType Type, int DisplayOrder);

public sealed record GameResponse(
    Guid Id, string Title, string Slug, string Description, decimal Price, string Currency,
    int DiscountPercent, GameStatus Status, string? CoverImageUrl, string? BannerImageUrl,
    string? TrailerUrl, string? Publisher, string? Developer, DateTime? ReleaseDate,
    string? MinimumRequirements, string? RecommendedRequirements, DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<CategoryResponse> Categories, IReadOnlyList<GameMediaResponse> Media);

public sealed record PageResponse<T>(
    int PageNumber, int PageSize, int TotalCount, int TotalPages, IReadOnlyList<T> Items);

public enum GameSort { Newest, Title, PriceAsc, PriceDesc }

public class GameQuery : IValidatableObject
{
    [Range(1, 1_000_000)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 12;

    [StringLength(200)]
    public string? Search { get; init; }

    public Guid? CategoryId { get; init; }

    [Range(typeof(decimal), "0", CatalogInput.MaximumPrice, ParseLimitsInInvariantCulture = true)]
    public decimal? MinPrice { get; init; }

    [Range(typeof(decimal), "0", CatalogInput.MaximumPrice, ParseLimitsInInvariantCulture = true)]
    public decimal? MaxPrice { get; init; }

    [EnumDataType(typeof(GameSort))]
    public GameSort Sort { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
        {
            yield return new ValidationResult("Minimum price must not exceed maximum price.", [nameof(MinPrice)]);
        }
    }
}

public sealed class AdminGameQuery : GameQuery
{
    [EnumDataType(typeof(GameStatus))]
    public GameStatus? Status { get; init; }
}

public sealed class SaveGameRequest : IValidatableObject
{
    [Required, StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(200), RegularExpression(CatalogInput.SlugPattern)]
    public string Slug { get; init; } = string.Empty;

    [Required]
    public string Description { get; init; } = string.Empty;

    [Required, Range(typeof(decimal), "0", CatalogInput.MaximumPrice, ParseLimitsInInvariantCulture = true)]
    public decimal? Price { get; init; }

    [Required, RegularExpression("^[A-Z]{3}$")]
    public string Currency { get; init; } = "VND";

    [Range(0, 100)]
    public int DiscountPercent { get; init; }

    [StringLength(500)]
    public string? CoverImageUrl { get; init; }

    [StringLength(500)]
    public string? BannerImageUrl { get; init; }

    [StringLength(500)]
    public string? TrailerUrl { get; init; }

    [StringLength(100)]
    public string? Publisher { get; init; }

    [StringLength(100)]
    public string? Developer { get; init; }

    public DateTime? ReleaseDate { get; init; }
    public string? MinimumRequirements { get; init; }
    public string? RecommendedRequirements { get; init; }

    [Required, MaxLength(100)]
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    [Required, MaxLength(100)]
    public IReadOnlyList<SaveGameMediaRequest> Media { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Price is decimal price && decimal.Round(price, 2) != price)
        {
            yield return new ValidationResult("Price must have at most two decimal places.", [nameof(Price)]);
        }

        if (CategoryIds.Distinct().Count() != CategoryIds.Count || CategoryIds.Contains(Guid.Empty))
        {
            yield return new ValidationResult("Choose distinct, valid category IDs.", [nameof(CategoryIds)]);
        }

        foreach (var (field, value) in new[]
                 { (nameof(CoverImageUrl), CoverImageUrl), (nameof(BannerImageUrl), BannerImageUrl), (nameof(TrailerUrl), TrailerUrl) })
        {
            if (value is not null && CatalogInput.WebUri(value) is null)
            {
                yield return new ValidationResult("Enter an absolute HTTP or HTTPS URL.", [field]);
            }
        }

        var media = new HashSet<(MediaType, string)>();
        for (var i = 0; i < Media.Count; i++)
        {
            var item = Media[i];
            if (item is null || item.MediaUrl is not { Length: > 0 and <= 500 } ||
                CatalogInput.WebUri(item.MediaUrl) is not Uri uri ||
                !Enum.IsDefined(item.Type) || item.DisplayOrder < 0)
            {
                yield return new ValidationResult(
                    "Each media item needs a valid HTTP or HTTPS URL, type and nonnegative display order.",
                    [$"{nameof(Media)}[{i}]"]);
                continue;
            }

            if (!media.Add((item.Type, uri.AbsoluteUri)))
            {
                yield return new ValidationResult("Media URL and type pairs must be unique.", [$"{nameof(Media)}[{i}]"]);
            }
        }
    }
}

public sealed class SaveGameMediaRequest
{
    [Required, StringLength(500)]
    public string MediaUrl { get; init; } = string.Empty;

    [EnumDataType(typeof(MediaType))]
    public MediaType Type { get; init; }

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; init; }
}

public sealed class UpdateGameStatusRequest
{
    [Required, EnumDataType(typeof(GameStatus))]
    public GameStatus? Status { get; init; }
}
