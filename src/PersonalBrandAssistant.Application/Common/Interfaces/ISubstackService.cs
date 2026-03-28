using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Parses Substack RSS feed into structured post data.</summary>
public interface ISubstackService
{
    Task<Result<IReadOnlyList<SubstackPost>>> GetRecentPostsAsync(
        int limit, CancellationToken ct);

    Task<Result<FeedFetchResult>> FetchFeedEntriesAsync(
        string? etag, DateTimeOffset? ifModifiedSince, CancellationToken ct);
}
