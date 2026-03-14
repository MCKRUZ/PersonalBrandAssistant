using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Events;

public sealed record AgentExecutionFailedEvent(Guid ExecutionId, string Error) : IDomainEvent;
