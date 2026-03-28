using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record AgentExecutionCompletedEvent(Guid ExecutionId, Guid? ContentId) : IDomainEvent;
