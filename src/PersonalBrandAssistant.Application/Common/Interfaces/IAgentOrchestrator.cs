using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IAgentOrchestrator
{
    Task<Result<AgentExecutionResult>> ExecuteAsync(AgentTask task, CancellationToken ct);
    Task<Result<AgentExecution>> GetExecutionStatusAsync(Guid executionId, CancellationToken ct);
    Task<Result<AgentExecution[]>> ListExecutionsAsync(Guid? contentId, CancellationToken ct);
}
