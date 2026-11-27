using System.ComponentModel.DataAnnotations;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Application.DTOs;

public sealed class LibraryQuery
{
    [Range(1, 1_000_000)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 12;

    [StringLength(200)]
    public string? Search { get; init; }
}

public sealed record LibraryGameResponse(
    Guid GameId, string Title, string Slug, string? CoverImageUrl, GameStatus Status,
    DateTime AcquiredAt, IReadOnlyList<string> Categories)
{
    public bool IsOwned => true;
}

public sealed record LibraryGameDetailResponse(
    Guid GameId, string Title, string Slug, string? CoverImageUrl, GameStatus Status,
    DateTime AcquiredAt, IReadOnlyList<string> Categories, string? BannerImageUrl,
    string Description, string? Developer, string? Publisher, DateTime? ReleaseDate,
    string? MinimumRequirements, string? RecommendedRequirements)
{
    public bool IsOwned => true;
}

public sealed record LibraryDownloadInfoResponse(
    Guid GameId, bool IsDownloadAvailable, string? FileName, long? FileSize, string? Message)
{
    public bool IsOwned => true;
}
