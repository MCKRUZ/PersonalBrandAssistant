using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentContext
{
    public required Guid ExecutionId { get; init; }
    public required BrandProfilePromptModel BrandProfile { get; init; }
    public ContentPromptModel? Content { get; init; }
    public required IPromptTemplateService PromptService { get; init; }
    public required ISidecarClient SidecarClient { get; init; }
    public string? SessionId { get; init; }
    public required Dictionary<string, string> Parameters { get; init; }
}
