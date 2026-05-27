using System.Text.RegularExpressions;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed partial class MediumFormatter : IPlatformFormatter
{
    public Platform Platform => Platform.Medium;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        var body = content.Body;

        body = ResolveRelativeImages(body, content.Images);
        body = SvgToPng().Replace(body, ".png)");

        if (!string.IsNullOrEmpty(content.CanonicalUrl))
        {
            var host = new Uri(content.CanonicalUrl).Host;
            body += $"\n\n---\n*Originally published at [{host}]({content.CanonicalUrl})*";
        }

        return Task.FromResult(body);
    }

    private static string ResolveRelativeImages(string body, IReadOnlyList<ImageReference> images)
    {
        foreach (var image in images)
        {
            if (!image.OriginalPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                body = body.Replace(
                    $"({image.OriginalPath})",
                    $"({image.AbsoluteUrl})");
            }
        }
        return body;
    }

    [GeneratedRegex(@"\.svg\)")]
    private static partial Regex SvgToPng();
}
