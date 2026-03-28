using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record ContentPublishedEvent(Guid ContentId, PlatformType[] Platforms) : IDomainEvent;
