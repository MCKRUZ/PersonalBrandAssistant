using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using PBA.Infrastructure.Transformers;
using Xunit;

namespace PBA.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Guards the no-retry rule on the publish lanes that post to a public timeline.
///
/// On 2026-08-03 the standard resilience handler retried a Buffer <c>createPost</c> that had in fact
/// succeeded but answered slowly. All three attempts landed, and the same clip went out to TikTok
/// three times. A duplicate public post cannot be un-published, so on these clients a retry is
/// strictly worse than a failure — the caller can always retry a failure deliberately.
///
/// These tests exercise the real resilience pipeline and count actual HTTP attempts, rather than
/// reading the configured policy back. A configuration assertion would still pass if the handler's
/// behaviour changed underneath it; only counting attempts can fail for the reason that matters.
/// </summary>
public class PublishRetryPolicyTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CountingHandler _buffer = new();
    private readonly CountingHandler _instagram = new();
    private readonly CountingHandler _retryingControl = new();

    public PublishRetryPolicyTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                ["Publishing:Substack:PublicationSlug"] = "test",
                ["Publishing:Buffer:ApiKey"] = "test",
                ["BlogConnector:RepoPath"] = "/tmp",
                ["BlogConnector:TemplatePath"] = "/tmp/template.html",
                ["ContentTransformer:BaseUrl"] = "https://test.example.com",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase($"PublishRetry_{Guid.NewGuid()}"));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton(Mock.Of<IProcessRunner>());
        services.AddSingleton(Mock.Of<ISidecarClient>());
        services.AddSingleton(Mock.Of<IBackgroundJobClient>());
        services.AddLogging();
        services.AddHttpClient();
        services.Configure<BlogConnectorOptions>(config.GetSection(BlogConnectorOptions.SectionName));

        services.AddPublishingDependencies(config);

        // Swap the transport underneath the real pipelines. Re-registering the same typed client
        // appends to its existing configuration, so the resilience handler under test stays intact.
        services.AddHttpClient<BufferConnector>().ConfigurePrimaryHttpMessageHandler(() => _buffer);
        services.AddHttpClient<InstagramConnector>().ConfigurePrimaryHttpMessageHandler(() => _instagram);

        // Instrument check: a client that DOES retry, so a passing "exactly one attempt" assertion
        // above cannot be an artefact of a miswired stub. Delays are near-zero purely to keep the
        // test fast — this client exists only to prove the counter can observe a retry at all.
        services.AddHttpClient("RetryingControl")
            .ConfigurePrimaryHttpMessageHandler(() => _retryingControl)
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.Retry.Delay = TimeSpan.Zero;
                o.Retry.UseJitter = false;
                o.Retry.BackoffType = Polly.DelayBackoffType.Constant;
            });

        _provider = services.BuildServiceProvider();
    }

    /// <summary>
    /// 500 is a status the default resilience handler retries. If retries were ever re-enabled on
    /// this client, the attempt count would climb and this test would fail — which is the point.
    /// </summary>
    [Fact]
    public async Task BufferClient_ServerError_IsAttemptedExactlyOnce()
    {
        var client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BufferConnector));

        var response = await client.PostAsync("/", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, _buffer.Attempts);
    }

    /// <summary>
    /// Instagram's /media_publish is non-idempotent for the same reason as Buffer's createPost: a
    /// retry after a slow-but-successful call publishes the Reel a second time.
    /// </summary>
    [Fact]
    public async Task InstagramClient_ServerError_IsAttemptedExactlyOnce()
    {
        var client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(InstagramConnector));

        var response = await client.PostAsync(
            "https://graph.facebook.com/v21.0/me/media_publish",
            new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, _instagram.Attempts);
    }

    /// <summary>
    /// Proves the attempt counter actually observes retries. Without this, the two tests above would
    /// pass just as happily against a stub that was never reached.
    /// </summary>
    [Fact]
    public async Task RetryingControl_ServerError_IsAttemptedMoreThanOnce()
    {
        var client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient("RetryingControl");

        await client.PostAsync("https://example.test/", new StringContent("{}"));

        Assert.True(_retryingControl.Attempts > 1,
            $"the counter should observe retries on a retrying client, but saw {_retryingControl.Attempts} attempt(s)");
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    public void Dispose() => _provider.Dispose();
}
