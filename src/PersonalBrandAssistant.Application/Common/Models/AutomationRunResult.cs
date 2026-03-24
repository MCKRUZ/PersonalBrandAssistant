namespace PersonalBrandAssistant.Application.Common.Models;

public record AutomationRunResult(
    bool Success,
    Guid RunId,
    Guid? PrimaryContentId,
    string? ImageFileId,
    int PlatformVersionCount,
    string? Error,
    long DurationMs);
