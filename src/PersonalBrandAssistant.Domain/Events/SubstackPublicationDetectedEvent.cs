using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record SubstackPublicationDetectedEvent(
    Guid ContentId, string SubstackUrl, DateTimeOffset PublishedAt) : IDomainEvent;
