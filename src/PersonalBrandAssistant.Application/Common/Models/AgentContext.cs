using Microsoft.Extensions.AI;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentContext
{
    public required Guid ExecutionId { get; init; }
    public required BrandProfilePromptModel BrandProfile { get; init; }
    public ContentPromptModel? Content { get; init; }
    public required IPromptTemplateService PromptService { get; init; }
    public required IChatClient ChatClient { get; init; }
    public required Dictionary<string, string> Parameters { get; init; }
    public required ModelTier ModelTier { get; init; }
}
