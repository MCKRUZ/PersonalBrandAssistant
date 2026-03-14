using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentExecutionResult(
    Guid ExecutionId,
    AgentExecutionStatus Status,
    AgentOutput? Output,
    Guid? CreatedContentId);
