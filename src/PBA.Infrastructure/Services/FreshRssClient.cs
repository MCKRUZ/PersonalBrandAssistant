using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services;

public record RssFeed(string Id, string Title, string Url, string? Category);

public record RssEntry(
    string Id,
    string Title,
    string Content,
    string Url,
    string FeedTitle,
    string? ThumbnailUrl,
    IReadOnlyList<string> Categories,
    DateTimeOffset PublishedAt);

public class FreshRssClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<FreshRssOptions> _options;
    private readonly ILogger<FreshRssClient> _logger;
    private string? _authToken;

    public FreshRssClient(
        HttpClient httpClient,
        IOptionsMonitor<FreshRssOptions> options,
        ILogger<FreshRssClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public virtual async Task<string> AuthenticateAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", opts.Username),
            new KeyValuePair<string, string>("Passwd", opts.ApiPassword),
        });

        var response = await _httpClient.PostAsync(
            $"{opts.BaseUrl}/api/greader.php/accounts/ClientLogin", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("Auth=", StringComparison.Ordinal))
            {
                _authToken = line["Auth=".Length..].Trim();
                return _authToken;
            }
        }

        throw new InvalidOperationException("Auth token not found in FreshRSS login response");
    }

    public virtual async Task<IReadOnlyList<RssFeed>> GetSubscriptionsAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);
        var opts = _options.CurrentValue;

        using var request = CreateAuthRequest(HttpMethod.Get,
            $"{opts.BaseUrl}/api/greader.php/reader/api/0/subscription/list?output=json");
        var response = await SendWithReauthAsync(request, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var feeds = new List<RssFeed>();

        foreach (var sub in doc.RootElement.GetProperty("subscriptions").EnumerateArray())
        {
            var categories = sub.TryGetProperty("categories", out var cats)
                ? cats.EnumerateArray().Select(c => c.GetProperty("label").GetString()).FirstOrDefault()
                : null;

            feeds.Add(new RssFeed(
                sub.GetProperty("id").GetString()!,
                sub.GetProperty("title").GetString()!,
                sub.TryGetProperty("url", out var url) ? url.GetString()! : "",
                categories));
        }

        return feeds;
    }

    public virtual async Task<IReadOnlyList<RssEntry>> GetEntriesAsync(
        DateTimeOffset? newerThan = null,
        int? count = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);
        var opts = _options.CurrentValue;
        var batchSize = count ?? opts.BatchSize;
        var entries = new List<RssEntry>();
        string? continuation = null;

        do
        {
            var url = $"{opts.BaseUrl}/api/greader.php/reader/api/0/stream/contents/reading-list?output=json&n={batchSize}";
            if (newerThan.HasValue)
                url += $"&ot={newerThan.Value.ToUnixTimeSeconds()}";
            if (continuation != null)
                url += $"&c={Uri.EscapeDataString(continuation)}";

            using var request = CreateAuthRequest(HttpMethod.Get, url);
            var response = await SendWithReauthAsync(request, ct);

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    entries.Add(ParseEntry(item));
                }
            }

            continuation = doc.RootElement.TryGetProperty("continuation", out var cont)
                ? cont.GetString()
                : null;
        } while (continuation != null);

        return entries;
    }

    public virtual async Task MarkAsReadAsync(IEnumerable<string> entryIds, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);
        var opts = _options.CurrentValue;

        foreach (var id in entryIds)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("i", id),
                new KeyValuePair<string, string>("a", "user/-/state/com.google/read"),
            });

            using var request = CreateAuthRequest(HttpMethod.Post,
                $"{opts.BaseUrl}/api/greader.php/reader/api/0/edit-tag");
            request.Content = content;
            await SendWithReauthAsync(request, ct);
        }
    }

    internal static string NormalizeItemId(string rawId)
    {
        const string prefix = "tag:google.com,2005:reader/item/";
        if (rawId.StartsWith(prefix, StringComparison.Ordinal))
        {
            var suffix = rawId[prefix.Length..];
            if (long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var dec))
                return dec.ToString(CultureInfo.InvariantCulture);
            if (long.TryParse(suffix, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return hex.ToString(CultureInfo.InvariantCulture);
            return suffix;
        }

        return rawId;
    }

    private RssEntry ParseEntry(JsonElement item)
    {
        var rawId = item.GetProperty("id").GetString()!;
        var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var content = "";
        if (item.TryGetProperty("summary", out var summary) && summary.TryGetProperty("content", out var c))
            content = c.GetString() ?? "";

        var url = "";
        if (item.TryGetProperty("canonical", out var canonical) && canonical.GetArrayLength() > 0)
            url = canonical[0].GetProperty("href").GetString() ?? "";

        var feedTitle = "";
        if (item.TryGetProperty("origin", out var origin) && origin.TryGetProperty("title", out var ot))
            feedTitle = ot.GetString() ?? "";

        string? thumbnailUrl = null;
        if (item.TryGetProperty("enclosure", out var enc) && enc.GetArrayLength() > 0)
        {
            var href = enc[0].TryGetProperty("href", out var h) ? h.GetString() : null;
            if (href != null && (href.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                 href.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
                                 href.Contains(".webp", StringComparison.OrdinalIgnoreCase)))
                thumbnailUrl = href;
        }

        var categories = new List<string>();
        if (item.TryGetProperty("categories", out var cats))
        {
            foreach (var cat in cats.EnumerateArray())
            {
                var label = cat.GetString();
                if (label != null && !label.StartsWith("user/", StringComparison.Ordinal))
                    categories.Add(label);
            }
        }

        var published = item.TryGetProperty("published", out var pub)
            ? DateTimeOffset.FromUnixTimeSeconds(pub.GetInt64())
            : DateTimeOffset.UtcNow;

        return new RssEntry(
            NormalizeItemId(rawId),
            title,
            content,
            url,
            feedTitle,
            thumbnailUrl,
            categories,
            published);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_authToken == null)
            await AuthenticateAsync(ct);
    }

    private HttpRequestMessage CreateAuthRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (_authToken != null)
            request.Headers.TryAddWithoutValidation("Authorization", $"GoogleLogin auth={_authToken}");
        return request;
    }

    private async Task<HttpResponseMessage> SendWithReauthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("FreshRSS returned 401, re-authenticating");
            _authToken = null;
            await AuthenticateAsync(ct);

            using var retry = CreateAuthRequest(request.Method, request.RequestUri!.ToString());
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(ct);
                retry.Content = new StringContent(body, System.Text.Encoding.UTF8,
                    request.Content.Headers.ContentType?.MediaType ?? "application/x-www-form-urlencoded");
            }
            response = await _httpClient.SendAsync(retry, ct);
        }

        response.EnsureSuccessStatusCode();
        return response;
    }
}
