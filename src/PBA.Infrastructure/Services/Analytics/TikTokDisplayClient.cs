using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Services.Analytics;

// Thin seam over open.tiktokapis.com v2 Display API. One page per GetVideoPageAsync (the facade loops on the
// cursor/has_more). Untested by design (the facade mocks ITikTokDisplayClient); needs a real-credential smoke
// test before prod. BaseAddress is set in DI.
public sealed class TikTokDisplayClient(HttpClient http, ILogger<TikTokDisplayClient> logger)
    : ITikTokDisplayClient
{
    private const int PageSize = 20;

    public async Task<IReadOnlyDictionary<string, long>> GetUserStatsAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "v2/user/info/?fields=follower_count,following_count,likes_count,video_count");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var user = (await SendAsync(request, ct)).GetProperty("data").GetProperty("user");

        var result = new Dictionary<string, long>();
        AddIfPresent(result, "followers", user, "follower_count");
        AddIfPresent(result, "following", user, "following_count");
        AddIfPresent(result, "likes", user, "likes_count");
        AddIfPresent(result, "videos", user, "video_count");
        return result;
    }

    public async Task<TikTokVideoPage> GetVideoPageAsync(string accessToken, string? cursor, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "v2/video/list/?fields=id,title,view_count,like_count,comment_count,share_count");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(cursor is not null && long.TryParse(cursor, out var c)
            ? new { max_count = PageSize, cursor = c }
            : (object)new { max_count = PageSize });

        var data = (await SendAsync(request, ct)).GetProperty("data");

        var videos = new List<TikTokVideo>();
        if (data.TryGetProperty("videos", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in arr.EnumerateArray())
            {
                videos.Add(new TikTokVideo(
                    Id: v.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Title: v.TryGetProperty("title", out var t) ? t.GetString() : null,
                    Views: ReadLong(v, "view_count"),
                    Likes: ReadLong(v, "like_count"),
                    Comments: ReadLong(v, "comment_count"),
                    Shares: ReadLong(v, "share_count")));
            }
        }

        var nextCursor = data.TryGetProperty("cursor", out var cur) && cur.ValueKind == JsonValueKind.Number
            ? cur.GetInt64().ToString()
            : null;
        var hasMore = data.TryGetProperty("has_more", out var hm) && hm.ValueKind is JsonValueKind.True or JsonValueKind.False
            && hm.GetBoolean();

        return new TikTokVideoPage(videos, nextCursor, hasMore);
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonSerializer.Deserialize<JsonElement>(json);

        // TikTok reports errors in the body even on 200; error.code == "ok" means success.
        if (root.TryGetProperty("error", out var error)
            && error.TryGetProperty("code", out var code)
            && code.GetString() is { } c && c != "ok")
        {
            logger.LogWarning("TikTok Display API error: {Code}", c);
            throw new InvalidOperationException($"TikTok Display API error: {c}");
        }

        return root;
    }

    private static void AddIfPresent(Dictionary<string, long> bag, string key, JsonElement obj, string field)
    {
        if (obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number)
            bag[key] = v.GetInt64();
    }

    private static long ReadLong(JsonElement obj, string field) =>
        obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
