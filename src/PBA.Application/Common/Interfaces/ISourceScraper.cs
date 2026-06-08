using PBA.Domain.Entities;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Fetches new items for a single idea source. Implementations are registered with keyed DI
/// keyed by <see cref="PBA.Domain.Enums.IdeaSourceType"/>. Must never throw for an expected
/// failure (network, parse) — return an empty list and let the caller record the failure.
/// </summary>
public interface ISourceScraper
{
    Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default);
}
