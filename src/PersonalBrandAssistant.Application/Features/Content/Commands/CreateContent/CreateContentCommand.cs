using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;

public sealed record CreateContentCommand(
    ContentType ContentType,
    string Body,
    string? Title = null,
    PlatformType[]? TargetPlatforms = null,
    ContentMetadata? Metadata = null) : IRequest<Result<Guid>>;
