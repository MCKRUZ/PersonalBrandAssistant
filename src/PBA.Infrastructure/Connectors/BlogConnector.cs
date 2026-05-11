using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Connectors;

public sealed partial class BlogConnector : IBlogConnector
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptionsMonitor<BlogConnectorOptions> _options;
    private readonly ILogger<BlogConnector> _logger;
    private readonly MarkdownPipeline _pipeline;

    public BlogConnector(
        IProcessRunner processRunner,
        IOptionsMonitor<BlogConnectorOptions> options,
        ILogger<BlogConnector> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
        _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    }

    public async Task<string> PublishAsync(Content content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Title, nameof(content.Title));
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Body, nameof(content.Body));

        var opts = _options.CurrentValue;

        if (!File.Exists(opts.TemplatePath))
            throw new InvalidOperationException($"Blog template not found: {opts.TemplatePath}");

        var slug = GenerateSlug(content.Title);
        if (string.IsNullOrEmpty(slug))
            throw new InvalidOperationException($"Cannot generate a valid URL slug from title: {content.Title}");

        var template = await File.ReadAllTextAsync(opts.TemplatePath, ct);
        var html = Markdown.ToHtml(content.Body, _pipeline);

        var rendered = template
            .Replace("{{title}}", content.Title)
            .Replace("{{content}}", html)
            .Replace("{{date}}", content.CreatedAt.ToString("yyyy-MM-dd"))
            .Replace("{{author}}", opts.Author)
            .Replace("{{tags}}", string.Join(", ", content.Tags))
            .Replace("{{category}}", content.ContentType.ToString());

        var filePath = Path.Combine(opts.RepoPath, "posts", $"{slug}.html");
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(filePath, rendered, ct);

        await RunGitAsync(opts, $"add posts/{slug}.html", ct: ct);
        await RunGitCommitAsync(opts, content.Title, ct);
        await RunGitAsync(opts, $"push {opts.RemoteName} {opts.Branch}", ct: ct);

        _logger.LogInformation("Published blog post {Slug} to {BaseUrl}", slug, opts.BaseUrl);

        return $"{opts.BaseUrl.TrimEnd('/')}/posts/{slug}";
    }

    private async Task RunGitAsync(BlogConnectorOptions opts, string arguments, CancellationToken ct)
    {
        var result = await _processRunner.RunAsync("git", $"-C \"{opts.RepoPath}\" {arguments}", ct: ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {arguments} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    private async Task RunGitCommitAsync(BlogConnectorOptions opts, string title, CancellationToken ct)
    {
        var result = await _processRunner.RunAsync(
            "git",
            $"-C \"{opts.RepoPath}\" commit --file=-",
            stdinContent: $"publish: {title}",
            ct: ct);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git commit failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    internal static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant();
        slug = SlugInvalidChars().Replace(slug, "");
        slug = SlugWhitespace().Replace(slug, "-");
        slug = SlugConsecutiveHyphens().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex SlugInvalidChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SlugWhitespace();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugConsecutiveHyphens();
}
