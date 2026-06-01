using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;

namespace PBA.Infrastructure.Transformers;

public sealed partial class BlogFormatter : IPlatformFormatter
{
    private const int DescriptionLength = 155;
    private const int WordsPerMinute = 200;

    private readonly IOptionsMonitor<BlogConnectorOptions> _options;
    private readonly ILogger<BlogFormatter> _logger;
    private readonly MarkdownPipeline _pipeline;

    public BlogFormatter(
        IOptionsMonitor<BlogConnectorOptions> options,
        ILogger<BlogFormatter> logger)
    {
        _options = options;
        _logger = logger;
        _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    }

    public Platform Platform => Platform.Blog;

    public async Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        var opts = _options.CurrentValue;

        if (!File.Exists(opts.TemplatePath))
            throw new InvalidOperationException($"Blog template not found: {opts.TemplatePath}");

        var template = await File.ReadAllTextAsync(opts.TemplatePath, ct);

        var html = Markdown.ToHtml(content.Body, _pipeline);
        var plainText = Markdown.ToPlainText(content.Body, _pipeline);

        var slug = BlogConnector.GenerateSlug(content.Title);
        var baseUrl = opts.BaseUrl.TrimEnd('/');
        var canonicalUrl = $"{baseUrl}/blog/{slug}.html";
        var heroImageUrl = $"{baseUrl}/assets/blog-images/{slug}.png";

        var date = content.CreatedAt ?? DateTimeOffset.UtcNow;
        var isoDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var displayDate = date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

        var description = BuildDescription(plainText);
        var readingTime = BuildReadingTime(plainText);

        return template
            .Replace("{{title}}", WebUtility.HtmlEncode(content.Title))
            .Replace("{{description}}", WebUtility.HtmlEncode(description))
            .Replace("{{content}}", html)
            .Replace("{{isoDate}}", isoDate)
            .Replace("{{displayDate}}", WebUtility.HtmlEncode(displayDate))
            .Replace("{{author}}", WebUtility.HtmlEncode(opts.Author))
            .Replace("{{category}}", WebUtility.HtmlEncode(content.ContentType ?? string.Empty))
            .Replace("{{canonicalUrl}}", WebUtility.HtmlEncode(canonicalUrl))
            .Replace("{{heroImageUrl}}", WebUtility.HtmlEncode(heroImageUrl))
            .Replace("{{readingTime}}", WebUtility.HtmlEncode(readingTime));
    }

    private static string BuildDescription(string plainText)
    {
        var collapsed = Whitespace().Replace(plainText, " ").Trim();
        if (collapsed.Length <= DescriptionLength)
            return collapsed;

        return collapsed[..DescriptionLength].TrimEnd() + "...";
    }

    private static string BuildReadingTime(string plainText)
    {
        var wordCount = Whitespace()
            .Split(plainText.Trim())
            .Count(static w => w.Length > 0);
        var minutes = Math.Max(1, (int)Math.Ceiling(wordCount / (double)WordsPerMinute));
        return $"{minutes} min read";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
