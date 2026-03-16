namespace PersonalBrandAssistant.Application.Common.Models;

public abstract record SidecarEvent;

public record ChatEvent(string EventType, string? Text, string? FilePath, string? ToolName) : SidecarEvent;

public record FileChangeEvent(string FilePath, string ChangeType) : SidecarEvent;

public record StatusEvent(string Status) : SidecarEvent;

public record SessionUpdateEvent(string SessionId) : SidecarEvent;

public record TaskCompleteEvent(
    string SessionId,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens = 0,
    int CacheCreationTokens = 0,
    decimal Cost = 0m) : SidecarEvent;

public record ErrorEvent(string Message) : SidecarEvent;
