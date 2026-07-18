using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Services.Analytics;

// Thin seam over graph.instagram.com (Instagram-Login). Maps insight metrics by RETURNED name, so any metric
// the API omits/deprecates simply doesn't appear in the bag (logged at Debug). Untested by design (the facade
// mocks IInstagramGraphClient); needs a real-credential smoke test before prod. BaseAddress is set in DI.
public sealed class InstagramGraphClient(HttpClient http, ILogger<InstagramGraphClient> logger)
    : IInstagramGraphClient
{
    public async Task<IReadOnlyDictionary<string, long>> GetAccountMetricsAsync(
        string accessToken, IReadOnlyList<string> metrics, CancellationToken ct)
    {
        var userId = await GetUserIdAsync(accessToken, ct);
        var result = new Dictionary<string, long>();

        // follower_count via the user node.
        var followers = await GetJsonAsync($"{userId}?fields=followers_count", accessToken, ct);
        if (followers.TryGetProperty("followers_count", out var fc) && fc.ValueKind == JsonValueKind.Number)
            result["followers"] = fc.GetInt64();

        // Account insights (metric_type=total_value, period=day). Map by returned name; dropped metrics absent.
        var metricCsv = string.Join(",", metrics);
        var insights = await GetJsonAsync(
            $"{userId}/insights?metric={Enc(metricCsv)}&metric_type=total_value&period=day", accessToken, ct);

        if (insights.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null)
                    continue;
                if (item.TryGetProperty("total_value", out var tv) && tv.TryGetProperty("value", out var val)
                    && val.ValueKind == JsonValueKind.Number)
                    result[name] = val.GetInt64();
                else
                    logger.LogDebug("Instagram metric {Metric} returned no total_value; skipped", name);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<InstagramMediaMetrics>> GetRecentMediaAsync(
        string accessToken, IReadOnlyList<string> mediaMetrics, int max, CancellationToken ct)
    {
        var list = await GetJsonAsync($"me/media?fields=id,caption&limit={max}", accessToken, ct);
        var media = new List<InstagramMediaMetrics>();

        if (!list.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return media;

        var metricCsv = string.Join(",", mediaMetrics);
        foreach (var item in data.EnumerateArray().Take(max))
        {
            var mediaId = item.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (mediaId is null)
                continue;
            var caption = item.TryGetProperty("caption", out var cap) ? cap.GetString() : null;

            var metrics = new Dictionary<string, long>();
            var insights = await GetJsonAsync($"{mediaId}/insights?metric={Enc(metricCsv)}", accessToken, ct);
            if (insights.TryGetProperty("data", out var mData) && mData.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mData.EnumerateArray())
                {
                    var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is null)
                        continue;
                    if (m.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
                        && values.EnumerateArray().FirstOrDefault() is { } first
                        && first.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number)
                        metrics[name] = val.GetInt64();
                }
            }

            media.Add(new InstagramMediaMetrics(mediaId, caption, metrics));
        }

        return media;
    }

    private async Task<string> GetUserIdAsync(string accessToken, CancellationToken ct)
    {
        var me = await GetJsonAsync("me?fields=user_id", accessToken, ct);
        if (me.TryGetProperty("user_id", out var uid) && uid.ValueKind == JsonValueKind.String)
            return uid.GetString()!;
        if (me.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString()!;
        throw new InvalidOperationException("Instagram /me returned no user id");
    }

    // The token goes in the Authorization header (not the query string) to avoid proxy/log capture.
    private async Task<JsonElement> GetJsonAsync(string relativeUrl, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static string Enc(string value) => Uri.EscapeDataString(value);
}
