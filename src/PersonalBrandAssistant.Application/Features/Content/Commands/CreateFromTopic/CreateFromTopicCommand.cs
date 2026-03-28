using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;

public sealed record CreateFromTopicCommand(
    ContentType ContentType,
    string Topic,
    string? Outline = null,
    PlatformType[]? TargetPlatforms = null,
    Guid? ParentContentId = null,
    Dictionary<string, string>? Parameters = null) : IRequest<Result<Guid>>;
