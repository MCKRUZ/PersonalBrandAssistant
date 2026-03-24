namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IImagePromptService
{
    Task<string> GeneratePromptAsync(string postContent, CancellationToken ct);
}
