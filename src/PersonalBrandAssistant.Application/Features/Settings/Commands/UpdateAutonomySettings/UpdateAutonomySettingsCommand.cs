using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Features.Settings.Commands.UpdateAutonomySettings;

public sealed record UpdateAutonomySettingsCommand(
    AutonomyLevel GlobalLevel,
    bool AutoPublishEnabled,
    bool RequireApprovalForSocial,
    int MaxAutoPostsPerDay,
    string DefaultTone,
    bool AutoScheduleEnabled,
    int AutoPublishThreshold = 90) : IRequest<Result<AutonomySettingsResponse>>;
