namespace PersonalBrandAssistant.Application.Common.Models;

public class BlogChatOptions
{
    public const string SectionName = "BlogChat";

    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 4096;
    public string SystemPromptPath { get; set; } = "prompts/blog-chat-system.md";
    public int RecentMessageCount { get; set; } = 20;
    public int FinalizationMaxRetries { get; set; } = 3;
}
