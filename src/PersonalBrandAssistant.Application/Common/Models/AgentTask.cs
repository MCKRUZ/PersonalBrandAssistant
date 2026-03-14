using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentTask(
    AgentCapabilityType Type,
    Guid? ContentId,
    Dictionary<string, string> Parameters);
