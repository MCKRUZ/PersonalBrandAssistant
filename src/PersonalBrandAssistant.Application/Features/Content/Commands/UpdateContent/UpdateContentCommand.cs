using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;

public sealed record UpdateContentCommand(
    Guid Id,
    string? Title = null,
    string? Body = null,
    PlatformType[]? TargetPlatforms = null,
    ContentMetadata? Metadata = null,
    uint Version = 0) : IRequest<Result<Unit>>;
