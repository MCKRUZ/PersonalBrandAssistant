using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record ContentRejectedEvent(Guid ContentId, string Feedback) : IDomainEvent;
