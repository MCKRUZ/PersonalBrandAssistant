using System.Net;
using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;

namespace PBA.Infrastructure.Transformers;

public sealed class BlogFormatter : IPlatformFormatter
{
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
        var date = (content.CreatedAt ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");

        return template
            .Replace("{{title}}", WebUtility.HtmlEncode(content.Title))
            .Replace("{{content}}", html)
            .Replace("{{date}}", date)
            .Replace("{{author}}", WebUtility.HtmlEncode(opts.Author))
            .Replace("{{tags}}", WebUtility.HtmlEncode(string.Join(", ", content.Tags)))
            .Replace("{{category}}", WebUtility.HtmlEncode(content.ContentType ?? string.Empty));
    }
}
