using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record ContentScheduledEvent(Guid ContentId, DateTimeOffset ScheduledAt) : IDomainEvent;
