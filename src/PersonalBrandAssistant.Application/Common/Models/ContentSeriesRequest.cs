using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record ContentSeriesRequest(
    string Name,
    string? Description,
    string RecurrenceRule,
    PlatformType[] TargetPlatforms,
    ContentType ContentType,
    List<string> ThemeTags,
    string TimeZoneId,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);
