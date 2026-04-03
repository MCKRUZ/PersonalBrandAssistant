using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public sealed record AutonomySettingsResponse(
    Guid Id,
    AutonomyLevel GlobalLevel,
    bool AutoPublishEnabled,
    bool RequireApprovalForSocial,
    int MaxAutoPostsPerDay,
    string DefaultTone,
    bool AutoScheduleEnabled)
{
    public static AutonomySettingsResponse Default => new(
        Guid.Empty,
        AutonomyLevel.SemiAuto,
        AutoPublishEnabled: false,
        RequireApprovalForSocial: true,
        MaxAutoPostsPerDay: 5,
        DefaultTone: "Professional",
        AutoScheduleEnabled: false);
}
