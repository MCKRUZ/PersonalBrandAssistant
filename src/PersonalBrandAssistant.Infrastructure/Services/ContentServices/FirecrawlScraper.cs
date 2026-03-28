using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class FirecrawlScraper : IArticleScraper
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FirecrawlOptions _options;
    private readonly ILogger<FirecrawlScraper> _logger;

    public FirecrawlScraper(
        IHttpClientFactory httpClientFactory,
        IOptions<FirecrawlOptions> options,
        ILogger<FirecrawlScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ScrapeResult>> ScrapeAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Result<ScrapeResult>.Failure(ErrorCode.ValidationFailed, "URL is required for scraping");

        var client = _httpClientFactory.CreateClient("Firecrawl");

        var payload = new { url, formats = new[] { "markdown" } };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("scrape", payload, ct);
        }
        catch (TaskCanceledException)
        {
            return Result<ScrapeResult>.Failure(ErrorCode.InternalError, "Firecrawl request timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Firecrawl HTTP request failed for {Url}", url);
            return Result<ScrapeResult>.Failure(ErrorCode.InternalError, "Failed to reach Firecrawl API");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Firecrawl returned {StatusCode} for {Url}: {Body}",
                response.StatusCode, url, body);
            return Result<ScrapeResult>.Failure(ErrorCode.InternalError,
                $"Firecrawl returned {(int)response.StatusCode}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        if (!json.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("markdown", out var markdownElement))
        {
            return Result<ScrapeResult>.Failure(ErrorCode.InternalError,
                "Firecrawl response missing data.markdown");
        }

        var markdown = markdownElement.GetString() ?? "";

        if (markdown.Length > _options.MaxContentLength)
            markdown = markdown[.._options.MaxContentLength];

        // Extract OG image from metadata
        string? imageUrl = null;
        if (data.TryGetProperty("metadata", out var metadata))
        {
            if (metadata.TryGetProperty("ogImage", out var ogImage))
                imageUrl = ogImage.GetString();
            else if (metadata.TryGetProperty("og:image", out var ogImage2))
                imageUrl = ogImage2.GetString();
        }

        return Result<ScrapeResult>.Success(new ScrapeResult(markdown, imageUrl));
    }
}
