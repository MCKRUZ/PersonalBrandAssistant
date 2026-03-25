using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

internal sealed partial class SubstackService(
    HttpClient httpClient,
    IOptions<SubstackOptions> options,
    ILogger<SubstackService> logger) : ISubstackService
{
    private readonly SubstackOptions _options = options.Value;

    public async Task<Result<IReadOnlyList<SubstackPost>>> GetRecentPostsAsync(
        int limit, CancellationToken ct)
    {
        // SSRF protection: validate the URL host
        if (!IsValidSubstackHost(_options.FeedUrl))
        {
            logger.LogWarning(
                "Substack feed URL validation failed: {FeedUrl} is not a substack.com domain",
                _options.FeedUrl);
            return Result<IReadOnlyList<SubstackPost>>.Failure(
                ErrorCode.ValidationFailed,
                "Substack feed URL must be a substack.com domain");
        }

        try
        {
            using var response = await httpClient.GetAsync(_options.FeedUrl, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            var feed = SyndicationFeed.Load(reader);
            if (feed is null)
            {
                logger.LogWarning("Failed to parse Substack RSS feed from {FeedUrl}", _options.FeedUrl);
                return Result<IReadOnlyList<SubstackPost>>.Failure(
                    ErrorCode.InternalError,
                    "Failed to parse Substack RSS feed");
            }

            var posts = feed.Items
                .Select(item => new SubstackPost(
                    Title: item.Title?.Text ?? "",
                    Url: item.Links.FirstOrDefault()?.Uri?.AbsoluteUri ?? "",
                    PublishedAt: item.PublishDate != default
                        ? item.PublishDate
                        : item.LastUpdatedTime != default
                            ? item.LastUpdatedTime
                            : DateTimeOffset.UtcNow,
                    Summary: StripHtml(item.Summary?.Text)))
                .OrderByDescending(p => p.PublishedAt)
                .Take(limit)
                .ToList();

            return Result<IReadOnlyList<SubstackPost>>.Success(posts);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch Substack RSS feed from {FeedUrl}", _options.FeedUrl);
            return Result<IReadOnlyList<SubstackPost>>.Failure(
                ErrorCode.InternalError,
                $"Failed to fetch Substack RSS feed: {ex.Message}");
        }
        catch (XmlException ex)
        {
            logger.LogError(ex, "Failed to parse Substack RSS feed from {FeedUrl}", _options.FeedUrl);
            return Result<IReadOnlyList<SubstackPost>>.Failure(
                ErrorCode.InternalError,
                $"Failed to parse Substack RSS feed: {ex.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Substack RSS feed fetch cancelled for {FeedUrl}", _options.FeedUrl);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching Substack RSS feed from {FeedUrl}", _options.FeedUrl);
            return Result<IReadOnlyList<SubstackPost>>.Failure(
                ErrorCode.InternalError,
                $"Unexpected error: {ex.Message}");
        }
    }

    private static bool IsValidSubstackHost(string feedUrl)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.EndsWith(".substack.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("substack.com", StringComparison.OrdinalIgnoreCase));
    }

    private static string? StripHtml(string? html)
    {
        if (html is null) return null;
        var text = HtmlTagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = CollapseWhitespaceRegex().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespaceRegex();
}
