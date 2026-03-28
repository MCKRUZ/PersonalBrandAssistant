using System.Text.RegularExpressions;
using Markdig;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

internal sealed class BlogHtmlGenerator : IBlogHtmlGenerator
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex SlugApostropheRegex = new(@"['`]", RegexOptions.Compiled);
    private static readonly Regex SlugStripRegex = new(@"[^a-z0-9\-]", RegexOptions.Compiled);
    private static readonly Regex SlugCollapseRegex = new(@"-{2,}", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly BlogPublishOptions _options;
    private readonly ILogger<BlogHtmlGenerator> _logger;

    public BlogHtmlGenerator(
        IApplicationDbContext db,
        IOptions<BlogPublishOptions> options,
        ILogger<BlogHtmlGenerator> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<BlogHtmlResult>> GenerateAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _db.Contents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<BlogHtmlResult>.NotFound("Content not found");

        var title = content.Title ?? "Untitled";
        var slug = GenerateSlug(content.Title);
        var hash = contentId.ToString("N")[^6..];
        var dateStr = content.CreatedAt.ToString("yyyy-MM-dd");
        var contentPath = _options.ContentPath.TrimEnd('/');
        var filePath = $"{contentPath}/{dateStr}-{slug}-{hash}.html";

        var bodyHtml = Markdown.ToHtml(content.Body ?? string.Empty, Pipeline);
        var canonicalUrl = content.SubstackPostUrl;

        var metaDescription = ExtractMetaDescription(content.Body);
        var template = LoadTemplate();

        var html = template
            .Replace("{{title}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{date}}", dateStr)
            .Replace("{{date_iso}}", content.CreatedAt.ToString("O"))
            .Replace("{{author}}", System.Net.WebUtility.HtmlEncode(_options.AuthorName))
            .Replace("{{meta_description}}", System.Net.WebUtility.HtmlEncode(metaDescription))
            .Replace("{{canonical_url}}", canonicalUrl ?? "")
            .Replace("{{og_title}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{og_description}}", System.Net.WebUtility.HtmlEncode(metaDescription))
            .Replace("{{og_url}}", canonicalUrl ?? "")
            .Replace("{{body}}", bodyHtml);

        _logger.LogInformation("Generated blog HTML for content {ContentId} at {FilePath}", contentId, filePath);

        return Result<BlogHtmlResult>.Success(new BlogHtmlResult(html, filePath, canonicalUrl));
    }

    internal static string GenerateSlug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "untitled";

        var slug = title.ToLowerInvariant();
        slug = SlugApostropheRegex.Replace(slug, "");
        slug = SlugStripRegex.Replace(slug, "-");
        slug = SlugCollapseRegex.Replace(slug, "-");
        slug = slug.Trim('-');

        return string.IsNullOrEmpty(slug) ? "untitled" : slug;
    }

    private static string ExtractMetaDescription(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            var plain = trimmed.Replace("**", "").Replace("*", "").Replace("`", "");
            return plain.Length > 160 ? plain[..157] + "..." : plain;
        }

        return string.Empty;
    }

    private string LoadTemplate()
    {
        try
        {
            if (File.Exists(_options.TemplatePath))
                return File.ReadAllText(_options.TemplatePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load blog template from {Path}, using fallback", _options.TemplatePath);
        }

        return FallbackTemplate;
    }

    private const string FallbackTemplate = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{title}}</title>
            <meta name="description" content="{{meta_description}}" />
            <meta name="author" content="{{author}}" />
            <link rel="canonical" href="{{canonical_url}}" />
            <meta property="og:type" content="article" />
            <meta property="og:title" content="{{og_title}}" />
            <meta property="og:description" content="{{og_description}}" />
            <meta property="og:url" content="{{og_url}}" />
            <meta property="article:published_time" content="{{date_iso}}" />
        </head>
        <body>
            <article>
                <header>
                    <h1>{{title}}</h1>
                    <time datetime="{{date_iso}}">{{date}}</time>
                    <span class="author">{{author}}</span>
                </header>
                <div class="content">
                    {{body}}
                </div>
            </article>
        </body>
        </html>
        """;
}
