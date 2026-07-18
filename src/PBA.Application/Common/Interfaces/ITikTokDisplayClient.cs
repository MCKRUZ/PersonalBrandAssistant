namespace PBA.Application.Common.Interfaces;

// Thin seam over open.tiktokapis.com v2 Display API. /v2/video/list/ caps at 20/page, so the facade loops
// on cursor/has_more (via GetVideoPageAsync) to accumulate up to N videos. No reach/demographics/retention
// are available on TikTok — all TikTok trends are snapshot deltas only.
public interface ITikTokDisplayClient
{
    // /v2/user/info/ — follower_count, following_count, likes_count, video_count as a name -> value map.
    Task<IReadOnlyDictionary<string, long>> GetUserStatsAsync(string accessToken, CancellationToken ct);

    // One page of /v2/video/list/ (<=20 videos) plus the cursor/has_more for the next page.
    Task<TikTokVideoPage> GetVideoPageAsync(string accessToken, string? cursor, CancellationToken ct);
}

public record TikTokVideoPage(IReadOnlyList<TikTokVideo> Videos, string? NextCursor, bool HasMore);

public record TikTokVideo(string Id, string? Title, long Views, long Likes, long Comments, long Shares);
