using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed partial class BlogConnector : IPlatformConnector
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptionsMonitor<BlogConnectorOptions> _options;
    private readonly ILogger<BlogConnector> _logger;

    public BlogConnector(
        IProcessRunner processRunner,
        IOptionsMonitor<BlogConnectorOptions> options,
        ILogger<BlogConnector> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
    }

    public Platform Platform => Platform.Blog;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Content.Title, nameof(request.Content.Title));
            ArgumentException.ThrowIfNullOrWhiteSpace(request.TransformedContent, nameof(request.TransformedContent));

            var opts = _options.CurrentValue;
            var slug = GenerateSlug(request.Content.Title);
            if (string.IsNullOrEmpty(slug))
                return new PlatformPublishResult(false, null, null,
                    $"Cannot generate a valid URL slug from title: {request.Content.Title}");

            var filePath = Path.Combine(opts.RepoPath, "posts", $"{slug}.html");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, request.TransformedContent, ct);

            await RunGitAsync(opts, $"add posts/{slug}.html", ct);
            await RunGitCommitAsync(opts, request.Content.Title, ct);
            await RunGitAsync(opts, $"push {opts.RemoteName} {opts.Branch}", ct);

            var url = $"{opts.BaseUrl.TrimEnd('/')}/posts/{slug}";
            _logger.LogInformation("Published blog post {Slug} to {BaseUrl}", slug, opts.BaseUrl);

            return new PlatformPublishResult(true, url, slug, null);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish blog post '{Title}'", request.Content.Title);
            return new PlatformPublishResult(false, null, null, ex.Message);
        }
    }

    public Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        return Task.FromResult(Directory.Exists(opts.RepoPath));
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: int.MaxValue,
        SupportsMarkdown: false,
        SupportsHtml: true,
        SupportsImages: true,
        SupportsScheduling: false,
        SupportsThreads: false,
        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "image/webp"]
    );

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
