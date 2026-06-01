using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed partial class BlogConnector : IPlatformConnector
{
    private const string DefaultCategory = "Enterprise AI";
    private const int ExcerptLength = 160;

    private readonly IProcessRunner _processRunner;
    private readonly IBlogIndexUpdater _indexUpdater;
    private readonly IHeroImageGenerator _heroImageGenerator;
    private readonly IOptionsMonitor<BlogConnectorOptions> _options;
    private readonly ILogger<BlogConnector> _logger;

    public BlogConnector(
        IProcessRunner processRunner,
        IBlogIndexUpdater indexUpdater,
        IHeroImageGenerator heroImageGenerator,
        IOptionsMonitor<BlogConnectorOptions> options,
        ILogger<BlogConnector> logger)
    {
        _processRunner = processRunner;
        _indexUpdater = indexUpdater;
        _heroImageGenerator = heroImageGenerator;
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

            var baseUrl = opts.BaseUrl.TrimEnd('/');

            // 1. Write the post HTML to blog/{slug}.html.
            var postPath = Path.Combine(opts.RepoPath, "blog", $"{slug}.html");
            Directory.CreateDirectory(Path.GetDirectoryName(postPath)!);
            await File.WriteAllTextAsync(postPath, request.TransformedContent, ct);

            // 2. Build post metadata for hero image + index weaving.
            var meta = new BlogPostMeta(
                Slug: slug,
                Title: request.Content.Title,
                Excerpt: BuildExcerpt(request.TransformedContent),
                Category: DefaultCategory,
                Date: DateOnly.FromDateTime(DateTime.UtcNow),
                HeroImagePath: $"assets/blog-images/{slug}.png",
                Url: $"{baseUrl}/blog/{slug}.html");

            // 3. Hero image is best-effort: a missing image only leaves a broken <img>
            //    reference in the template, which must not block the publish.
            var heroResult = await TryGenerateHeroImageAsync(meta, ct);

            // 4. Weave the new post into the static-site index files.
            WeaveIndexFile(opts.RepoPath, "blog.html",
                html => _indexUpdater.InsertIntoBlogListing(html, meta));
            WeaveIndexFile(opts.RepoPath, "index.html",
                html => _indexUpdater.InsertIntoHomepage(html, meta));
            WeaveIndexFile(opts.RepoPath, "sitemap.xml",
                xml => _indexUpdater.InsertIntoSitemap(xml, meta));

            // 5. Stage every changed path that exists on disk, then commit and push.
            var relativePaths = new List<string>
            {
                $"blog/{slug}.html",
                "blog.html",
                "index.html",
                "sitemap.xml"
            };
            if (heroResult.IsSuccess)
                relativePaths.Add($"assets/blog-images/{slug}.png");

            foreach (var relativePath in relativePaths)
            {
                if (File.Exists(Path.Combine(opts.RepoPath, relativePath)))
                    await RunGitAsync(opts, $"add \"{relativePath}\"", ct);
            }

            await RunGitCommitAsync(opts, request.Content.Title, ct);
            await RunGitAsync(opts, $"push {opts.RemoteName} {opts.Branch}", ct);

            var url = $"{baseUrl}/blog/{slug}";
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

    private async Task<Domain.Common.Result<string>> TryGenerateHeroImageAsync(BlogPostMeta meta, CancellationToken ct)
    {
        try
        {
            var result = await _heroImageGenerator.GenerateAsync(meta, ct);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Hero image generation skipped for {Slug}: {Errors}. Publishing without a hero image.",
                    meta.Slug, string.Join("; ", result.Errors));
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Hero image generation failed for {Slug}. Publishing without a hero image.", meta.Slug);
            return Domain.Common.Result<string>.Fail(ex.Message);
        }
    }

    private void WeaveIndexFile(string repoPath, string fileName, Func<string, string> transform)
    {
        var path = Path.Combine(repoPath, fileName);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Index file {FileName} not found at {Path}; skipping weave.", fileName, path);
            return;
        }

        var original = File.ReadAllText(path);
        File.WriteAllText(path, transform(original));
    }

    /// <summary>
    /// Crudely strips HTML/markdown from the post body and returns the first
    /// <see cref="ExcerptLength"/> characters of plain text, truncated on a word
    /// boundary so the excerpt does not end mid-word.
    /// </summary>
    private static string BuildExcerpt(string content)
    {
        var text = HtmlTagRegex().Replace(content, " ");
        text = MarkdownTokenRegex().Replace(text, "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();

        if (text.Length <= ExcerptLength)
            return text;

        var truncated = text[..ExcerptLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > 0)
            truncated = truncated[..lastSpace];

        return truncated.TrimEnd() + "...";
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

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[#*_`>~\[\]\(\)]")]
    private static partial Regex MarkdownTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
