using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class ArticleAnalyzer : IArticleAnalyzer
{
    private readonly IApplicationDbContext _db;
    private readonly IArticleScraper _scraper;
    private readonly ISidecarClient _sidecar;
    private readonly ILogger<ArticleAnalyzer> _logger;

    public ArticleAnalyzer(
        IApplicationDbContext db,
        IArticleScraper scraper,
        ISidecarClient sidecar,
        ILogger<ArticleAnalyzer> logger)
    {
        _db = db;
        _scraper = scraper;
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task<Result<AnalysisResult>> AnalyzeAsync(Guid trendItemId, CancellationToken ct)
    {
        var trendItem = await _db.TrendItems.FirstOrDefaultAsync(t => t.Id == trendItemId, ct);
        if (trendItem is null)
            return Result<AnalysisResult>.NotFound($"TrendItem {trendItemId} not found");

        // Cache hit
        if (!string.IsNullOrEmpty(trendItem.Summary))
            return Result<AnalysisResult>.Success(new AnalysisResult(trendItem.Summary, trendItem.ThumbnailUrl));

        if (string.IsNullOrWhiteSpace(trendItem.Url))
            return Result<AnalysisResult>.Failure(ErrorCode.ValidationFailed,
                "TrendItem has no URL to analyze");

        var scrapeResult = await _scraper.ScrapeAsync(trendItem.Url, ct);
        if (!scrapeResult.IsSuccess)
            return Result<AnalysisResult>.Failure(scrapeResult.ErrorCode, scrapeResult.Errors.ToArray());

        var scrape = scrapeResult.Value!;

        // Backfill thumbnail from OG image if not already set
        if (string.IsNullOrEmpty(trendItem.ThumbnailUrl) && !string.IsNullOrEmpty(scrape.ImageUrl))
            trendItem.ThumbnailUrl = scrape.ImageUrl.Length <= 500 ? scrape.ImageUrl : scrape.ImageUrl[..500];

        var prompt = BuildAnalysisPrompt(trendItem.Title, trendItem.SourceName, scrape.Markdown);

        var (analysis, error) = await ConsumeEventStreamAsync(prompt, ct);
        if (error is not null)
        {
            _logger.LogWarning("Article analysis failed for {Id}: {Error}", trendItemId, error);
            return Result<AnalysisResult>.Failure(ErrorCode.InternalError, error);
        }

        trendItem.Summary = analysis;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Article analysis cached for TrendItem {Id}", trendItemId);

        return Result<AnalysisResult>.Success(new AnalysisResult(analysis!, trendItem.ThumbnailUrl));
    }

    private static string BuildAnalysisPrompt(string title, string sourceName, string scrapedMarkdown)
    {
        return $$"""
            You are an expert technology analyst helping a personal brand consultant.
            Analyze the following article and produce a structured analysis in Markdown.

            Article Title: {{title}}
            Source: {{sourceName}}

            Article Content:
            {{scrapedMarkdown}}

            Produce EXACTLY this structure:

            ## TL;DR
            2-3 sentence summary of the key takeaway.

            ## Social Media POV
            - 2-3 angles for LinkedIn posts, tweets, or short-form content
            - Focus on contrarian takes, personal experience hooks, or teaching moments

            ## Business Opportunities
            - Consulting/advisory angles this opens up
            - Products or services that could leverage this trend

            ## Coding Takeaways
            - Specific techniques, patterns, or tools mentioned
            - Agentic AI applications if relevant
            - New ideas applicable to both agentic and regular coding
            - **Repos & Code:** If the article references any GitHub repos, libraries, frameworks, code snippets, or open-source projects, list them with direct links and a one-line description of what's worth exploring or incorporating. Format as: `[repo-name](url) — what to look at`

            ## Key Quotes
            > Notable excerpts worth sharing or referencing (2-3 max)

            Keep the total response under 800 words. Be specific and actionable, not generic.
            """;
    }

    private async Task<(string? Text, string? Error)> ConsumeEventStreamAsync(
        string prompt, CancellationToken ct)
    {
        try
        {
            if (!_sidecar.IsConnected)
                await _sidecar.ConnectAsync(ct);

            string? summaryText = null;
            await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
            {
                switch (evt)
                {
                    case ChatEvent { EventType: "summary", Text: not null } chat:
                        summaryText = chat.Text;
                        break;
                    case ErrorEvent error:
                        return (null, error.Message);
                }
            }

            return (summaryText, summaryText is null ? "No summary received from sidecar" : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consuming sidecar event stream for article analysis");
            return (null, ex.Message);
        }
    }
}
