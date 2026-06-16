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

    /// <summary>
    /// Returns one embedding vector per input, in input order. Empty/whitespace inputs are skipped
    /// (never sent to the API), so the result length may be less than the input length — callers should
    /// pass only non-empty inputs when positional alignment matters. Batches internally in chunks of
    /// EmbeddingOptions.BatchSize and passes dimensions=1536 (R-M3). Never returns a zero or NaN vector:
    /// a corrupt embedding from the API surfaces as an exception so the caller can leave the item
    /// unembedded for retry (R-H2). Honored by <see cref="OpenRouterClient"/>; CLI-based implementations
    /// throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamPromptAsync(
        Guid contentId,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
