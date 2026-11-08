using GameDistributionPlatform.Domain.Common;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Domain.Entities;

public class GameMedia : BaseEntity
{
    public Guid GameId { get; set; }
    public Game Game { get; set; } = null!;

    public string MediaUrl { get; set; } = string.Empty;
    public MediaType Type { get; set; } = MediaType.Image;
    public int DisplayOrder { get; set; } = 0;
}

