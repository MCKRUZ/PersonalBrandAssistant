namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentOutput
{
    public required string GeneratedText { get; init; }
    public string? Title { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public bool CreatesContent { get; init; }
    public List<AgentOutputItem> Items { get; init; } = [];
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheCreationTokens { get; init; }
}

public record AgentOutputItem(
    string Text,
    string? Title,
    Dictionary<string, string> Metadata);
