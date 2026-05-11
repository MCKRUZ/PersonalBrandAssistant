namespace PBA.Application.Common.Interfaces;

public interface ISidecarClient
{
    Task<string> SendPromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
