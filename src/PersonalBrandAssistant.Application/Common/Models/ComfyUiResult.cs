namespace PersonalBrandAssistant.Application.Common.Models;

public record ComfyUiResult(
    bool Success,
    string? OutputFilename,
    string? OutputSubfolder,
    string? Error);
