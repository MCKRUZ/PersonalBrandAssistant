using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IAgentCapability
{
    AgentCapabilityType Type { get; }
    ModelTier DefaultModelTier { get; }
    Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct);
}
