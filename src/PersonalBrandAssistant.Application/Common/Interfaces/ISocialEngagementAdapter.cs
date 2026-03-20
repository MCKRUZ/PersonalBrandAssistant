using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISocialEngagementAdapter
{
    PlatformType Platform { get; }
    Task<Result<IReadOnlyList<EngagementTarget>>> FindRelevantPostsAsync(
        string targetCriteriaJson, int maxResults, CancellationToken ct);
    Task<Result<string>> PostCommentAsync(string postId, string text, CancellationToken ct);
    Task<Result<IReadOnlyList<InboxEntry>>> PollInboxAsync(DateTimeOffset? since, CancellationToken ct);
    Task<Result<string>> SendReplyAsync(string platformItemId, string text, CancellationToken ct);
}

public record EngagementTarget(
    string PostId,
    string PostUrl,
    string Title,
    string Content,
    string Community);

public record InboxEntry(
    string PlatformItemId,
    InboxItemType ItemType,
    string AuthorName,
    string AuthorProfileUrl,
    string Content,
    string SourceUrl,
    DateTimeOffset ReceivedAt);
