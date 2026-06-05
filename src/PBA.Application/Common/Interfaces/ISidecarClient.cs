namespace PBA.Application.Common.Interfaces;

public interface ISidecarClient
{
    /// <summary>Sends a prompt to the configured AI backend.</summary>
    /// <param name="systemPrompt">The system-level instruction context.</param>
    /// <param name="userPrompt">The user-facing prompt to complete.</param>
    /// <param name="model">
    /// Optional model override. Honored by <see cref="OpenRouterClient"/>; ignored by
    /// CLI-based implementations (e.g. SidecarClient) which cannot switch models at runtime.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamPromptAsync(
        Guid contentId,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
