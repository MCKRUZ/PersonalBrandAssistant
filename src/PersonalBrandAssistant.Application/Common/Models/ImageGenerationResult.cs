namespace PersonalBrandAssistant.Application.Common.Models;

public record ImageGenerationResult(bool Success, string? FileId, string? Error, long DurationMs);
