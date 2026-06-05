namespace PBA.Application.Common.Interfaces;

public interface ISidecarClient
{
    Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamPromptAsync(
        Guid contentId,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
