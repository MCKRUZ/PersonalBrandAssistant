using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record ContentStateChangedEvent(
    Guid ContentId,
    ContentStatus OldStatus,
    ContentStatus NewStatus) : IDomainEvent;
