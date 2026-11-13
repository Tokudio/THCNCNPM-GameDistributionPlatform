using GameDistributionPlatform.Domain.Entities;

namespace GameDistributionPlatform.Infrastructure.Data.Seeding;

public static class SeedConstants
{
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string User = "User";
    }

    public static IReadOnlyList<Category> DefaultCategories => new List<Category>
    {
        new()
        {
            Name = "Action",
            Slug = "action",
            Description = "Fast-paced games involving physical challenges, combat, and quick reflexes.",
            IsActive = true
        },
        new()
        {
            Name = "Adventure",
            Slug = "adventure",
            Description = "Story-driven games focusing on exploration, puzzle solving, and narrative experiences.",
            IsActive = true
        },
        new()
        {
            Name = "RPG",
            Slug = "rpg",
            Description = "Role-playing games where players control characters in rich, detailed fictional settings.",
            IsActive = true
        },
        new()
        {
            Name = "Strategy",
            Slug = "strategy",
            Description = "Games emphasizing tactical, strategic, and logistical thinking to achieve victory.",
            IsActive = true
        },
        new()
        {
            Name = "Simulation",
            Slug = "simulation",
            Description = "Games designed to simulate real-world activities, vehicles, or systems.",
            IsActive = true
        },
        new()
        {
            Name = "Sports",
            Slug = "sports",
            Description = "Competitive athletic games simulating traditional sports and tournaments.",
            IsActive = true
        },
        new()
        {
            Name = "Racing",
            Slug = "racing",
            Description = "High-speed competitions driving land, air, or watercraft.",
            IsActive = true
        },
        new()
        {
            Name = "Horror",
            Slug = "horror",
            Description = "Suspenseful, eerie experiences designed to evoke fear, tension, and excitement.",
            IsActive = true
        }
    };
}

