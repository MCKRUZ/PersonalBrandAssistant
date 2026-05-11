using PBA.Domain.Enums;

namespace PBA.Application.Features.Content;

public static class PlatformConstraints
{
    private static readonly IReadOnlyDictionary<Platform, int> CharacterLimits =
        new Dictionary<Platform, int>
        {
            [Platform.Blog] = 50_000,
            [Platform.Substack] = 50_000,
            [Platform.LinkedIn] = 3_000,
            [Platform.Twitter] = 280,
            [Platform.Reddit] = 40_000,
            [Platform.YouTube] = 5_000,
        };

    public static int GetCharacterLimit(Platform platform)
        => CharacterLimits.TryGetValue(platform, out var limit) ? limit : 50_000;

    public static string GetConstraintDescription(Platform platform)
        => $"{platform} ({GetCharacterLimit(platform)} character limit)";
}
