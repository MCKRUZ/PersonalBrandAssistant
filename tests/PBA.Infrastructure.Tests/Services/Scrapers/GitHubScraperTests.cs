using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Scrapers;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Scrapers;

public class GitHubScraperTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly List<HttpRequestMessage> _requests = new();

    private GitHubScraper Build(GitHubScraperOptions opts)
    {
        var http = new HttpClient(_handler.Object) { BaseAddress = new Uri("https://api.github.com") };
        return new GitHubScraper(http, Options.Create(opts), NullLogger<GitHubScraper>.Instance);
    }

    private void Route(Func<string, string?> responder)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var body = responder(req.RequestUri!.AbsolutePath);
                return body is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            });
    }

    private static IdeaSource Repo(string apiUrl) => new()
    { Name = "gh", Type = IdeaSourceType.GitHub, ApiUrl = apiUrl, Category = "Dev" };

    [Fact]
    public async Task FetchAsync_RepoReleases_MapsItems()
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        Route(path => path == "/repos/dotnet/runtime/releases"
            ? $"[{{\"html_url\":\"https://github.com/dotnet/runtime/releases/tag/v9\",\"name\":\"v9\",\"tag_name\":\"v9\",\"body\":\"notes\",\"published_at\":\"{now}\"}}]"
            : null);
        var scraper = Build(new GitHubScraperOptions());

        var items = await scraper.FetchAsync(Repo("github:repo:dotnet/runtime"), DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Contains("v9", item.Title);
        Assert.Equal("https://github.com/dotnet/runtime/releases/tag/v9", item.Url);
    }

    [Fact]
    public async Task FetchAsync_UserEvents_MapsItems()
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        Route(path => path == "/users/octocat/events/public"
            ? $"[{{\"id\":\"42\",\"type\":\"PushEvent\",\"created_at\":\"{now}\",\"repo\":{{\"name\":\"octocat/hello\"}}}}]"
            : null);
        var scraper = Build(new GitHubScraperOptions());

        var items = await scraper.FetchAsync(Repo("github:user:octocat"), DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Contains("octocat/hello", item.Title);
        Assert.Equal("https://github.com/octocat/hello", item.Url);
    }

    [Fact]
    public async Task FetchAsync_MalformedApiUrl_ReturnsEmpty()
    {
        Route(_ => "[]");
        var scraper = Build(new GitHubScraperOptions());
        Assert.Empty(await scraper.FetchAsync(Repo("not-a-github-url"), DateTimeOffset.UnixEpoch, CancellationToken.None));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task FetchAsync_WithToken_SendsAuthHeader()
    {
        Route(_ => "[]");
        var scraper = Build(new GitHubScraperOptions { Token = "ghp_secret" });
        await scraper.FetchAsync(Repo("github:repo:a/b"), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Contains(_requests, r => r.Headers.Authorization?.Parameter == "ghp_secret");
    }

    [Fact]
    public async Task FetchAsync_NoToken_NoAuthHeader()
    {
        Route(_ => "[]");
        var scraper = Build(new GitHubScraperOptions { Token = "" });
        await scraper.FetchAsync(Repo("github:repo:a/b"), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.All(_requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task FetchAsync_FiltersOlderThanSince()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-30).ToString("o");
        Route(path => path == "/repos/a/b/releases"
            ? $"[{{\"html_url\":\"https://github.com/a/b/releases/tag/v1\",\"name\":\"v1\",\"tag_name\":\"v1\",\"body\":\"\",\"published_at\":\"{old}\"}}]"
            : null);
        var scraper = Build(new GitHubScraperOptions());
        var items = await scraper.FetchAsync(Repo("github:repo:a/b"), DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        Assert.Empty(items);
    }
}
