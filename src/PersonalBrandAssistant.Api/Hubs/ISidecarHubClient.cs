namespace PersonalBrandAssistant.Api.Hubs;

public interface ISidecarHubClient
{
    Task ReceiveTokenBatch(string streamId, IReadOnlyList<string> tokens);
    Task StreamComplete(string streamId, bool isDraft, string fullText);
    Task StreamError(string streamId, string error);
    Task TypingStarted(string streamId);
    Task TokenUsage(string streamId, int inputTokens, int outputTokens, decimal cost);
}
