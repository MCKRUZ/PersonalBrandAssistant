using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Transformers;

public sealed partial class ContentTransformer : IContentTransformer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<TransformerOptions> _options;
    private readonly ILogger<ContentTransformer> _logger;

    public ContentTransformer(
        IServiceProvider serviceProvider,
        IOptionsMonitor<TransformerOptions> options,
        ILogger<ContentTransformer> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct)
    {
        var preprocessed = Preprocess(content);

        var formatter = _serviceProvider.GetKeyedService<IPlatformFormatter>(platform)
            ?? throw new NotSupportedException($"No formatter registered for platform: {platform}");

        _logger.LogDebug("Transforming content '{Title}' for {Platform}", content.Title, platform);

        return await formatter.FormatAsync(preprocessed, ct);
    }

    private PreprocessedContent Preprocess(Content content)
    {
        var body = StripFrontmatter(content.Body);
        var baseUrl = _options.CurrentValue.BaseUrl;
        var (resolvedBody, images) = ResolveImagePaths(body, baseUrl);

        return new PreprocessedContent(
            Title: content.Title,
            Body: resolvedBody,
            CanonicalUrl: null,
            Tags: content.Tags.AsReadOnly(),
            Images: images,
            ContentType: content.ContentType.ToString(),
            CreatedAt: content.CreatedAt
        );
    }

    internal static string StripFrontmatter(string body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith("---"))
            return body;

        var searchFrom = trimmed.IndexOf('\n');
        if (searchFrom < 0)
            return body;

        var endIndex = trimmed.IndexOf("\n---", searchFrom);
        if (endIndex < 0)
            return body;

        return trimmed[(endIndex + 4)..].TrimStart('\r', '\n');
    }

    internal static (string body, IReadOnlyList<ImageReference> images) ResolveImagePaths(
        string body, string baseUrl)
    {
        var images = new List<ImageReference>();

        if (string.IsNullOrEmpty(body))
            return (body, images);

        var resolvedBody = ImagePattern().Replace(body, match =>
        {
            var altText = match.Groups[1].Value;
            var originalPath = match.Groups[2].Value;

            string absoluteUrl;
            if (originalPath.StartsWith("http://") || originalPath.StartsWith("https://"))
            {
                absoluteUrl = originalPath;
            }
            else
            {
                absoluteUrl = $"{baseUrl.TrimEnd('/')}/{originalPath.TrimStart('/')}";
            }

            images.Add(new ImageReference(
                originalPath,
                absoluteUrl,
                string.IsNullOrEmpty(altText) ? null : altText));

            return $"![{altText}]({absoluteUrl})";
        });

        return (resolvedBody, images);
    }

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex ImagePattern();
}
