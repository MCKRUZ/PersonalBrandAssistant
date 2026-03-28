namespace PersonalBrandAssistant.Application.Common.Models;

public record GitCommitResult(string CommitSha, string CommitUrl, bool Success, string? Error);
