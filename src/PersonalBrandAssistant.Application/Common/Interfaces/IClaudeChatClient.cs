namespace PersonalBrandAssistant.Application.Common.Interfaces;

public record ClaudeChatMessage(string Role, string Content);

public interface IClaudeChatClient
{
    IAsyncEnumerable<string> StreamMessageAsync(
        string model,
        int maxTokens,
        string systemPrompt,
        IReadOnlyList<ClaudeChatMessage> messages,
        CancellationToken ct);

    Task<string> SendMessageAsync(
        string model,
        int maxTokens,
        string systemPrompt,
        IReadOnlyList<ClaudeChatMessage> messages,
        CancellationToken ct);
}
