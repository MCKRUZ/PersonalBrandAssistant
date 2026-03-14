using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record ContentApprovedEvent(Guid ContentId) : IDomainEvent;
