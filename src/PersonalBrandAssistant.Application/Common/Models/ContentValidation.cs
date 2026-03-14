namespace PersonalBrandAssistant.Application.Common.Models;

public record ContentValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
