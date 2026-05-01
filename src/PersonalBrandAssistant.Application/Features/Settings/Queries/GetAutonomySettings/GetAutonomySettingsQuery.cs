using MediatR;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Settings.Queries.GetAutonomySettings;

public sealed record GetAutonomySettingsQuery : IRequest<Result<AutonomySettingsResponse>>;
