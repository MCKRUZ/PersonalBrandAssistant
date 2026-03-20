using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

internal sealed partial class RssFeedPoller(
    IHttpClientFactory httpClientFactory,
    ILogger<RssFeedPoller> logger) : ITrendSourcePoller
{
    private const int MaxItemsPerFeed = 50;
    private const int MaxTitleLength = 500;
    private const int MaxDescriptionLength = 4000;
    private const int MaxUrlLength = 2000;

    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";

    public TrendSourceType SourceType => TrendSourceType.RssFeed;

    public async Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source.FeedUrl))
        {
            logger.LogWarning("RssFeed source {SourceId} has no FeedUrl configured", source.Id);
            return [];
        }

        var client = httpClientFactory.CreateClient("RssFeed");

        using var response = await client.GetAsync(source.FeedUrl, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore });

        var feed = SyndicationFeed.Load(reader);
        if (feed is null)
        {
            logger.LogWarning("Failed to parse RSS/Atom feed from {FeedUrl}", source.FeedUrl);
            return [];
        }

        return feed.Items
            .Take(MaxItemsPerFeed)
            .Select(item =>
            {
                var rawUrl = item.Links.FirstOrDefault()?.Uri?.AbsoluteUri
                    ?? (Uri.IsWellFormedUriString(item.Id, UriKind.Absolute) ? item.Id : null);
                var itemUrl = Truncate(rawUrl, MaxUrlLength);
                return new TrendItem
                {
                    Title = Truncate(item.Title?.Text ?? "", MaxTitleLength)!,
                    Description = Truncate(StripHtml(item.Summary?.Text), MaxDescriptionLength),
                    Url = itemUrl,
                    SourceName = source.Name,
                    SourceType = TrendSourceType.RssFeed,
                    TrendSourceId = source.Id,
                    ThumbnailUrl = ExtractThumbnailUrl(item, itemUrl),
                    DetectedAt = (item.PublishDate != default
                        ? item.PublishDate
                        : (item.LastUpdatedTime != default ? item.LastUpdatedTime : DateTimeOffset.UtcNow))
                        .ToUniversalTime(),
                };
            })
            .ToList();
    }

    private static string? ExtractThumbnailUrl(SyndicationItem item, string? itemUrl)
    {
        // Try YouTube URL pattern first
        if (itemUrl is not null)
        {
            var match = YouTubeVideoIdRegex().Match(itemUrl);
            if (match.Success)
            {
                return $"https://img.youtube.com/vi/{match.Groups[1].Value}/hqdefault.jpg";
            }
        }

        // Try media:thumbnail from element extensions
        foreach (var ext in item.ElementExtensions)
        {
            try
            {
                if (ext.OuterName == "thumbnail" && ext.OuterNamespace == MediaNs.NamespaceName)
                {
                    var element = ext.GetObject<XElement>();
                    var url = element.Attribute("url")?.Value;
                    if (!string.IsNullOrWhiteSpace(url))
                        return url.Length <= 500 ? url : url[..500];
                }
            }
            catch
            {
                // Skip malformed extensions
            }
        }

        return null;
    }

    private static string? StripHtml(string? html)
    {
        if (html is null) return null;
        var text = HtmlTagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = CollapseWhitespaceRegex().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    [GeneratedRegex(@"(?:v=|youtu\.be/)([a-zA-Z0-9_-]{11})")]
    private static partial Regex YouTubeVideoIdRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespaceRegex();
}
